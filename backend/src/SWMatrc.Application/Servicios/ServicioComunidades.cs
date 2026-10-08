using Microsoft.EntityFrameworkCore;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Comun;
using SWMatrc.Application.Dtos;
using SWMatrc.Domain.Entities;

namespace SWMatrc.Application.Servicios;

/// <inheritdoc cref="IServicioComunidades"/>
public sealed class ServicioComunidades(
    ISwmatrcDbContext db,
    IServicioBitacora bitacora,
    IServicioMonitoreo monitoreo) : IServicioComunidades
{
    public async Task<IReadOnlyList<ComunidadDto>> ListarAsync(FiltroComunidades filtro, CancellationToken ct = default)
    {
        var consulta = db.Comunidades.AsNoTracking().Include(c => c.Sensores).AsQueryable();

        if (!string.IsNullOrWhiteSpace(filtro.Busqueda))
        {
            var texto = filtro.Busqueda.Trim();
            consulta = consulta.Where(c =>
                c.Nombre.Contains(texto) || c.Municipio.Contains(texto) ||
                c.Departamento.Contains(texto) || c.Pais.Contains(texto));
        }

        if (filtro.Activa is { } activa)
            consulta = consulta.Where(c => c.Activa == activa);

        if (!string.IsNullOrWhiteSpace(filtro.Municipio))
            consulta = consulta.Where(c => c.Municipio == filtro.Municipio.Trim());

        if (!string.IsNullOrWhiteSpace(filtro.Departamento))
            consulta = consulta.Where(c => c.Departamento == filtro.Departamento.Trim());

        var comunidades = await consulta.OrderBy(c => c.Nombre).ToListAsync(ct);
        return comunidades.Select(ComunidadDto.Desde).ToList();
    }

    public async Task<ComunidadDto> ObtenerAsync(int id, CancellationToken ct = default) =>
        ComunidadDto.Desde(await CargarAsync(id, ct));

    public async Task<ComunidadDto> CrearAsync(GuardarComunidadRequest request, CancellationToken ct = default)
    {
        var nombre = request.Nombre.Trim();
        await ValidarNombreUnicoAsync(nombre, null, ct);

        var comunidad = new Comunidad();
        Aplicar(comunidad, request);

        db.Comunidades.Add(comunidad);
        await db.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("ComunidadCreada", nameof(Comunidad), comunidad.Id,
            new { comunidad.Nombre, comunidad.Municipio, comunidad.Departamento }, ct);

        return ComunidadDto.Desde(comunidad);
    }

    public async Task<ComunidadDto> ActualizarAsync(int id, GuardarComunidadRequest request, CancellationToken ct = default)
    {
        var comunidad = await CargarAsync(id, ct);
        await ValidarNombreUnicoAsync(request.Nombre.Trim(), id, ct);

        var estabaActiva = comunidad.Activa;
        Aplicar(comunidad, request);
        await db.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("ComunidadActualizada", nameof(Comunidad), comunidad.Id,
            new { comunidad.Nombre }, ct);

        if (estabaActiva != comunidad.Activa)
            await RegistrarCambioEstadoAsync(comunidad, ct);

        return ComunidadDto.Desde(comunidad);
    }

    public async Task<ComunidadDto> CambiarEstadoAsync(int id, bool activa, CancellationToken ct = default)
    {
        var comunidad = await CargarAsync(id, ct);
        if (comunidad.Activa == activa)
            return ComunidadDto.Desde(comunidad);

        comunidad.Activa = activa;
        await db.SaveChangesAsync(ct);
        await RegistrarCambioEstadoAsync(comunidad, ct);

        return ComunidadDto.Desde(comunidad);
    }

    private async Task RegistrarCambioEstadoAsync(Comunidad comunidad, CancellationToken ct)
    {
        await bitacora.RegistrarAsync(comunidad.Activa ? "ComunidadActivada" : "ComunidadDesactivada",
            nameof(Comunidad), comunidad.Id, new { comunidad.Nombre }, ct);

        // Se reevalúa en el acto: al desactivarla se cierran sus alertas abiertas y al
        // reactivarla el motor la diagnostica sin esperar al siguiente ciclo.
        await monitoreo.EvaluarComunidadAsync(comunidad.Id, ct);
    }

    private static void Aplicar(Comunidad comunidad, GuardarComunidadRequest request)
    {
        comunidad.Nombre = request.Nombre.Trim();
        comunidad.Municipio = request.Municipio.Trim();
        comunidad.Departamento = request.Departamento.Trim();
        comunidad.Pais = request.Pais.Trim();
        comunidad.Descripcion = string.IsNullOrWhiteSpace(request.Descripcion) ? null : request.Descripcion.Trim();
        comunidad.Latitud = request.Latitud;
        comunidad.Longitud = request.Longitud;
        comunidad.Poblacion = request.Poblacion;
        comunidad.Activa = request.Activa;
    }

    private async Task ValidarNombreUnicoAsync(string nombre, int? excluirId, CancellationToken ct)
    {
        if (await db.Comunidades.AnyAsync(c => c.Nombre == nombre && c.Id != excluirId, ct))
            throw new ExcepcionValidacion($"Ya existe una comunidad llamada '{nombre}'.");
    }

    private async Task<Comunidad> CargarAsync(int id, CancellationToken ct) =>
        await db.Comunidades.Include(c => c.Sensores).FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw new ExcepcionNoEncontrado("la comunidad", id);
}
