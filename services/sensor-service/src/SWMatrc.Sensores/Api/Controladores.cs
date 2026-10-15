using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SWMatrc.Comun;
using SWMatrc.Sensores.Aplicacion;
using SWMatrc.Sensores.Dominio;
using SWMatrc.Sensores.Integracion;
using SWMatrc.Sensores.TiempoReal;

namespace SWMatrc.Sensores.Api;

/// <summary>Red de sensores, lecturas y consultas del tablero.</summary>
[ApiController]
[Route("api/sensors")]
[Authorize(Policy = Permisos.SensoresLeer)]
public sealed class SensoresController(
    ServicioSensores sensores,
    ServicioLecturas lecturas,
    ServicioMonitoreo monitoreo) : ControllerBase
{
    // --- Sensores --------------------------------------------------------------------

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SensorDto>>> Listar([FromQuery] FiltroSensores filtro, CancellationToken ct) =>
        Ok(await sensores.ListarAsync(filtro, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SensorDto>> Obtener(int id, CancellationToken ct) =>
        Ok(await sensores.ObtenerAsync(id, ct));

    /// <summary>
    /// Calibración de fábrica por tipo (unidad, rango, reposo, variación). El formulario de
    /// alta la usa para proponer valores. Los umbrales recomendados los da Alert Service.
    /// </summary>
    [HttpGet("templates")]
    public ActionResult<IEnumerable<object>> Plantillas() =>
        Ok(PlantillaSensor.Todas.Select(p => new
        {
            tipo = p.Tipo,
            tipoValor = (int)p.Tipo,
            unidad = p.Unidad,
            valorMinimo = p.ValorMinimo,
            valorMaximo = p.ValorMaximo,
            valorReposo = p.ValorReposo,
            variacionMaxima = p.VariacionMaxima
        }));

    [HttpPost]
    [Authorize(Policy = Permisos.SensoresEditar)]
    public async Task<ActionResult<SensorDto>> Crear(CrearSensorRequest request, CancellationToken ct)
    {
        var sensor = await sensores.CrearAsync(request, ct);
        return CreatedAtAction(nameof(Obtener), new { id = sensor.Id }, sensor);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Permisos.SensoresEditar)]
    public async Task<ActionResult<SensorDto>> Actualizar(int id, ActualizarSensorRequest request, CancellationToken ct) =>
        Ok(await sensores.ActualizarAsync(id, request, ct));

    /// <summary>Activa o desactiva un sensor sin borrarlo de la red.</summary>
    [HttpPatch("{id:int}/status")]
    [Authorize(Policy = Permisos.SensoresOperar)]
    public async Task<ActionResult<SensorDto>> CambiarEstado(int id, CambiarEstadoSensorRequest request, CancellationToken ct) =>
        Ok(await sensores.CambiarEstadoAsync(id, request.Estado!.Value, ct));

    /// <summary>Fuerza una lectura concreta para ensayar escenarios de alerta.</summary>
    [HttpPost("{id:int}/value")]
    [Authorize(Policy = Permisos.SensoresOperar)]
    public async Task<ActionResult<LecturaHistoricaDto>> EstablecerValor(int id, EstablecerValorRequest request, CancellationToken ct) =>
        Ok(await sensores.EstablecerValorAsync(id, request.Valor!.Value, ct));

    [HttpDelete("{id:int}")]
    [Authorize(Policy = Permisos.SensoresEditar)]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        await sensores.EliminarAsync(id, ct);
        return NoContent();
    }

    // --- Lecturas --------------------------------------------------------------------

    /// <summary>Lecturas por sensor, comunidad, tipo y rango de fechas, de la más reciente a la más antigua.</summary>
    [HttpGet("readings")]
    public async Task<ActionResult<PaginaDto<LecturaHistoricaDto>>> Lecturas([FromQuery] FiltroLecturas filtro, CancellationToken ct) =>
        Ok(await lecturas.ListarAsync(filtro, ct: ct));

    /// <summary>Registro manual de una lectura; se evalúa igual que una automática.</summary>
    [HttpPost("readings")]
    [Authorize(Policy = Permisos.LecturasEditar)]
    public async Task<ActionResult<LecturaHistoricaDto>> RegistrarLectura(RegistrarLecturaRequest request, CancellationToken ct) =>
        Ok(await lecturas.RegistrarAsync(request, ct));

    [HttpDelete("readings/{id:long}")]
    [Authorize(Policy = Permisos.SensoresEditar)]
    public async Task<IActionResult> EliminarLectura(long id, CancellationToken ct)
    {
        await lecturas.EliminarAsync(id, ct);
        return NoContent();
    }

    [HttpGet("readings/stats")]
    public async Task<ActionResult<IReadOnlyList<EstadisticaTipoDto>>> Estadisticas(
        [FromQuery] int? comunidadId, [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct) =>
        Ok(await lecturas.EstadisticasAsync(comunidadId, desde, hasta, ct));

    // --- Tablero ---------------------------------------------------------------------

    [HttpGet("summary")]
    public async Task<ActionResult<ResumenSensoresDto>> Resumen([FromQuery] int? comunidadId, CancellationToken ct) =>
        Ok(await monitoreo.ObtenerResumenAsync(comunidadId, ct));

    /// <summary>Fotografía completa de una comunidad: sensores y alertas abiertas.</summary>
    [HttpGet("communities/{comunidadId:int}/state")]
    public async Task<ActionResult<EstadoComunidadDto>> Estado(int comunidadId, CancellationToken ct) =>
        Ok(await monitoreo.ObtenerEstadoAsync(comunidadId, ct));

    /// <summary>Series para los gráficos de evolución.</summary>
    /// <param name="minutos">Ventana hacia atrás, entre 1 y 1440 minutos.</param>
    [HttpGet("communities/{comunidadId:int}/series")]
    public async Task<ActionResult<IReadOnlyList<SerieHistoricaDto>>> Series(
        int comunidadId, [FromQuery] int minutos = 30, CancellationToken ct = default) =>
        Ok(await monitoreo.ObtenerSeriesAsync(comunidadId, minutos, ct));

    /// <summary>
    /// Descarta las lecturas, devuelve los sensores al reposo y cierra las alertas abiertas.
    /// El historial de eventos se conserva.
    /// </summary>
    [HttpPost("system/reset")]
    [Authorize(Policy = Permisos.SistemaAdministrar)]
    public async Task<ActionResult<object>> Reiniciar(CancellationToken ct) =>
        Ok(await monitoreo.ReiniciarAsync(ct));
}

/// <summary>
/// Rutas para Community, Alert y Report. Viven fuera de <c>/api</c> (el gateway no las
/// expone) y exigen token de servicio.
/// </summary>
[ApiController]
[Route("internal")]
[Authorize(Policy = Politicas.SoloServicios)]
public sealed class InternoController(
    ServicioSensores sensores,
    ServicioLecturas lecturas,
    INotificador notificador) : ControllerBase
{
    private static readonly HashSet<string> EventosPermitidos =
        ["AlertaGenerada", "AlertaActualizada", "AlertaCerrada", "AlertaAtendida", "AlertaReconocida"];

    /// <summary>Inventario completo (RF-REP-02 y RF-REP-06).</summary>
    [HttpGet("sensors")]
    public async Task<ActionResult<IReadOnlyList<SensorDto>>> Sensores([FromQuery] FiltroSensores filtro, CancellationToken ct) =>
        Ok(await sensores.ListarAsync(filtro, ct));

    /// <summary>Lo consume Community Service para la cantidad de sensores de cada comunidad.</summary>
    [HttpGet("sensors/count-by-community")]
    public async Task<ActionResult<IReadOnlyList<ConteoSensoresDto>>> Conteo(CancellationToken ct) =>
        Ok(await sensores.ContarPorComunidadAsync(ct));

    /// <summary>Lecturas paginadas con páginas de hasta 5000 filas, para procesar por lotes (RF-REP-03).</summary>
    [HttpGet("readings")]
    public async Task<ActionResult<PaginaDto<LecturaHistoricaDto>>> Lecturas([FromQuery] FiltroLecturas filtro, CancellationToken ct) =>
        Ok(await lecturas.ListarAsync(filtro, ServicioLecturas.TamanoMaximoInterno, ct));

    /// <summary>Estadísticas por tipo en un periodo (RF-REP-07).</summary>
    [HttpGet("readings/stats")]
    public async Task<ActionResult<IReadOnlyList<EstadisticaTipoDto>>> Estadisticas(
        [FromQuery] int? comunidadId, [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct) =>
        Ok(await lecturas.EstadisticasAsync(comunidadId, desde, hasta, ct));

    /// <summary>Alert Service avisa un cambio hecho por un usuario para que los tableros lo vean al instante.</summary>
    [HttpPost("notify")]
    public async Task<IActionResult> Notificar(Notificacion notificacion, CancellationToken ct)
    {
        if (!EventosPermitidos.Contains(notificacion.Evento))
            throw new ExcepcionValidacion($"Evento '{notificacion.Evento}' no reconocido.");
        if (notificacion.ComunidadId is not { } comunidadId)
            throw new ExcepcionValidacion("Indique la comunidad de la alerta.");

        await notificador.AlertaAsync(notificacion.Evento, comunidadId, notificacion.Datos, ct);
        return Accepted();
    }
}
