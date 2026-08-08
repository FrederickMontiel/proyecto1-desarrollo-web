using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SWMatrc.Application.Abstracciones;

namespace SWMatrc.Api.Hubs;

/// <summary>
/// Extremo servidor del canal en tiempo real. La conexión viaja sobre un WebSocket
/// permanente: el servidor empuja cada lectura y cada alerta en cuanto se producen, sin
/// que el navegador tenga que preguntar.
/// </summary>
/// <remarks>
/// Requiere token igual que el resto de la API. Como el navegador no permite fijar
/// cabeceras en el handshake de un WebSocket, el token llega por query string y se
/// traslada al contexto en <c>Program.cs</c> (evento <c>OnMessageReceived</c>).
/// </remarks>
[Authorize]
public sealed class MonitoreoHub(IServicioMonitoreo monitoreo, ILogger<MonitoreoHub> logger) : Hub
{
    /// <summary>Ruta pública del hub. Se comparte con el frontend y con la configuración de nginx.</summary>
    public const string Ruta = "/hubs/monitoreo";

    public override async Task OnConnectedAsync()
    {
        logger.LogInformation("Cliente {ConnectionId} conectado al canal de monitoreo ({Usuario})",
            Context.ConnectionId, Context.User?.Identity?.Name ?? "desconocido");

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("Cliente {ConnectionId} desconectado del canal de monitoreo", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Suscribe la conexión a una comunidad. Los mensajes se difunden por grupo, de modo
    /// que un tablero abierto en una comunidad no recibe el tráfico de las demás: es lo
    /// que permite sumar comunidades sin que crezca el ancho de banda de cada cliente.
    /// </summary>
    public async Task SuscribirComunidad(int comunidadId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, NombreGrupo(comunidadId));

        // El recién llegado recibe la fotografía completa para pintar el tablero sin
        // esperar al siguiente ciclo del simulador.
        var estado = await monitoreo.ObtenerEstadoAsync(comunidadId, Context.ConnectionAborted);
        await Clients.Caller.SendAsync("EstadoComunidad", estado, Context.ConnectionAborted);
    }

    public Task CancelarSuscripcion(int comunidadId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, NombreGrupo(comunidadId));

    /// <summary>Comprobación de latencia usada por el indicador de conexión del frontend.</summary>
    public DateTime Ping() => DateTime.UtcNow;

    public static string NombreGrupo(int comunidadId) => $"comunidad-{comunidadId}";
}
