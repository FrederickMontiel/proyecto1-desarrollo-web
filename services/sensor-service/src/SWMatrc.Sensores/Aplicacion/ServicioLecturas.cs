using Microsoft.EntityFrameworkCore;
using SWMatrc.Comun;
using SWMatrc.Sensores.Datos;
using SWMatrc.Sensores.Dominio;
using SWMatrc.Sensores.Integracion;

namespace SWMatrc.Sensores.Aplicacion;

/// <summary>Consulta histórica de lecturas, para el frontend y para Report Service.</summary>
public sealed class ServicioLecturas(
    SensoresDb db,
    IFuenteComunidades comunidades,
    ServicioSensores sensores,
    IClienteAuditoria auditoria)
{
    /// <summary>Tope de página para el frontend.</summary>
    public const int TamanoMaximoPublico = 500;

    /// <summary>Tope de página para Report Service, que recorre grandes volúmenes por lotes (RNF-REP-05).</summary>
    public const int TamanoMaximoInterno = 5000;

    public async Task<PaginaDto<LecturaHistoricaDto>> ListarAsync(
        FiltroLecturas filtro, int tamanoMaximo = TamanoMaximoPublico, CancellationToken ct = default)
    {
        if (filtro is { Desde: { } desde, Hasta: { } hasta } && desde > hasta)
            throw new ExcepcionValidacion("La fecha 'desde' no puede ser posterior a 'hasta'.");

        var pagina = Math.Max(1, filtro.Pagina);
        var tamano = Math.Clamp(filtro.Tamano, 1, tamanoMaximo);
        var consulta = Filtrar(db.Lecturas.AsNoTracking(), filtro);

        var total = await consulta.CountAsync(ct);
        var filas = await consulta
            .OrderByDescending(l => l.FechaHora).ThenByDescending(l => l.Id)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .Select(l => new { Lectura = l, l.Sensor!.Codigo, l.Sensor.Nombre, l.Sensor.ComunidadId, l.Sensor.Tipo })
            .ToListAsync(ct);

        var catalogo = await comunidades.ListarAsync(ct);
        return new PaginaDto<LecturaHistoricaDto>
        {
            Elementos = filas.Select(f => new LecturaHistoricaDto
            {
                Id = f.Lectura.Id,
                SensorId = f.Lectura.SensorId,
                SensorCodigo = f.Codigo,
                SensorNombre = f.Nombre,
                ComunidadId = f.ComunidadId,
                ComunidadNombre = catalogo?.GetValueOrDefault(f.ComunidadId)?.Nombre ?? string.Empty,
                Tipo = f.Tipo,
                Valor = f.Lectura.Valor,
                UnidadMedida = f.Lectura.UnidadMedida,
                EstadoSensor = f.Lectura.EstadoSensor,
                Nivel = f.Lectura.Nivel,
                FechaHora = f.Lectura.FechaHora
            }).ToList(),
            Pagina = pagina,
            TamanoPagina = tamano,
            TotalElementos = total
        };
    }

    /// <summary>Registro manual: mismo camino que forzar un valor (valida, evalúa y difunde).</summary>
    public Task<LecturaHistoricaDto> RegistrarAsync(RegistrarLecturaRequest request, CancellationToken ct = default) =>
        sensores.EstablecerValorAsync(request.SensorId!.Value, request.Valor!.Value, ct);

    /// <summary>Elimina una lectura errónea. Queda en la auditoría.</summary>
    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        var lectura = await db.Lecturas.Include(l => l.Sensor).FirstOrDefaultAsync(l => l.Id == id, ct)
                      ?? throw new ExcepcionNoEncontrado("la lectura", id);

        db.Lecturas.Remove(lectura);
        await db.SaveChangesAsync(ct);

        auditoria.Registrar("LecturaEliminada", nameof(Lectura), id,
            new { Sensor = lectura.Sensor?.Codigo, lectura.Valor, lectura.FechaHora });
    }

    /// <summary>
    /// Promedio, mínimo, máximo y suma por tipo de sensor en un periodo (RF-REP-07). Se
    /// calcula en SQL Server: con cientos de miles de lecturas no tiene sentido traerlas.
    /// </summary>
    public async Task<IReadOnlyList<EstadisticaTipoDto>> EstadisticasAsync(
        int? comunidadId, DateTime? desde, DateTime? hasta, CancellationToken ct = default)
    {
        var consulta = Filtrar(db.Lecturas.AsNoTracking(),
            new FiltroLecturas { ComunidadId = comunidadId, Desde = desde, Hasta = hasta });

        var filas = await consulta
            .GroupBy(l => new { l.Sensor!.Tipo, l.UnidadMedida })
            .Select(g => new
            {
                g.Key.Tipo,
                g.Key.UnidadMedida,
                Lecturas = g.Count(),
                Promedio = g.Average(l => l.Valor),
                Minimo = g.Min(l => l.Valor),
                Maximo = g.Max(l => l.Valor),
                Suma = g.Sum(l => l.Valor)
            })
            .ToListAsync(ct);

        return filas
            .OrderBy(f => f.Tipo)
            .Select(f => new EstadisticaTipoDto(f.Tipo, f.UnidadMedida, f.Lecturas,
                Math.Round(f.Promedio, 3), f.Minimo, f.Maximo, f.Suma))
            .ToList();
    }

    internal static IQueryable<Lectura> Filtrar(IQueryable<Lectura> consulta, FiltroLecturas filtro)
    {
        if (filtro.SensorId is { } sensorId)
            consulta = consulta.Where(l => l.SensorId == sensorId);
        if (filtro.ComunidadId is { } comunidadId)
            consulta = consulta.Where(l => l.Sensor!.ComunidadId == comunidadId);
        if (filtro.Tipo is { } tipo)
            consulta = consulta.Where(l => l.Sensor!.Tipo == tipo);
        if (filtro.Desde is { } desde)
            consulta = consulta.Where(l => l.FechaHora >= desde);
        if (filtro.Hasta is { } hasta)
            consulta = consulta.Where(l => l.FechaHora <= hasta);
        return consulta;
    }

    internal static LecturaHistoricaDto AHistorica(Lectura l, Sensor s, string? comunidadNombre) => new()
    {
        Id = l.Id,
        SensorId = s.Id,
        SensorCodigo = s.Codigo,
        SensorNombre = s.Nombre,
        ComunidadId = s.ComunidadId,
        ComunidadNombre = comunidadNombre ?? string.Empty,
        Tipo = s.Tipo,
        Valor = l.Valor,
        UnidadMedida = l.UnidadMedida,
        EstadoSensor = l.EstadoSensor,
        Nivel = l.Nivel,
        FechaHora = l.FechaHora
    };
}
