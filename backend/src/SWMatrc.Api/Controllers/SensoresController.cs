using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Dtos;
using SWMatrc.Domain.Enums;
using SWMatrc.Domain.Riesgo;

namespace SWMatrc.Api.Controllers;

/// <summary>Administración de la red de sensores simulados.</summary>
[ApiController]
[Route("api/sensores")]
[Authorize]
public sealed class SensoresController(IServicioSensores sensores) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SensorDto>>> Listar(
        [FromQuery] FiltroSensores filtro, CancellationToken ct) =>
        Ok(await sensores.ListarAsync(filtro, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SensorDto>> Obtener(int id, CancellationToken ct) =>
        Ok(await sensores.ObtenerAsync(id, ct));

    /// <summary>
    /// Catálogo de calibraciones de fábrica por tipo de sensor. El formulario de alta lo
    /// usa para proponer unidad, rango y umbrales sin que el usuario los invente.
    /// </summary>
    [HttpGet("plantillas")]
    public ActionResult<object> Plantillas() =>
        Ok(Enum.GetValues<TipoSensor>().Select(PlantillaSensor.Para).Select(p => new
        {
            tipo = p.Tipo.ToString(),
            tipoValor = (int)p.Tipo,
            unidad = p.Unidad,
            valorMinimo = p.ValorMinimo,
            valorMaximo = p.ValorMaximo,
            valorReposo = p.ValorReposo,
            variacionMaxima = p.VariacionMaxima,
            umbralAmarilloAlto = p.AmarilloAlto,
            umbralNaranjaAlto = p.NaranjaAlto,
            umbralRojoAlto = p.RojoAlto,
            umbralAmarilloBajo = p.AmarilloBajo,
            umbralNaranjaBajo = p.NaranjaBajo,
            umbralRojoBajo = p.RojoBajo
        }));

    [HttpPost]
    [Authorize(Policy = "Administracion")]
    public async Task<ActionResult<SensorDto>> Crear(CrearSensorRequest request, CancellationToken ct)
    {
        var sensor = await sensores.CrearAsync(request, ct);
        return CreatedAtAction(nameof(Obtener), new { id = sensor.Id }, sensor);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "Administracion")]
    public async Task<ActionResult<SensorDto>> Actualizar(
        int id, ActualizarSensorRequest request, CancellationToken ct) =>
        Ok(await sensores.ActualizarAsync(id, request, ct));

    /// <summary>Activa o desactiva un sensor sin borrarlo de la red.</summary>
    [HttpPatch("{id:int}/estado")]
    [Authorize(Policy = "Operacion")]
    public async Task<ActionResult<SensorDto>> CambiarEstado(
        int id, [FromQuery] EstadoSensor estado, CancellationToken ct) =>
        Ok(await sensores.CambiarEstadoAsync(id, estado, ct));

    /// <summary>Fuerza una lectura concreta. Sirve para ensayar escenarios de alerta.</summary>
    [HttpPost("{id:int}/valor")]
    [Authorize(Policy = "Operacion")]
    public async Task<ActionResult<SensorDto>> EstablecerValor(
        int id, EstablecerValorRequest request, CancellationToken ct) =>
        Ok(await sensores.EstablecerValorAsync(id, request.Valor, ct));

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "Administracion")]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        await sensores.EliminarAsync(id, ct);
        return NoContent();
    }
}
