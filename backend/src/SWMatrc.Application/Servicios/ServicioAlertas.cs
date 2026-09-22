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

    public async Task<PaginaDto<AlertaDto>> ListarAsync(FiltroAlertas filtro, CancellationToken ct = default)
    {
        var pagina = Math.Max(1, filtro.Pagina);
        var tamano = Math.Clamp(filtro.Tamano, 1, 200);

        var consulta = ConsultaBase();

        if (filtro.ComunidadId is { } comunidadId)
            consulta = consulta.Where(a => a.ComunidadId == comunidadId);

        if (filtro.SensorId is { } sensorId)
            consulta = consulta.Where(a => a.SensorId == sensorId);

        if (filtro.Fenomeno is { } fenomeno)
            consulta = consulta.Where(a => a.Fenomeno == fenomeno);

        if (filtro.Nivel is { } nivel)
            consulta = consulta.Where(a => a.Nivel == nivel);

        if (filtro.Estado is { } estado)
            consulta = consulta.Where(a => a.Estado == estado);

        if (filtro.Desde is { } desde)
            consulta = consulta.Where(a => a.FechaHora >= desde);

        if (filtro.Hasta is { } hasta)
            consulta = consulta.Where(a => a.FechaHora <= hasta);

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

    public async Task<AlertaDto> ObtenerAsync(int alertaId, CancellationToken ct = default)
    {
        var alerta = await ConsultaBase().FirstOrDefaultAsync(a => a.Id == alertaId, ct)
            ?? throw new ExcepcionNoEncontrado("la alerta", alertaId);

        return AlertaDto.Desde(alerta);
    }

    public async Task<AlertaDto> AtenderAsync(int alertaId, string? comentario = null, CancellationToken ct = default)
    {
        var alerta = await CargarAsync(alertaId, ct);

        if (alerta.Estado == EstadoAlerta.Cerrada)
            throw new ExcepcionValidacion("La alerta ya está cerrada.");

        if (alerta.Estado == EstadoAlerta.Atendida)
            throw new ExcepcionValidacion("La alerta ya fue atendida.");

        var ahora = DateTime.UtcNow;
        alerta.Atender(usuarioActual.Id, ahora);
        await SincronizarEventoAsync(alerta, EstadoAlerta.Atendida, null, ct);
        await db.SaveChangesAsync(ct);

        var dto = await RecargarAsync(alerta.Id, ct);

        await bitacora.RegistrarAsync("AlertaAtendida", nameof(Alerta), alerta.Id,
            new { Fenomeno = alerta.Fenomeno.ToString(), Nivel = alerta.Nivel.ToString(), Comentario = comentario }, ct);
        await notificador.AlertaGeneradaAsync(dto, ct);

        return dto;
    }

    /// <remarks>
    /// Cerrar a mano da por terminado el episodio. Si la condición de riesgo persiste, el
    /// motor abrirá una alerta nueva en el siguiente ciclo: el cierre no silencia el riesgo.
    /// </remarks>
    public async Task<AlertaDto> CerrarAsync(int alertaId, string? comentario = null, CancellationToken ct = default)
    {
        var alerta = await CargarAsync(alertaId, ct);

        if (alerta.Estado == EstadoAlerta.Cerrada)
            throw new ExcepcionValidacion("La alerta ya está cerrada.");

        var ahora = DateTime.UtcNow;
        alerta.Cerrar(usuarioActual.Id, ahora);
        await SincronizarEventoAsync(alerta, EstadoAlerta.Cerrada, ahora, ct);
        await db.SaveChangesAsync(ct);

        var dto = await RecargarAsync(alerta.Id, ct);

        await bitacora.RegistrarAsync("AlertaCerrada", nameof(Alerta), alerta.Id,
            new { Fenomeno = alerta.Fenomeno.ToString(), Nivel = alerta.Nivel.ToString(), Comentario = comentario }, ct);
        await notificador.AlertaCerradaAsync(dto, ct);

        return dto;
    }

    /// <summary>Traslada el estado y el responsable al asiento de historial de la alerta.</summary>
    private async Task SincronizarEventoAsync(Alerta alerta, EstadoAlerta estado, DateTime? fin, CancellationToken ct)
    {
        var evento = await db.Eventos
            .Where(e => e.AlertaId == alerta.Id)
            .OrderByDescending(e => e.FechaInicio)
            .FirstOrDefaultAsync(ct);

        if (evento is null)
            return;

        evento.Estado = estado;
        evento.UsuarioResponsableId = usuarioActual.Id;
        if (fin is { } f && evento.FechaFin is null)
            evento.FechaFin = f;
    }

    private async Task<Alerta> CargarAsync(int alertaId, CancellationToken ct) =>
        await db.Alertas.FirstOrDefaultAsync(a => a.Id == alertaId, ct)
        ?? throw new ExcepcionNoEncontrado("la alerta", alertaId);

    private async Task<AlertaDto> RecargarAsync(int alertaId, CancellationToken ct) =>
        AlertaDto.Desde(await ConsultaBase().FirstAsync(a => a.Id == alertaId, ct));

    private IQueryable<Alerta> ConsultaBase() =>
        db.Alertas
            .AsNoTracking()
            .Include(a => a.Comunidad)
            .Include(a => a.Sensor)
            .Include(a => a.ReconocidaPorUsuario)
            .Include(a => a.CerradaPorUsuario);
}
