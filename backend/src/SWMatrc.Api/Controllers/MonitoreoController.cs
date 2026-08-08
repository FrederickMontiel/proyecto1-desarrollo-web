using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Dtos;

namespace SWMatrc.Api.Controllers;

/// <summary>
/// Consultas del tablero. El canal en tiempo real cubre la actualización continua; estos
/// extremos sirven para la carga inicial y para recargar tras un cambio de filtros.
/// </summary>
[ApiController]
[Route("api/monitoreo")]
[Authorize]
public sealed class MonitoreoController(IServicioMonitoreo monitoreo) : ControllerBase
{
    [HttpGet("comunidades")]
    public async Task<ActionResult<IReadOnlyList<ComunidadDto>>> Comunidades(CancellationToken ct) =>
        Ok(await monitoreo.ListarComunidadesAsync(ct));

    /// <summary>Instantánea completa de una comunidad: sensores y alertas abiertas.</summary>
    [HttpGet("comunidades/{comunidadId:int}/estado")]
    public async Task<ActionResult<EstadoComunidadDto>> Estado(int comunidadId, CancellationToken ct) =>
        Ok(await monitoreo.ObtenerEstadoAsync(comunidadId, ct));

    /// <summary>Series de lecturas para los gráficos de evolución.</summary>
    /// <param name="minutos">Ventana hacia atrás, entre 1 y 1440 minutos.</param>
    [HttpGet("comunidades/{comunidadId:int}/series")]
    public async Task<ActionResult<IReadOnlyList<SerieHistoricaDto>>> Series(
        int comunidadId, [FromQuery] int minutos = 30, CancellationToken ct = default) =>
        Ok(await monitoreo.ObtenerSeriesAsync(comunidadId, minutos, ct));

    /// <summary>Indicadores agregados que encabezan el dashboard.</summary>
    [HttpGet("resumen")]
    public async Task<ActionResult<ResumenDashboardDto>> Resumen(
        [FromQuery] int? comunidadId, CancellationToken ct) =>
        Ok(await monitoreo.ObtenerResumenAsync(comunidadId, ct));
}
