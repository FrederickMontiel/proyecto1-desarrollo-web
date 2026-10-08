using Microsoft.EntityFrameworkCore;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Comun;
using SWMatrc.Application.Dtos;
using SWMatrc.Domain.Entities;

namespace SWMatrc.Application.Servicios;

/// <inheritdoc cref="IServicioLecturas"/>
public sealed class ServicioLecturas(
    ISwmatrcDbContext db,
    IServicioSensores sensores,
    IServicioBitacora bitacora) : IServicioLecturas
{
    public async Task<PaginaDto<LecturaHistoricaDto>> ListarAsync(FiltroLecturas filtro, CancellationToken ct = default)
    {
        var pagina = Math.Max(1, filtro.Pagina);
        var tamano = Math.Clamp(filtro.Tamano, 1, 500);

        var consulta = db.Lecturas
            .AsNoTracking()
            .Include(l => l.Sensor!).ThenInclude(s => s.Comunidad)
            .AsQueryable();

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

        var total = await consulta.CountAsync(ct);

        var lecturas = await consulta
            .OrderByDescending(l => l.FechaHora)
            .ThenByDescending(l => l.Id)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync(ct);

        return new PaginaDto<LecturaHistoricaDto>
        {
            Elementos = lecturas.Select(LecturaHistoricaDto.Desde).ToList(),
            Pagina = pagina,
            TamanoPagina = tamano,
            TotalElementos = total
        };
    }

    public async Task<LecturaHistoricaDto> RegistrarAsync(RegistrarLecturaRequest request, CancellationToken ct = default)
    {
        // Reutiliza el camino de la inyección manual: valida el rango, rechaza sensores
        // inactivos, evalúa el riesgo y difunde la lectura en tiempo real.
        await sensores.EstablecerValorAsync(request.SensorId, request.Valor, ct);

        var lectura = await db.Lecturas
            .AsNoTracking()
            .Include(l => l.Sensor!).ThenInclude(s => s.Comunidad)
            .Where(l => l.SensorId == request.SensorId)
            .OrderByDescending(l => l.Id)
            .FirstAsync(ct);

        return LecturaHistoricaDto.Desde(lectura);
    }

    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        var lectura = await db.Lecturas.Include(l => l.Sensor).FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new ExcepcionNoEncontrado("la lectura", id);

        db.Lecturas.Remove(lectura);
        await db.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("LecturaEliminada", nameof(Lectura), id,
            new { Sensor = lectura.Sensor?.Codigo, lectura.Valor, lectura.FechaHora }, ct);
    }
}
