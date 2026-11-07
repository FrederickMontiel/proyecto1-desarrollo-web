using System.Diagnostics;
using Yarp.ReverseProxy.Forwarder;

namespace SWMatrc.Gateway;

/// <summary>
/// Asigna a cada petición un identificador de correlación y lo propaga a los servicios de
/// atrás. Con él se sigue una misma operación a través de los logs de todos los contenedores.
/// También deja una línea de log por petición con su resultado y duración.
/// </summary>
public sealed class CorrelacionMiddleware(RequestDelegate siguiente, ILogger<CorrelacionMiddleware> logger)
{
    public const string Cabecera = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext contexto)
    {
        var id = contexto.Request.Headers[Cabecera].FirstOrDefault();
        // Solo se acepta un identificador entrante si es corto y sin caracteres raros: de lo
        // contrario un cliente podría inyectar texto arbitrario en los logs.
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || !id.All(c => char.IsLetterOrDigit(c) || c == '-'))
            id = Guid.NewGuid().ToString("N");

        contexto.Request.Headers[Cabecera] = id;
        contexto.Response.Headers[Cabecera] = id;

        var cronometro = Stopwatch.StartNew();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            await siguiente(contexto);

            logger.LogInformation("{Metodo} {Ruta} -> {Estado} en {Milisegundos} ms",
                contexto.Request.Method,
                contexto.Request.Path.Value,
                contexto.Response.StatusCode,
                cronometro.ElapsedMilliseconds);
        }
    }
}

/// <summary>
/// Traduce los fallos del proxy (servicio caído, tiempo agotado) a un ProblemDetails en
/// español. Sin esto el cliente recibiría un 502 vacío y no sabría qué pasó.
/// </summary>
public sealed class ErroresProxyMiddleware(RequestDelegate siguiente, ILogger<ErroresProxyMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext contexto)
    {
        await siguiente(contexto);

        var error = contexto.Features.Get<IForwarderErrorFeature>();
        if (error is null || contexto.Response.HasStarted)
            return;

        var ruta = contexto.GetReverseProxyFeature().Route.Config.RouteId;
        logger.LogWarning(error.Exception, "Fallo al reenviar a la ruta {Ruta}: {Error}", ruta, error.Error);

        var (estado, detalle) = error.Error switch
        {
            ForwarderError.RequestTimedOut =>
                (StatusCodes.Status504GatewayTimeout, "El servicio tardó demasiado en responder."),
            ForwarderError.RequestCanceled =>
                (StatusCodes.Status499ClientClosedRequest, "La solicitud fue cancelada."),
            _ =>
                (StatusCodes.Status503ServiceUnavailable, "El servicio no está disponible en este momento.")
        };

        // El nombre interno del servicio no se revela: solo el estado y un mensaje genérico.
        await Results.Problem(statusCode: estado, title: "Servicio no disponible", detail: detalle)
            .ExecuteAsync(contexto);
    }
}
