using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Dtos;

namespace SWMatrc.Api.Controllers;

/// <summary>Consulta, atención y cierre de las alertas emitidas.</summary>
[ApiController]
[Route("api/alertas")]
[Authorize]
public sealed class AlertasController(IServicioAlertas alertas) : ControllerBase
{
    [HttpGet("activas")]
    public async Task<ActionResult<IReadOnlyList<AlertaDto>>> Activas(
        [FromQuery] int? comunidadId, CancellationToken ct) =>
        Ok(await alertas.ListarActivasAsync(comunidadId, ct));

    /// <summary>Listado paginado con filtros por fecha, comunidad, sensor, fenómeno, nivel y estado.</summary>
    [HttpGet]
    public async Task<ActionResult<PaginaDto<AlertaDto>>> Listar(
        [FromQuery] FiltroAlertas filtro, CancellationToken ct) =>
        Ok(await alertas.ListarAsync(filtro, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AlertaDto>> Obtener(int id, CancellationToken ct) =>
        Ok(await alertas.ObtenerAsync(id, ct));

    /// <summary>Deja constancia de que un responsable vio la alerta y se hace cargo.</summary>
    [HttpPost("{id:int}/atender")]
    [Authorize(Policy = "Operacion")]
    public async Task<ActionResult<AlertaDto>> Atender(
        int id, [FromBody] GestionAlertaRequest? request, CancellationToken ct) =>
        Ok(await alertas.AtenderAsync(id, request?.Comentario, ct));

    /// <summary>Ruta anterior de <see cref="Atender"/>, conservada por compatibilidad.</summary>
    [HttpPost("{id:int}/reconocer")]
    [Authorize(Policy = "Operacion")]
    public async Task<ActionResult<AlertaDto>> Reconocer(int id, CancellationToken ct) =>
        Ok(await alertas.AtenderAsync(id, null, ct));

    /// <summary>Cierre manual con registro del usuario responsable.</summary>
    [HttpPost("{id:int}/cerrar")]
    [Authorize(Policy = "Operacion")]
    public async Task<ActionResult<AlertaDto>> Cerrar(
        int id, [FromBody] GestionAlertaRequest? request, CancellationToken ct) =>
        Ok(await alertas.CerrarAsync(id, request?.Comentario, ct));
}
