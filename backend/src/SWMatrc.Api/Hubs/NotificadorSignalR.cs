using Microsoft.AspNetCore.SignalR;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Dtos;

namespace SWMatrc.Api.Hubs;

/// <summary>
/// Implementación del canal de difusión sobre SignalR. Es la única pieza que conoce el
/// transporte; la capa de aplicación solo publica eventos de negocio contra la interfaz.
/// </summary>
public sealed class NotificadorSignalR(IHubContext<MonitoreoHub> hub) : INotificadorTiempoReal
{
    public Task LecturaRecibidaAsync(LecturaDto lectura, CancellationToken ct = default) =>
        Grupo(lectura.ComunidadId).SendAsync("LecturaRecibida", lectura, ct);

    public Task AlertaGeneradaAsync(AlertaDto alerta, CancellationToken ct = default) =>
        Grupo(alerta.ComunidadId).SendAsync("AlertaGenerada", alerta, ct);

    public Task AlertaCerradaAsync(AlertaDto alerta, CancellationToken ct = default) =>
        Grupo(alerta.ComunidadId).SendAsync("AlertaCerrada", alerta, ct);

    public Task EstadoSensorCambiadoAsync(SensorDto sensor, CancellationToken ct = default) =>
        Grupo(sensor.ComunidadId).SendAsync("EstadoSensorCambiado", sensor, ct);

    public Task EstadoComunidadAsync(EstadoComunidadDto estado, CancellationToken ct = default) =>
        Grupo(estado.ComunidadId).SendAsync("EstadoComunidad", estado, ct);

    /// <summary>El reinicio afecta a toda la instalación, así que va a todas las conexiones.</summary>
    public Task SistemaReiniciadoAsync(CancellationToken ct = default) =>
        hub.Clients.All.SendAsync("SistemaReiniciado", ct);

    private IClientProxy Grupo(int comunidadId) => hub.Clients.Group(MonitoreoHub.NombreGrupo(comunidadId));
}
