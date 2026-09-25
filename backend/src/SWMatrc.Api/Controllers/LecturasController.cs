using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Dtos;

namespace SWMatrc.Api.Controllers;

/// <summary>Consulta histórica y administración de las lecturas almacenadas.</summary>
[ApiController]
[Route("api/lecturas")]
[Authorize]
public sealed class LecturasController(IServicioLecturas lecturas) : ControllerBase
{
    /// <summary>Lecturas por sensor, comunidad, tipo y rango de fechas, de la más reciente a la más antigua.</summary>
    [HttpGet]
    public async Task<ActionResult<PaginaDto<LecturaHistoricaDto>>> Listar(
        [FromQuery] FiltroLecturas filtro, CancellationToken ct) =>
        Ok(await lecturas.ListarAsync(filtro, ct));

    /// <summary>Registro manual de una lectura; se evalúa contra las reglas igual que una automática.</summary>
    [HttpPost]
    [Authorize(Policy = "Operacion")]
    public async Task<ActionResult<LecturaHistoricaDto>> Registrar(RegistrarLecturaRequest request, CancellationToken ct) =>
        Ok(await lecturas.RegistrarAsync(request, ct));

    /// <summary>Elimina una lectura errónea. Queda registrado en la bitácora.</summary>
    [HttpDelete("{id:long}")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Eliminar(long id, CancellationToken ct)
    {
        await lecturas.EliminarAsync(id, ct);
        return NoContent();
    }
}
