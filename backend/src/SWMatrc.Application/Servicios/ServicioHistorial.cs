using Microsoft.EntityFrameworkCore;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Dtos;
using SWMatrc.Domain.Entities;

namespace SWMatrc.Application.Servicios;

/// <inheritdoc cref="IServicioHistorial"/>
public sealed class ServicioHistorial(ISwmatrcDbContext db) : IServicioHistorial
{
    public async Task<PaginaDto<EventoDto>> ListarAsync(FiltroHistorial filtro, CancellationToken ct = default)
    {
        var pagina = Math.Max(1, filtro.Pagina);
        var tamano = Math.Clamp(filtro.Tamano, 1, 200);

        var consulta = Filtrar(filtro)
            .Include(e => e.Comunidad)
            .Include(e => e.UsuarioResponsable);

        var total = await consulta.CountAsync(ct);

        var eventos = await consulta
            .OrderByDescending(e => e.FechaInicio)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync(ct);

        return new PaginaDto<EventoDto>
        {
            Elementos = eventos.Select(EventoDto.Desde).ToList(),
            Pagina = pagina,
            TamanoPagina = tamano,
            TotalElementos = total
        };
    }

    public async Task<EstadisticasHistorialDto> EstadisticasAsync(FiltroHistorial filtro, CancellationToken ct = default)
    {
        // Se proyecta solo lo necesario: el agregado se calcula en memoria para no depender
        // de cómo traduzca cada proveedor las agrupaciones por fecha.
        var filas = await Filtrar(filtro)
            .Select(e => new
            {
                e.Fenomeno,
                e.NivelMaximo,
                Comunidad = e.Comunidad!.Nombre,
                e.FechaInicio,
                e.FechaFin
            })
            .ToListAsync(ct);

        var cerrados = filas.Where(f => f.FechaFin is not null).ToList();

        return new EstadisticasHistorialDto
        {
            TotalEventos = filas.Count,
            EventosAbiertos = filas.Count - cerrados.Count,
            DuracionPromedioMinutos = cerrados.Count == 0
                ? null
                : Math.Round(cerrados.Average(f => (f.FechaFin!.Value - f.FechaInicio).TotalMinutes), 1),
            PorFenomeno = filas.GroupBy(f => f.Fenomeno.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            PorNivel = filas.GroupBy(f => f.NivelMaximo.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            PorComunidad = filas.GroupBy(f => f.Comunidad).ToDictionary(g => g.Key, g => g.Count()),
            PorDia = filas
                .GroupBy(f => f.FechaInicio.Date)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key.ToString("yyyy-MM-dd"), g => g.Count())
        };
    }

    private IQueryable<EventoHistorial> Filtrar(FiltroHistorial filtro)
    {
        var consulta = db.Eventos.AsNoTracking().AsQueryable();

        if (filtro.ComunidadId is { } id)
            consulta = consulta.Where(e => e.ComunidadId == id);

        if (filtro.Fenomeno is { } f)
            consulta = consulta.Where(e => e.Fenomeno == f);

        if (filtro.Nivel is { } n)
            consulta = consulta.Where(e => e.NivelMaximo == n);

        if (filtro.Estado is { } estado)
            consulta = consulta.Where(e => e.Estado == estado);

        if (filtro.Desde is { } d)
            consulta = consulta.Where(e => e.FechaInicio >= d);

        // El filtro superior se aplica sobre el inicio del evento para que un episodio
        // todavía abierto siga apareciendo dentro del rango consultado.
        if (filtro.Hasta is { } h)
            consulta = consulta.Where(e => e.FechaInicio <= h);

        return consulta;
    }
}
