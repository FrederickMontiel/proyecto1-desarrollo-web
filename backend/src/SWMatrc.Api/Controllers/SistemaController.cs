using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SWMatrc.Application.Abstracciones;

namespace SWMatrc.Api.Controllers;

/// <summary>Operaciones de mantenimiento sobre el sistema de monitoreo.</summary>
[ApiController]
[Route("api/sistema")]
[Authorize(Policy = "Administracion")]
public sealed class SistemaController(IServicioMonitoreo monitoreo) : ControllerBase
{
    /// <summary>
    /// Reinicia el monitoreo: cierra las alertas abiertas, sella sus eventos, descarta las
    /// lecturas acumuladas y devuelve todos los sensores a su valor de reposo. El historial
    /// de incidentes se conserva.
    /// </summary>
    [HttpPost("reiniciar")]
    public async Task<IActionResult> Reiniciar(CancellationToken ct)
    {
        await monitoreo.ReiniciarSistemaAsync(ct);
        return Ok(new { mensaje = "Sistema de monitoreo reiniciado." });
    }
}
