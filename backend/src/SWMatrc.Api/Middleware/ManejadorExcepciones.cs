using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SWMatrc.Application.Comun;

namespace SWMatrc.Api.Middleware;

/// <summary>
/// Traduce las excepciones de negocio a respuestas <c>ProblemDetails</c>. Cualquier otra
/// excepción se responde como 500 con un mensaje genérico: el detalle queda en el log del
/// servidor y no se filtra al cliente.
/// </summary>
public sealed class ManejadorExcepciones(ILogger<ManejadorExcepciones> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext contexto, Exception excepcion, CancellationToken ct)
    {
        var (estado, titulo, detalle) = excepcion switch
        {
            ExcepcionNoEncontrado e => (StatusCodes.Status404NotFound, "Recurso no encontrado", e.Message),
            ExcepcionValidacion e => (StatusCodes.Status400BadRequest, "Solicitud inválida", e.Message),
            ExcepcionAutenticacion e => (StatusCodes.Status401Unauthorized, "No autenticado", e.Message),
            ExcepcionAplicacion e => (StatusCodes.Status400BadRequest, "Operación rechazada", e.Message),
            _ => (StatusCodes.Status500InternalServerError, "Error interno",
                  "Ocurrió un error inesperado al procesar la solicitud.")
        };

        if (estado == StatusCodes.Status500InternalServerError)
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
        }, ct);

        return true;
    }
}
