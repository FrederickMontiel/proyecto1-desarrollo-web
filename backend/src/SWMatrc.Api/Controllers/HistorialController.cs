using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Dtos;
using SWMatrc.Domain.Enums;

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
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = 20,
        [FromQuery] int? comunidadId = null,
        [FromQuery] TipoFenomeno? fenomeno = null,
        [FromQuery] DateTime? desde = null,
        [FromQuery] DateTime? hasta = null,
        CancellationToken ct = default) =>
        Ok(await historial.ListarAsync(pagina, tamano, comunidadId, fenomeno, desde, hasta, ct));

    /// <summary>Pista de auditoría. Solo la ve el administrador.</summary>
    [HttpGet("bitacora")]
    [Authorize(Policy = "Administracion")]
    public async Task<ActionResult<PaginaDto<BitacoraDto>>> Bitacora(
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = 30,
        [FromQuery] string? accion = null,
        CancellationToken ct = default) =>
        Ok(await bitacora.ListarAsync(pagina, tamano, accion, ct));
}
