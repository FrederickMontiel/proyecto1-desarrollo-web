using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Dtos;

namespace SWMatrc.Api.Controllers;

/// <summary>Historial de incidentes y bitácora de acciones.</summary>
[ApiController]
[Route("api")]
[Authorize]
public sealed class HistorialController(IServicioHistorial historial, IServicioBitacora bitacora)
    : ControllerBase
{
    /// <summary>Eventos registrados, del más reciente al más antiguo.</summary>
    [HttpGet("historial")]
    public async Task<ActionResult<PaginaDto<EventoDto>>> Eventos(
        [FromQuery] FiltroHistorial filtro, CancellationToken ct) =>
        Ok(await historial.ListarAsync(filtro, ct));

    /// <summary>Estadísticas de los eventos que cumplen el mismo filtro que el listado.</summary>
    [HttpGet("historial/estadisticas")]
    public async Task<ActionResult<EstadisticasHistorialDto>> Estadisticas(
        [FromQuery] FiltroHistorial filtro, CancellationToken ct) =>
        Ok(await historial.EstadisticasAsync(filtro, ct));

    /// <summary>Pista de auditoría. Solo la ve el administrador.</summary>
    [HttpGet("bitacora")]
    [Authorize(Policy = "Administracion")]
    public async Task<ActionResult<PaginaDto<BitacoraDto>>> Bitacora(
        [FromQuery] FiltroBitacora filtro, CancellationToken ct) =>
        Ok(await bitacora.ListarAsync(filtro, ct));

    /// <summary>Acciones y entidades registradas, para poblar los filtros de la bitácora.</summary>
    [HttpGet("bitacora/catalogos")]
    [Authorize(Policy = "Administracion")]
    public async Task<ActionResult<object>> CatalogosBitacora(CancellationToken ct)
    {
        var (acciones, entidades) = await bitacora.CatalogosAsync(ct);
        return Ok(new { acciones, entidades });
    }
}
