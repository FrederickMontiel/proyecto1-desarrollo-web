using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Dtos;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Api.Controllers;

/// <summary>Consulta y acuse de recibo de las alertas emitidas.</summary>
[ApiController]
[Route("api/alertas")]
[Authorize]
public sealed class AlertasController(IServicioAlertas alertas) : ControllerBase
{
    [HttpGet("activas")]
    public async Task<ActionResult<IReadOnlyList<AlertaDto>>> Activas(
        [FromQuery] int? comunidadId, CancellationToken ct) =>
        Ok(await alertas.ListarActivasAsync(comunidadId, ct));

    [HttpGet]
    public async Task<ActionResult<PaginaDto<AlertaDto>>> Listar(
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = 20,
        [FromQuery] int? comunidadId = null,
        [FromQuery] NivelAlerta? nivel = null,
        CancellationToken ct = default) =>
        Ok(await alertas.ListarAsync(pagina, tamano, comunidadId, nivel, ct));

    /// <summary>Deja constancia de que un responsable vio la alerta y se hace cargo.</summary>
    [HttpPost("{id:int}/reconocer")]
    [Authorize(Policy = "Operacion")]
    public async Task<ActionResult<AlertaDto>> Reconocer(int id, CancellationToken ct) =>
        Ok(await alertas.ReconocerAsync(id, ct));
}
