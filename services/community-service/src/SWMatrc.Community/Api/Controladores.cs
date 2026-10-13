using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SWMatrc.Comun;
using SWMatrc.Community.Aplicacion;

namespace SWMatrc.Community.Api;

/// <summary>Administración de las comunidades monitoreadas.</summary>
[ApiController]
[Route("api/communities")]
[Authorize(Policy = Permisos.ComunidadesLeer)]
public sealed class ComunidadesController(ServicioComunidades comunidades) : ControllerBase
{
    /// <summary>Listado con búsqueda y filtros por estado, municipio y departamento.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ComunidadDto>>> Listar([FromQuery] FiltroComunidades filtro, CancellationToken ct) =>
        Ok(await comunidades.ListarAsync(filtro, ct));

    /// <summary>Las mismas comunidades como GeoJSON, para el mapa.</summary>
    [HttpGet("geo")]
    public async Task<ActionResult> GeoJson([FromQuery] FiltroComunidades filtro, CancellationToken ct) =>
        Content(System.Text.Json.JsonSerializer.Serialize(await comunidades.GeoJsonAsync(filtro, ct), Json.Opciones),
            "application/geo+json");

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ComunidadDto>> Obtener(int id, CancellationToken ct) =>
        Ok(await comunidades.ObtenerAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = Permisos.ComunidadesEditar)]
    public async Task<ActionResult<ComunidadDto>> Crear(GuardarComunidadRequest request, CancellationToken ct)
    {
        var comunidad = await comunidades.CrearAsync(request, ct);
        return CreatedAtAction(nameof(Obtener), new { id = comunidad.Id }, comunidad);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Permisos.ComunidadesEditar)]
    public async Task<ActionResult<ComunidadDto>> Actualizar(int id, GuardarComunidadRequest request, CancellationToken ct) =>
        Ok(await comunidades.ActualizarAsync(id, request, ct));

    /// <summary>
    /// Activa o desactiva una comunidad. No se eliminan: sensores, alertas y reportes
    /// históricos siguen haciendo referencia a ellas.
    /// </summary>
    [HttpPatch("{id:int}/status")]
    [Authorize(Policy = Permisos.ComunidadesEditar)]
    public async Task<ActionResult<ComunidadDto>> CambiarEstado(int id, CambiarEstadoRequest request, CancellationToken ct) =>
        Ok(await comunidades.CambiarEstadoAsync(id, request.Activa!.Value, ct));
}

/// <summary>
/// Rutas para Sensor, Alert y Report. Viven fuera de <c>/api</c>, así que el gateway no las
/// expone, y exigen token de servicio.
/// </summary>
[ApiController]
[Route("internal/communities")]
[Authorize(Policy = Politicas.SoloServicios)]
public sealed class InternoController(ServicioComunidades comunidades) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ComunidadInternaDto>>> Listar([FromQuery] bool? activa, CancellationToken ct) =>
        Ok(await comunidades.ListarInternoAsync(activa, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ComunidadInternaDto>> Obtener(int id, CancellationToken ct) =>
        Ok(await comunidades.ObtenerInternoAsync(id, ct));
}
