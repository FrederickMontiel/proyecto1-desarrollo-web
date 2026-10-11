using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SWMatrc.Comun;

// ---------------------------------------------------------------------------
// Excepciones de negocio
// ---------------------------------------------------------------------------

/// <summary>Error esperado de negocio. El manejador lo traduce a una respuesta HTTP con significado.</summary>
public class ExcepcionAplicacion(string mensaje) : Exception(mensaje);

/// <summary>El recurso solicitado no existe. Se responde 404.</summary>
public sealed class ExcepcionNoEncontrado(string entidad, object id)
    : ExcepcionAplicacion($"No se encontró {entidad} con identificador '{id}'.");

/// <summary>Los datos recibidos no cumplen las reglas de negocio. Se responde 400.</summary>
public sealed class ExcepcionValidacion(string mensaje) : ExcepcionAplicacion(mensaje);

/// <summary>La operación choca con el estado actual (un nombre duplicado, por ejemplo). Se responde 409.</summary>
public sealed class ExcepcionConflicto(string mensaje) : ExcepcionAplicacion(mensaje);

/// <summary>Otro microservicio del que depende la operación no respondió. Se responde 503.</summary>
public sealed class ExcepcionServicioNoDisponible(string mensaje) : ExcepcionAplicacion(mensaje);

/// <summary>
/// Traduce las excepciones a ProblemDetails, el mismo formato que usan el gateway y los
/// demás servicios. Las inesperadas se responden como 500 genérico: el detalle queda en el log.
/// </summary>
public sealed class ManejadorExcepciones(ILogger<ManejadorExcepciones> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext contexto, Exception excepcion, CancellationToken ct)
    {
        var (estado, titulo, detalle) = excepcion switch
        {
            ExcepcionNoEncontrado e => (StatusCodes.Status404NotFound, "Recurso no encontrado", e.Message),
            ExcepcionValidacion e => (StatusCodes.Status400BadRequest, "Solicitud inválida", e.Message),
            ExcepcionConflicto e => (StatusCodes.Status409Conflict, "Conflicto", e.Message),
            ExcepcionServicioNoDisponible e => (StatusCodes.Status503ServiceUnavailable, "Servicio no disponible", e.Message),
            ExcepcionAplicacion e => (StatusCodes.Status400BadRequest, "Operación rechazada", e.Message),
            BadHttpRequestException e => (e.StatusCode, "Solicitud inválida", "La solicitud no tiene un formato válido."),
            _ => (StatusCodes.Status500InternalServerError, "Error interno",
                  "Ocurrió un error inesperado al procesar la solicitud.")
        };

        if (estado >= 500 && excepcion is not ExcepcionServicioNoDisponible)
            logger.LogError(excepcion, "Error no controlado en {Ruta}", contexto.Request.Path);
        else
            logger.LogWarning("{Titulo} en {Ruta}: {Detalle}", titulo, contexto.Request.Path, detalle);

        contexto.Response.StatusCode = estado;
        await contexto.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = estado,
            Title = titulo,
            Detail = detalle,
            Instance = contexto.Request.Path
        }, Json.Opciones, "application/problem+json", ct);

        return true;
    }
}

// ---------------------------------------------------------------------------
// Correlación
// ---------------------------------------------------------------------------

/// <summary>
/// El gateway asigna a cada petición un <c>X-Correlation-Id</c>. Este middleware lo agrega
/// al ámbito de los logs, de modo que cada línea de log de la operación lo incluya.
/// </summary>
public sealed class Correlacion(RequestDelegate siguiente, ILogger<Correlacion> logger)
{
    public const string Cabecera = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext contexto)
    {
        var id = contexto.Request.Headers[Cabecera].FirstOrDefault() ?? Activity.Current?.TraceId.ToString();
        using (logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = id }))
            await siguiente(contexto);
    }
}

// ---------------------------------------------------------------------------
// JSON
// ---------------------------------------------------------------------------

public static class Json
{
    /// <summary>camelCase y enumerados como texto, igual que en la Fase 2.</summary>
    public static readonly JsonSerializerOptions Opciones = Configurar(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configurar(JsonSerializerOptions o)
    {
        o.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        // Acentos y eñes tal cual ("ó", no "ó"). Las respuestas son JSON, nunca HTML incrustado.
        o.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        if (!o.Converters.OfType<JsonStringEnumConverter>().Any())
            o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }
}

// ---------------------------------------------------------------------------
// Arranque común
// ---------------------------------------------------------------------------

public static class ExtensionesWeb
{
    public const string EtiquetaListo = "ready";

    /// <summary>Servicios web comunes: JSON, errores, proxy, apagado ordenado y sondas.</summary>
    public static IServiceCollection AgregarWebSwmatrc(this IServiceCollection services)
    {
        services.AddControllers()
            .AddJsonOptions(o => Json.Configurar(o.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(o =>
            {
                // Los errores de validación de modelo salen con el mismo formato y en español.
                o.InvalidModelStateResponseFactory = contexto =>
                {
                    var conErrores = contexto.ModelState.Where(e => e.Value?.Errors.Count > 0).ToList();

                    // Las claves "$..." vienen del lector de JSON: el cuerpo no se pudo leer
                    // (sintaxis, codificación o un tipo incorrecto). Ahí no hay reglas de
                    // negocio que reportar, solo dónde se rompió.
                    var errorJson = conErrores.FirstOrDefault(e => e.Key.StartsWith('$'));
                    IEnumerable<string> mensajes = errorJson.Key is { } clave
                        ? [clave == "$"
                            ? "El cuerpo de la solicitud no es un JSON válido."
                            : $"El cuerpo de la solicitud no es un JSON válido o el campo '{clave.TrimStart('$', '.')}' tiene un tipo incorrecto."]
                        : conErrores.SelectMany(e => e.Value!.Errors.Select(error =>
                            string.IsNullOrWhiteSpace(error.ErrorMessage) || error.ErrorMessage.StartsWith("The ")
                                ? $"El campo '{e.Key}' no es válido."
                                : error.ErrorMessage));

                    return new BadRequestObjectResult(new ProblemDetails
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Title = "Solicitud inválida",
                        Detail = string.Join(" ", mensajes),
                        Instance = contexto.HttpContext.Request.Path
                    })
                    { ContentTypes = { "application/problem+json" } };
                };
            });

        services.ConfigureHttpJsonOptions(o => Json.Configurar(o.SerializerOptions));
        services.AddProblemDetails();
        services.AddExceptionHandler<ManejadorExcepciones>();

        // El servicio siempre está detrás del gateway, que solo se publica en el loopback:
        // la IP real del cliente llega en X-Forwarded-For.
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });

        // Kubernetes da un plazo entre SIGTERM y SIGKILL; se aprovecha para terminar lo que esté en curso.
        services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(25));

        services.AddHealthChecks();
        return services;
    }

    /// <summary>Canalización común. Va antes de mapear los controladores.</summary>
    public static WebApplication UsarWebSwmatrc(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseMiddleware<Correlacion>();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();

        // "live": el proceso responde (si falla, se reinicia el contenedor).
        // "ready": además sus dependencias están bien (si falla, deja de recibir tráfico).
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = c => c.Tags.Contains(EtiquetaListo)
        }).AllowAnonymous();

        return app;
    }
}
