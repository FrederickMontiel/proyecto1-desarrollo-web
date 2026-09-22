using Microsoft.EntityFrameworkCore;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Comun;
using SWMatrc.Application.Dtos;
using SWMatrc.Domain.Entities;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Application.Servicios;

/// <inheritdoc cref="IServicioAutenticacion"/>
public sealed class ServicioAutenticacion(
    ISwmatrcDbContext db,
    IHasheadorPassword hasheador,
    IGeneradorToken generadorToken,
    IUsuarioActual usuarioActual,
    IServicioBitacora bitacora) : IServicioAutenticacion
{
    public async Task<SesionDto> IniciarSesionAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Email == email, ct);

        // Mismo mensaje para usuario inexistente y contraseña incorrecta: no se revela
        // cuáles correos están registrados.
        if (usuario is null || !hasheador.Verificar(request.Password, usuario.PasswordHash))
            throw new ExcepcionAutenticacion("Credenciales inválidas.");

        if (!usuario.Activo)
            throw new ExcepcionAutenticacion("La cuenta está deshabilitada. Contacte al administrador.");

        usuario.UltimoAcceso = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var (token, expira) = generadorToken.Generar(usuario);

        // El contexto de esta petición es anónimo —el token acaba de emitirse—, así que
        // el autor se indica de forma explícita para que la auditoría no diga "sistema".
        await bitacora.RegistrarComoAsync(usuario.Id, usuario.Email,
            "InicioSesion", nameof(Usuario), usuario.Id, new { Rol = usuario.Rol.ToString() }, ct);

        return new SesionDto { Token = token, Expira = expira, Usuario = UsuarioDto.Desde(usuario) };
    }

    public async Task<UsuarioDto> RegistrarAsync(RegistroRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await db.Usuarios.AnyAsync(u => u.Email == email, ct))
            throw new ExcepcionValidacion($"Ya existe un usuario registrado con el correo '{email}'.");

        var usuario = new Usuario
        {
            NombreCompleto = request.NombreCompleto.Trim(),
            Email = email,
            PasswordHash = hasheador.Hashear(request.Password),
            Rol = request.Rol,
            Activo = true
        };

        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("UsuarioCreado", nameof(Usuario), usuario.Id, new { usuario.Email, Rol = usuario.Rol.ToString() }, ct);

        return UsuarioDto.Desde(usuario);
    }

    public async Task CerrarSesionAsync(CancellationToken ct = default)
    {
        if (usuarioActual.Id is not { } id)
            return;

        await bitacora.RegistrarAsync("CierreSesion", nameof(Usuario), id, null, ct);
    }

    public async Task<IReadOnlyList<UsuarioDto>> ListarUsuariosAsync(FiltroUsuarios? filtro = null, CancellationToken ct = default)
    {
        var consulta = db.Usuarios.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filtro?.Busqueda))
        {
            var texto = filtro.Busqueda.Trim();
            consulta = consulta.Where(u => u.NombreCompleto.Contains(texto) || u.Email.Contains(texto));
        }

        if (filtro?.Rol is { } rol)
            consulta = consulta.Where(u => u.Rol == rol);

        if (filtro?.Activo is { } activo)
            consulta = consulta.Where(u => u.Activo == activo);

        var usuarios = await consulta.OrderBy(u => u.NombreCompleto).ToListAsync(ct);
        return usuarios.Select(UsuarioDto.Desde).ToList();
    }

    public async Task<UsuarioDto> CambiarEstadoAsync(int usuarioId, bool activo, CancellationToken ct = default)
    {
        var usuario = await CargarAsync(usuarioId, ct);

        if (!activo)
        {
            if (usuario.Id == usuarioActual.Id)
                throw new ExcepcionValidacion("No puede deshabilitar su propia cuenta.");

            await AsegurarQueQuedaAlgunAdministradorAsync(usuario, rolFuturo: usuario.Rol, activoFuturo: false, ct);
        }

        usuario.Activo = activo;
        usuario.FechaModificacion = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync(activo ? "UsuarioHabilitado" : "UsuarioDeshabilitado",
            nameof(Usuario), usuario.Id, new { usuario.Email }, ct);

        return UsuarioDto.Desde(usuario);
    }

    public async Task<UsuarioDto> ActualizarAsync(
        int usuarioId, ActualizarUsuarioRequest request, CancellationToken ct = default)
    {
        var usuario = await CargarAsync(usuarioId, ct);

        // Quitarse a uno mismo el rol de administrador deja la sesión en curso con un token
        // que ya no corresponde a los permisos reales: se bloquea antes de que ocurra.
        if (usuario.Id == usuarioActual.Id
            && usuario.Rol == RolUsuario.Administrador
            && request.Rol != RolUsuario.Administrador)
        {
            throw new ExcepcionValidacion("No puede retirarse a sí mismo el rol de administrador.");
        }

        await AsegurarQueQuedaAlgunAdministradorAsync(usuario, request.Rol, usuario.Activo, ct);

        var rolAnterior = usuario.Rol;
        usuario.NombreCompleto = request.NombreCompleto.Trim();
        usuario.Rol = request.Rol;
        usuario.FechaModificacion = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("UsuarioActualizado", nameof(Usuario), usuario.Id,
            new { usuario.Email, RolAnterior = rolAnterior.ToString(), RolNuevo = usuario.Rol.ToString() }, ct);

        return UsuarioDto.Desde(usuario);
    }

    public async Task RestablecerPasswordAsync(
        int usuarioId, string passwordNueva, CancellationToken ct = default)
    {
        var usuario = await CargarAsync(usuarioId, ct);

        usuario.PasswordHash = hasheador.Hashear(passwordNueva);
        usuario.FechaModificacion = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        // La contraseña nunca se registra, ni siquiera hasheada: la bitácora deja constancia
        // del hecho, no del secreto.
        await bitacora.RegistrarAsync("PasswordRestablecida", nameof(Usuario), usuario.Id,
            new { usuario.Email }, ct);
    }

    private async Task<Usuario> CargarAsync(int usuarioId, CancellationToken ct) =>
        await db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId, ct)
        ?? throw new ExcepcionNoEncontrado("el usuario", usuarioId);

    /// <summary>
    /// Impide dejar la instalación sin ningún administrador activo. Sin esta comprobación
    /// una sola operación podría cerrar el acceso a la administración de forma irreversible.
    /// </summary>
    private async Task AsegurarQueQuedaAlgunAdministradorAsync(
        Usuario usuario, RolUsuario rolFuturo, bool activoFuturo, CancellationToken ct)
    {
        var seguiraSiendoAdministradorActivo = rolFuturo == RolUsuario.Administrador && activoFuturo;
        if (seguiraSiendoAdministradorActivo)
            return;

        var eraAdministradorActivo = usuario.Rol == RolUsuario.Administrador && usuario.Activo;
        if (!eraAdministradorActivo)
            return;

        var otrosAdministradores = await db.Usuarios.CountAsync(
            u => u.Id != usuario.Id && u.Rol == RolUsuario.Administrador && u.Activo, ct);

        if (otrosAdministradores == 0)
            throw new ExcepcionValidacion(
                "Es el único administrador activo. Designe otro antes de retirarle el rol o deshabilitarlo.");
    }
}
