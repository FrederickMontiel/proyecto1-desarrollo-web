using Microsoft.EntityFrameworkCore;
using SWMatrc.Comun;
using SWMatrc.Community.Datos;
using SWMatrc.Community.Dominio;
using SWMatrc.Community.Integracion;

namespace SWMatrc.Community.Aplicacion;

public sealed class ServicioComunidades(
    ComunidadesDb db,
    IFuenteConteoSensores sensores,
    IClienteAuditoria auditoria)
{
    public async Task<IReadOnlyList<ComunidadDto>> ListarAsync(FiltroComunidades filtro, CancellationToken ct = default)
    {
        var comunidades = await Filtrar(db.Comunidades.AsNoTracking(), filtro)
            .OrderBy(c => c.Nombre)
            .ToListAsync(ct);

        var conteos = await sensores.ObtenerAsync(ct);
        return comunidades.Select(c => ComunidadDto.Desde(c, conteos)).ToList();
    }

    public async Task<ComunidadDto> ObtenerAsync(int id, CancellationToken ct = default) =>
        ComunidadDto.Desde(await CargarAsync(id, ct), await sensores.ObtenerAsync(ct));

    public async Task<ComunidadDto> CrearAsync(GuardarComunidadRequest request, CancellationToken ct = default)
    {
        var comunidad = new Comunidad();
        Aplicar(comunidad, request);
        await ValidarNombreUnicoAsync(comunidad.Nombre, null, ct);

        db.Comunidades.Add(comunidad);
        await db.SaveChangesAsync(ct);

        auditoria.Registrar("ComunidadCreada", nameof(Comunidad), comunidad.Id,
            new { comunidad.Nombre, comunidad.Municipio, comunidad.Departamento });

        return ComunidadDto.Desde(comunidad, await sensores.ObtenerAsync(ct));
    }

    public async Task<ComunidadDto> ActualizarAsync(int id, GuardarComunidadRequest request, CancellationToken ct = default)
    {
        var comunidad = await CargarAsync(id, ct);
        var estabaActiva = comunidad.Activa;

        Aplicar(comunidad, request);
        await ValidarNombreUnicoAsync(comunidad.Nombre, id, ct);
        await db.SaveChangesAsync(ct);

        auditoria.Registrar("ComunidadActualizada", nameof(Comunidad), comunidad.Id, new { comunidad.Nombre });
        if (estabaActiva != comunidad.Activa)
            RegistrarCambioEstado(comunidad);

        return ComunidadDto.Desde(comunidad, await sensores.ObtenerAsync(ct));
    }

    /// <summary>
    /// Activa o desactiva una comunidad. Sensor Service consulta el estado en cada ciclo de
    /// simulación, así que deja de generar lecturas para ella en cuestión de segundos.
    /// </summary>
    public async Task<ComunidadDto> CambiarEstadoAsync(int id, bool activa, CancellationToken ct = default)
    {
        var comunidad = await CargarAsync(id, ct);
        if (comunidad.Activa != activa)
        {
            comunidad.Activa = activa;
            await db.SaveChangesAsync(ct);
            RegistrarCambioEstado(comunidad);
        }

        return ComunidadDto.Desde(comunidad, await sensores.ObtenerAsync(ct));
    }

    /// <summary>Lista compacta para otros servicios; no depende del Sensor Service.</summary>
    public async Task<IReadOnlyList<ComunidadInternaDto>> ListarInternoAsync(bool? activa, CancellationToken ct = default)
    {
        var consulta = db.Comunidades.AsNoTracking();
        if (activa is { } valor)
            consulta = consulta.Where(c => c.Activa == valor);

        return await consulta.OrderBy(c => c.Id).Select(c => ComunidadInternaDto.Desde(c)).ToListAsync(ct);
    }

    public async Task<ComunidadInternaDto> ObtenerInternoAsync(int id, CancellationToken ct = default) =>
        ComunidadInternaDto.Desde(await CargarAsync(id, ct));

    /// <summary>
    /// Las comunidades como GeoJSON (RFC 7946), listo para pintar en Leaflet u OpenLayers.
    /// GeoJSON ordena las coordenadas como [longitud, latitud].
    /// </summary>
    public async Task<object> GeoJsonAsync(FiltroComunidades filtro, CancellationToken ct = default)
    {
        var comunidades = await ListarAsync(filtro, ct);
        return new
        {
            type = "FeatureCollection",
            features = comunidades.Select(c => new
            {
                type = "Feature",
                id = c.Id,
                geometry = new { type = "Point", coordinates = new[] { c.Longitud, c.Latitud } },
                properties = new
                {
                    c.Nombre,
                    c.Municipio,
                    c.Departamento,
                    c.Pais,
                    c.Poblacion,
                    c.Activa,
                    c.TotalSensores,
                    c.SensoresActivos
                }
            })
        };
    }

    internal static IQueryable<Comunidad> Filtrar(IQueryable<Comunidad> consulta, FiltroComunidades filtro)
    {
        if (!string.IsNullOrWhiteSpace(filtro.Busqueda))
        {
            // ToLower + Contains se traduce a lower(...) LIKE en PostgreSQL: la búsqueda no
            // distingue mayúsculas, igual que en SQL Server con la intercalación de la Fase 2.
            var texto = filtro.Busqueda.Trim().ToLower();
            consulta = consulta.Where(c =>
                c.Nombre.ToLower().Contains(texto) || c.Municipio.ToLower().Contains(texto) ||
                c.Departamento.ToLower().Contains(texto) || c.Pais.ToLower().Contains(texto));
        }

        if (filtro.Activa is { } activa)
            consulta = consulta.Where(c => c.Activa == activa);

        if (!string.IsNullOrWhiteSpace(filtro.Municipio))
        {
            var municipio = filtro.Municipio.Trim().ToLower();
            consulta = consulta.Where(c => c.Municipio.ToLower() == municipio);
        }

        if (!string.IsNullOrWhiteSpace(filtro.Departamento))
        {
            var departamento = filtro.Departamento.Trim().ToLower();
            consulta = consulta.Where(c => c.Departamento.ToLower() == departamento);
        }

        return consulta;
    }

    private void RegistrarCambioEstado(Comunidad comunidad) =>
        auditoria.Registrar(comunidad.Activa ? "ComunidadActivada" : "ComunidadDesactivada",
            nameof(Comunidad), comunidad.Id, new { comunidad.Nombre });

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
        var normalizado = nombre.ToLower();
        if (await db.Comunidades.AnyAsync(c => c.Nombre.ToLower() == normalizado && c.Id != excluirId, ct))
            throw new ExcepcionConflicto($"Ya existe una comunidad llamada '{nombre}'.");
    }

    private async Task<Comunidad> CargarAsync(int id, CancellationToken ct) =>
        await db.Comunidades.FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw new ExcepcionNoEncontrado("la comunidad", id);
}
