using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Dtos;

namespace SWMatrc.Api.Controllers;

/// <summary>Administración de las comunidades monitoreadas.</summary>
[ApiController]
[Route("api/comunidades")]
[Authorize]
public sealed class ComunidadesController(IServicioComunidades comunidades) : ControllerBase
{
    /// <summary>Listado con búsqueda y filtros por estado, municipio y departamento.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ComunidadDto>>> Listar(
        [FromQuery] FiltroComunidades filtro, CancellationToken ct) =>
        Ok(await comunidades.ListarAsync(filtro, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ComunidadDto>> Obtener(int id, CancellationToken ct) =>
        Ok(await comunidades.ObtenerAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = "Administracion")]
    public async Task<ActionResult<ComunidadDto>> Crear(GuardarComunidadRequest request, CancellationToken ct)
    {
        var comunidad = await comunidades.CrearAsync(request, ct);
        return CreatedAtAction(nameof(Obtener), new { id = comunidad.Id }, comunidad);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "Administracion")]
    public async Task<ActionResult<ComunidadDto>> Actualizar(
        int id, GuardarComunidadRequest request, CancellationToken ct) =>
        Ok(await comunidades.ActualizarAsync(id, request, ct));

    /// <summary>Activa o desactiva una comunidad. Desactivada, deja de generar lecturas y alertas.</summary>
    [HttpPatch("{id:int}/estado")]
    [Authorize(Policy = "Administracion")]
    public async Task<ActionResult<ComunidadDto>> CambiarEstado(int id, [FromQuery] bool activa, CancellationToken ct) =>
        Ok(await comunidades.CambiarEstadoAsync(id, activa, ct));
}
