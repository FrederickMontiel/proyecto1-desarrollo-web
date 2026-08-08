using SWMatrc.Application.Comun;
using SWMatrc.Application.Dtos;
using SWMatrc.Application.Servicios;
using SWMatrc.Domain.Entities;
using SWMatrc.Domain.Enums;
using SWMatrc.Infrastructure.Persistencia;

namespace SWMatrc.Application.Tests;

/// <summary>
/// Salvaguardas de la administración de cuentas.
///
/// Todas protegen contra el mismo desenlace: dejar la instalación sin nadie que pueda
/// administrarla. Es un error irreversible desde la propia aplicación, así que se impide
/// en el servidor y no solo deshabilitando un botón en la interfaz.
/// </summary>
public class CuentasTests
{
    private static ServicioAutenticacion CrearServicio(SwmatrcDbContext db, int? usuarioEnSesion) =>
        new(db, new HasheadorFalso(), new GeneradorTokenFalso(),
            new UsuarioActualFijo(usuarioEnSesion), new BitacoraMuda());

    private static async Task<Usuario> SembrarAsync(
        SwmatrcDbContext db, string email, RolUsuario rol, bool activo = true)
    {
        var usuario = new Usuario
        {
            NombreCompleto = email,
            Email = email,
            PasswordHash = $"hash:Clave.2026",
            Rol = rol,
            Activo = activo
        };

        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();

        return usuario;
    }

    // ------------------------------------------------------------------ Inicio de sesión

    [Fact]
    public async Task CredencialesCorrectas_devuelvenSesion()
    {
        using var db = Dobles.CrearContexto();
        await SembrarAsync(db, "admin@swmatrc.org", RolUsuario.Administrador);

        var sesion = await CrearServicio(db, null).IniciarSesionAsync(
            new LoginRequest { Email = "admin@swmatrc.org", Password = "Clave.2026" });

        Assert.NotEmpty(sesion.Token);
        Assert.Equal(RolUsuario.Administrador, sesion.Usuario.Rol);
    }

    [Fact]
    public async Task CuentaDeshabilitada_noPuedeIniciarSesion()
    {
        using var db = Dobles.CrearContexto();
        await SembrarAsync(db, "baja@swmatrc.org", RolUsuario.Operador, activo: false);

        await Assert.ThrowsAsync<ExcepcionAutenticacion>(() =>
            CrearServicio(db, null).IniciarSesionAsync(
                new LoginRequest { Email = "baja@swmatrc.org", Password = "Clave.2026" }));
    }

    [Fact]
    public async Task CorreoInexistenteYPasswordIncorrecta_dan_elMismoMensaje()
    {
        // Si las respuestas difirieran, cualquiera podría averiguar qué correos están
        // registrados probando uno por uno.
        using var db = Dobles.CrearContexto();
        await SembrarAsync(db, "existe@swmatrc.org", RolUsuario.Consulta);

        var servicio = CrearServicio(db, null);

        var sinCuenta = await Assert.ThrowsAsync<ExcepcionAutenticacion>(() =>
            servicio.IniciarSesionAsync(new LoginRequest { Email = "nadie@swmatrc.org", Password = "Clave.2026" }));

        var passwordMala = await Assert.ThrowsAsync<ExcepcionAutenticacion>(() =>
            servicio.IniciarSesionAsync(new LoginRequest { Email = "existe@swmatrc.org", Password = "Otra.2026" }));

        Assert.Equal(sinCuenta.Message, passwordMala.Message);
    }

    // ------------------------------------------------------------------ Alta de cuentas

    [Fact]
    public async Task CorreoDuplicado_seRechaza()
    {
        using var db = Dobles.CrearContexto();
        await SembrarAsync(db, "repetido@swmatrc.org", RolUsuario.Consulta);

        await Assert.ThrowsAsync<ExcepcionValidacion>(() =>
            CrearServicio(db, null).RegistrarAsync(new RegistroRequest
            {
                NombreCompleto = "Otra persona",
                Email = "REPETIDO@swmatrc.org", // el correo no distingue mayúsculas
                Password = "Clave.2026",
                Rol = RolUsuario.Operador
            }));
    }

    // ------------------------------------------------------------------ Salvaguardas

    [Fact]
    public async Task NadiePuedeDeshabilitarSuPropiaCuenta()
    {
        using var db = Dobles.CrearContexto();
        var admin = await SembrarAsync(db, "admin@swmatrc.org", RolUsuario.Administrador);
        await SembrarAsync(db, "otro@swmatrc.org", RolUsuario.Administrador);

        var error = await Assert.ThrowsAsync<ExcepcionValidacion>(() =>
            CrearServicio(db, admin.Id).CambiarEstadoAsync(admin.Id, activo: false));

        Assert.Contains("su propia cuenta", error.Message);
        Assert.True(admin.Activo);
    }

    [Fact]
    public async Task NadiePuedeRetirarseASiMismoElRolDeAdministrador()
    {
        using var db = Dobles.CrearContexto();
        var admin = await SembrarAsync(db, "admin@swmatrc.org", RolUsuario.Administrador);
        await SembrarAsync(db, "otro@swmatrc.org", RolUsuario.Administrador);

        var error = await Assert.ThrowsAsync<ExcepcionValidacion>(() =>
            CrearServicio(db, admin.Id).ActualizarAsync(admin.Id, new ActualizarUsuarioRequest
            {
                NombreCompleto = admin.NombreCompleto,
                Rol = RolUsuario.Consulta
            }));

        Assert.Contains("sí mismo", error.Message);
        Assert.Equal(RolUsuario.Administrador, admin.Rol);
    }

    [Fact]
    public async Task ElUltimoAdministradorActivo_noPuedeSerDeshabilitado()
    {
        using var db = Dobles.CrearContexto();
        var unico = await SembrarAsync(db, "unico@swmatrc.org", RolUsuario.Administrador);
        await SembrarAsync(db, "operador@swmatrc.org", RolUsuario.Operador);

        // Lo intenta otra sesión, así que la regla de «no a uno mismo» no aplica aquí.
        var error = await Assert.ThrowsAsync<ExcepcionValidacion>(() =>
            CrearServicio(db, usuarioEnSesion: 99).CambiarEstadoAsync(unico.Id, activo: false));

        Assert.Contains("único administrador activo", error.Message);
        Assert.True(unico.Activo);
    }

    [Fact]
    public async Task ElUltimoAdministradorActivo_noPuedePerderElRol()
    {
        using var db = Dobles.CrearContexto();
        var unico = await SembrarAsync(db, "unico@swmatrc.org", RolUsuario.Administrador);

        await Assert.ThrowsAsync<ExcepcionValidacion>(() =>
            CrearServicio(db, usuarioEnSesion: 99).ActualizarAsync(unico.Id, new ActualizarUsuarioRequest
            {
                NombreCompleto = unico.NombreCompleto,
                Rol = RolUsuario.Operador
            }));

        Assert.Equal(RolUsuario.Administrador, unico.Rol);
    }

    [Fact]
    public async Task ConOtroAdministradorActivo_siSePuedeRetirarElRol()
    {
        using var db = Dobles.CrearContexto();
        var saliente = await SembrarAsync(db, "saliente@swmatrc.org", RolUsuario.Administrador);
        await SembrarAsync(db, "relevo@swmatrc.org", RolUsuario.Administrador);

        var actualizado = await CrearServicio(db, usuarioEnSesion: 99)
            .ActualizarAsync(saliente.Id, new ActualizarUsuarioRequest
            {
                NombreCompleto = "Persona saliente",
                Rol = RolUsuario.Consulta
            });

        Assert.Equal(RolUsuario.Consulta, actualizado.Rol);
        Assert.Equal("Persona saliente", actualizado.NombreCompleto);
    }

    [Fact]
    public async Task UnAdministradorDeshabilitado_noCuentaComoRelevo()
    {
        // Tener otra cuenta con rol de administrador no basta: si está deshabilitada,
        // nadie podría entrar con ella.
        using var db = Dobles.CrearContexto();
        var activo = await SembrarAsync(db, "activo@swmatrc.org", RolUsuario.Administrador);
        await SembrarAsync(db, "suspendido@swmatrc.org", RolUsuario.Administrador, activo: false);

        await Assert.ThrowsAsync<ExcepcionValidacion>(() =>
            CrearServicio(db, usuarioEnSesion: 99).CambiarEstadoAsync(activo.Id, activo: false));
    }

    // ------------------------------------------------------------------ Contraseñas

    [Fact]
    public async Task RestablecerPassword_permiteEntrarConLaNueva()
    {
        using var db = Dobles.CrearContexto();
        var usuario = await SembrarAsync(db, "olvidadizo@swmatrc.org", RolUsuario.Operador);

        var servicio = CrearServicio(db, usuarioEnSesion: 99);
        await servicio.RestablecerPasswordAsync(usuario.Id, "Nueva.Clave.2026");

        var sesion = await servicio.IniciarSesionAsync(
            new LoginRequest { Email = "olvidadizo@swmatrc.org", Password = "Nueva.Clave.2026" });

        Assert.NotEmpty(sesion.Token);
    }

    [Fact]
    public async Task RestablecerPassword_invalidaLaAnterior()
    {
        using var db = Dobles.CrearContexto();
        var usuario = await SembrarAsync(db, "olvidadizo@swmatrc.org", RolUsuario.Operador);

        var servicio = CrearServicio(db, usuarioEnSesion: 99);
        await servicio.RestablecerPasswordAsync(usuario.Id, "Nueva.Clave.2026");

        await Assert.ThrowsAsync<ExcepcionAutenticacion>(() =>
            servicio.IniciarSesionAsync(
                new LoginRequest { Email = "olvidadizo@swmatrc.org", Password = "Clave.2026" }));
    }

    [Fact]
    public async Task OperarSobreUnaCuentaInexistente_devuelveNoEncontrado()
    {
        using var db = Dobles.CrearContexto();

        await Assert.ThrowsAsync<ExcepcionNoEncontrado>(() =>
            CrearServicio(db, usuarioEnSesion: 1).CambiarEstadoAsync(404, activo: false));
    }
}
