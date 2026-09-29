using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Dtos;

namespace SWMatrc.Api.Controllers;

/// <summary>
/// Reglas de alerta configurables. Cualquier usuario autenticado puede consultarlas para
/// entender por qué se levantó una alerta; solo el administrador las modifica.
/// </summary>
[ApiController]
[Route("api/reglas")]
[Authorize]
public sealed class ReglasController(IServicioReglas reglas) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReglaAlertaDto>>> Listar(
        [FromQuery] FiltroReglas filtro, CancellationToken ct) =>
        Ok(await reglas.ListarAsync(filtro, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ReglaAlertaDto>> Obtener(int id, CancellationToken ct) =>
        Ok(await reglas.ObtenerAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = "Administracion")]
    public async Task<ActionResult<ReglaAlertaDto>> Crear(GuardarReglaRequest request, CancellationToken ct)
    {
        var regla = await reglas.CrearAsync(request, ct);
        return CreatedAtAction(nameof(Obtener), new { id = regla.Id }, regla);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "Administracion")]
    public async Task<ActionResult<ReglaAlertaDto>> Actualizar(
        int id, GuardarReglaRequest request, CancellationToken ct) =>
        Ok(await reglas.ActualizarAsync(id, request, ct));

    [HttpPatch("{id:int}/estado")]
    [Authorize(Policy = "Administracion")]
    public async Task<ActionResult<ReglaAlertaDto>> CambiarEstado(int id, [FromQuery] bool activa, CancellationToken ct) =>
        Ok(await reglas.CambiarEstadoAsync(id, activa, ct));

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        await reglas.EliminarAsync(id, ct);
        return NoContent();
    }
}
