using Microsoft.EntityFrameworkCore;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Comun;
using SWMatrc.Application.Dtos;
using SWMatrc.Domain.Entities;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Application.Servicios;

/// <inheritdoc cref="IServicioAlertas"/>
public sealed class ServicioAlertas(
    ISwmatrcDbContext db,
    IUsuarioActual usuarioActual,
    IServicioBitacora bitacora,
    INotificadorTiempoReal notificador) : IServicioAlertas
{
    public async Task<IReadOnlyList<AlertaDto>> ListarActivasAsync(int? comunidadId, CancellationToken ct = default)
    {
        var consulta = ConsultaBase().Where(a => a.FechaCierre == null);

        if (comunidadId is { } id)
            consulta = consulta.Where(a => a.ComunidadId == id);

        var alertas = await consulta
            .OrderByDescending(a => a.Nivel)
            .ThenByDescending(a => a.FechaHora)
            .ToListAsync(ct);

        return alertas.Select(AlertaDto.Desde).ToList();
    }

    public async Task<PaginaDto<AlertaDto>> ListarAsync(
        int pagina, int tamano, int? comunidadId, NivelAlerta? nivel, CancellationToken ct = default)
    {
        pagina = Math.Max(1, pagina);
        tamano = Math.Clamp(tamano, 1, 200);

        var consulta = ConsultaBase();

        if (comunidadId is { } id)
            consulta = consulta.Where(a => a.ComunidadId == id);

        if (nivel is { } n)
            consulta = consulta.Where(a => a.Nivel == n);

        var total = await consulta.CountAsync(ct);

        var alertas = await consulta
            .OrderByDescending(a => a.FechaHora)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync(ct);

        return new PaginaDto<AlertaDto>
        {
            Elementos = alertas.Select(AlertaDto.Desde).ToList(),
            Pagina = pagina,
            TamanoPagina = tamano,
            TotalElementos = total
        };
    }

    public async Task<AlertaDto> ReconocerAsync(int alertaId, CancellationToken ct = default)
    {
        var alerta = await db.Alertas
            .Include(a => a.Comunidad)
            .Include(a => a.Sensor)
            .FirstOrDefaultAsync(a => a.Id == alertaId, ct)
            ?? throw new ExcepcionNoEncontrado("la alerta", alertaId);

        if (alerta.Reconocida)
            throw new ExcepcionValidacion("La alerta ya fue reconocida.");

        alerta.Reconocida = true;
        alerta.ReconocidaPorUsuarioId = usuarioActual.Id;
        alerta.FechaReconocimiento = DateTime.UtcNow;
        alerta.FechaModificacion = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        // Se recarga la navegación para que el DTO lleve el nombre de quien reconoció.
        await db.Usuarios.Where(u => u.Id == usuarioActual.Id).LoadAsync(ct);

        var dto = AlertaDto.Desde(alerta);

        await bitacora.RegistrarAsync("AlertaReconocida", nameof(Alerta), alerta.Id,
            new { Fenomeno = alerta.Fenomeno.ToString(), Nivel = alerta.Nivel.ToString() }, ct);
        await notificador.AlertaGeneradaAsync(dto, ct);

        return dto;
    }

    private IQueryable<Alerta> ConsultaBase() =>
        db.Alertas
            .AsNoTracking()
            .Include(a => a.Comunidad)
            .Include(a => a.Sensor)
            .Include(a => a.ReconocidaPorUsuario);
}
