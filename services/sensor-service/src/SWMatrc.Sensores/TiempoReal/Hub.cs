using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SWMatrc.Comun;
using SWMatrc.Sensores.Aplicacion;

namespace SWMatrc.Sensores.TiempoReal;

/// <summary>
/// Canal en tiempo real. El servidor empuja cada lectura y cada cambio de alerta en cuanto
/// se producen, sin que el navegador tenga que preguntar.
/// </summary>
/// <remarks>
/// El token llega en <c>?access_token=</c> porque el navegador no puede mandar cabeceras en
/// el handshake del WebSocket; lo admite <c>AgregarSeguridadSwmatrc(..., "/hubs")</c>.
/// Con más de una réplica haría falta un backplane (Redis) para que los grupos se
/// compartan; en la Fase 3 corre una sola.
/// </remarks>
[Authorize(Policy = Permisos.SensoresLeer)]
public sealed class MonitoreoHub(ServicioMonitoreo monitoreo, ILogger<MonitoreoHub> logger) : Hub
{
    public const string Ruta = "/hubs/monitoring";

    /// <summary>Grupo que recibe los avisos de todas las comunidades (dashboard general).</summary>
    public const string GrupoGlobal = "todas";

    public override async Task OnConnectedAsync()
    {
        logger.LogInformation("Cliente {Conexion} conectado ({Usuario}).", Context.ConnectionId, Context.User?.Identity?.Name);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Suscribe la conexión a una comunidad: deja de recibir el tráfico de las demás. El
    /// recién llegado recibe la fotografía completa para pintar sin esperar al siguiente ciclo.
    /// </summary>
    public async Task SuscribirComunidad(int comunidadId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, NombreGrupo(comunidadId));
        var estado = await monitoreo.ObtenerEstadoAsync(comunidadId, Context.ConnectionAborted);
        await Clients.Caller.SendAsync("EstadoComunidad", estado, Context.ConnectionAborted);
    }

    public Task CancelarSuscripcion(int comunidadId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, NombreGrupo(comunidadId));

    /// <summary>Recibe solo los cambios de alertas de todas las comunidades, sin las lecturas.</summary>
    public Task SuscribirAlertasGlobales() => Groups.AddToGroupAsync(Context.ConnectionId, GrupoGlobal);

    /// <summary>Comprobación de latencia para el indicador de conexión del frontend.</summary>
    public DateTime Ping() => DateTime.UtcNow;

    public static string NombreGrupo(int comunidadId) => $"comunidad-{comunidadId}";
}

/// <summary>Publica en el canal en tiempo real. La capa de aplicación no sabe que el transporte es un WebSocket.</summary>
public interface INotificador
{
    Task LecturaAsync(LecturaDto lectura, CancellationToken ct);
    Task EstadoSensorAsync(SensorDto sensor, CancellationToken ct);
    Task AlertaAsync(string evento, int comunidadId, object alerta, CancellationToken ct);
    Task SistemaReiniciadoAsync(CancellationToken ct);
}

public sealed class Notificador(IHubContext<MonitoreoHub> hub) : INotificador
{
    public Task LecturaAsync(LecturaDto lectura, CancellationToken ct) =>
        hub.Clients.Group(MonitoreoHub.NombreGrupo(lectura.ComunidadId)).SendAsync("LecturaRecibida", lectura, ct);

    public Task EstadoSensorAsync(SensorDto sensor, CancellationToken ct) =>
        hub.Clients.Group(MonitoreoHub.NombreGrupo(sensor.ComunidadId)).SendAsync("EstadoSensorCambiado", sensor, ct);

    /// <summary>Un cambio de alerta va a la comunidad y también al grupo global.</summary>
    public Task AlertaAsync(string evento, int comunidadId, object alerta, CancellationToken ct) =>
        hub.Clients.Groups(MonitoreoHub.NombreGrupo(comunidadId), MonitoreoHub.GrupoGlobal).SendAsync(evento, alerta, ct);

    public Task SistemaReiniciadoAsync(CancellationToken ct) =>
        hub.Clients.All.SendAsync("SistemaReiniciado", ct);
}
