using Microsoft.EntityFrameworkCore;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Dtos;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Application.Servicios;

/// <inheritdoc cref="IServicioHistorial"/>
public sealed class ServicioHistorial(ISwmatrcDbContext db) : IServicioHistorial
{
    public async Task<PaginaDto<EventoDto>> ListarAsync(
        int pagina, int tamano, int? comunidadId, TipoFenomeno? fenomeno,
        DateTime? desde, DateTime? hasta, CancellationToken ct = default)
    {
        pagina = Math.Max(1, pagina);
        tamano = Math.Clamp(tamano, 1, 200);

        var consulta = db.Eventos
            .AsNoTracking()
            .Include(e => e.Comunidad)
            .AsQueryable();

        if (comunidadId is { } id)
            consulta = consulta.Where(e => e.ComunidadId == id);

        if (fenomeno is { } f)
            consulta = consulta.Where(e => e.Fenomeno == f);

        if (desde is { } d)
            consulta = consulta.Where(e => e.FechaInicio >= d);

        // El filtro superior se aplica sobre el inicio del evento para que un episodio
        // todavía abierto siga apareciendo dentro del rango consultado.
        if (hasta is { } h)
            consulta = consulta.Where(e => e.FechaInicio <= h);

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
}
