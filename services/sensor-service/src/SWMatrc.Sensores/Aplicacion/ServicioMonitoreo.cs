using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SWMatrc.Comun;
using SWMatrc.Sensores.Datos;
using SWMatrc.Sensores.Dominio;
using SWMatrc.Sensores.Integracion;
using SWMatrc.Sensores.Simulacion;
using SWMatrc.Sensores.TiempoReal;

namespace SWMatrc.Sensores.Aplicacion;

/// <summary>Parámetros del ciclo de adquisición. Sección <c>Simulacion</c>.</summary>
public sealed class OpcionesSimulacion
{
    public const string Seccion = "Simulacion";

    public int IntervaloSegundos { get; set; } = 3;

    /// <summary>Con varias réplicas, solo una debe tenerla activa: el simulador guarda estado en memoria.</summary>
    public bool Habilitada { get; set; } = true;

    /// <summary>Ciclos seguidos sin reportar antes de declarar un sensor sin señal.</summary>
    public int CiclosToleranciaSinSenal { get; set; } = 3;

    public TimeSpan ToleranciaSinSenal =>
        TimeSpan.FromSeconds(Math.Max(1, IntervaloSegundos) * Math.Max(1, CiclosToleranciaSinSenal));
}

/// <summary>
/// Núcleo del Sensor Service. En cada ciclo adquiere las mediciones, le pide a Alert Service
/// que las evalúe, guarda las lecturas con el nivel resultante y difunde todo por el canal
/// en tiempo real.
/// </summary>
/// <remarks>
/// La evaluación de riesgo (reglas, umbrales, apertura y cierre de alertas) ya no ocurre
/// aquí: es responsabilidad de Alert Service. Si Alert no responde, las lecturas se guardan
/// y difunden igual, sin nivel; la adquisición nunca se detiene por otro servicio.
/// </remarks>
public sealed class ServicioMonitoreo(
    SensoresDb db,
    ISimuladorClima simulador,
    IFuenteComunidades comunidades,
    IClienteAlertas alertas,
    INotificador notificador,
    IClienteAuditoria auditoria,
    IOptions<OpcionesSimulacion> opciones,
    ILogger<ServicioMonitoreo> logger)
{
    private readonly OpcionesSimulacion _opciones = opciones.Value;

    // -----------------------------------------------------------------------
    // Ciclo de adquisición
    // -----------------------------------------------------------------------

    public async Task EjecutarCicloAsync(CancellationToken ct = default)
    {
        var catalogo = await comunidades.ListarAsync(ct);
        if (catalogo is null)
        {
            // Sin saber qué comunidades están activas no se simula: se podría estar
            // generando lecturas (y alertas) de una comunidad que alguien desactivó.
            logger.LogWarning("Ciclo omitido: todavía no se conocen las comunidades.");
            return;
        }

        var sensores = await db.Sensores.ToListAsync(ct);
        if (sensores.Count == 0)
            return;

        var ahora = DateTime.UtcNow;
        var nuevas = new List<(Lectura Lectura, Sensor Sensor)>();
        var cambiosDeEstado = new List<Sensor>();

        foreach (var sensor in sensores.Where(s => s.DebeReportar && EstaActiva(catalogo, s.ComunidadId)))
        {
            var valor = simulador.SiguienteValor(sensor);

            if (valor is null)
            {
                // El instrumento no respondió. No se inventa una lectura: si el silencio se
                // prolonga, el sensor pasa a "sin señal" y deja de sostener la evaluación.
                if (MarcarSinSenalSiProcede(sensor, ahora))
                    cambiosDeEstado.Add(sensor);
                continue;
            }

            if (sensor.Estado == EstadoSensor.SinSenal)
            {
                sensor.Estado = EstadoSensor.Activo;
                cambiosDeEstado.Add(sensor);
                logger.LogInformation("El sensor {Codigo} recuperó la señal.", sensor.Codigo);
            }

            sensor.ValorActual = valor.Value;
            sensor.UltimaLectura = ahora;

            var lectura = new Lectura
            {
                SensorId = sensor.Id,
                Valor = valor.Value,
                UnidadMedida = sensor.UnidadMedida,
                EstadoSensor = sensor.Estado,
                FechaHora = ahora
            };
            db.Lecturas.Add(lectura);
            nuevas.Add((lectura, sensor));
        }

        var evaluacion = await EvaluarAsync(sensores, catalogo, ct);
        foreach (var (lectura, sensor) in nuevas)
            lectura.Nivel = sensor.NivelActual;

        // Un único guardado por ciclo: las lecturas se insertan en lote.
        await db.SaveChangesAsync(ct);

        foreach (var (lectura, sensor) in nuevas)
            await notificador.LecturaAsync(LecturaDto.Desde(lectura, sensor), ct);

        foreach (var sensor in cambiosDeEstado)
            await notificador.EstadoSensorAsync(SensorDto.Desde(sensor, catalogo.GetValueOrDefault(sensor.ComunidadId)?.Nombre), ct);

        await DifundirCambiosAsync(evaluacion, ct);
    }

    /// <summary>
    /// Un sensor <i>inactivo</i> lo apagó alguien a propósito; uno <i>sin señal</i> está
    /// averiado. Mostrar el segundo con su última lectura congelada daría una falsa
    /// sensación de normalidad justo donde el sistema ya no sabe lo que ocurre.
    /// </summary>
    private bool MarcarSinSenalSiProcede(Sensor sensor, DateTime ahora)
    {
        if (sensor.Estado == EstadoSensor.SinSenal)
            return false;

        var silencio = ahora - (sensor.UltimaLectura ?? sensor.FechaCreacion);
        if (silencio < _opciones.ToleranciaSinSenal)
            return false;

        sensor.Estado = EstadoSensor.SinSenal;
        logger.LogWarning("El sensor {Codigo} lleva {Segundos:0} s sin reportar: se marca sin señal.",
            sensor.Codigo, silencio.TotalSeconds);
        return true;
    }

    // -----------------------------------------------------------------------
    // Evaluación en Alert Service
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reevalúa una comunidad fuera del ciclo: después de un valor manual, un cambio de
    /// estado o una edición del sensor. Guarda y difunde.
    /// </summary>
    /// <param name="lecturaNueva">Lectura recién agregada (aún sin guardar) que debe recibir su nivel.</param>
    public async Task ReevaluarComunidadAsync(int comunidadId, Lectura? lecturaNueva = null, CancellationToken ct = default)
    {
        var catalogo = await comunidades.ListarAsync(ct) ?? new Dictionary<int, ComunidadInfo>();
        var sensores = await db.Sensores.Where(s => s.ComunidadId == comunidadId).ToListAsync(ct);

        var evaluacion = await EvaluarAsync(sensores, catalogo, ct, soloComunidad: comunidadId);
        if (lecturaNueva is not null)
            lecturaNueva.Nivel = sensores.FirstOrDefault(s => s.Id == lecturaNueva.SensorId)?.NivelActual;

        await db.SaveChangesAsync(ct);

        if (lecturaNueva is not null && sensores.FirstOrDefault(s => s.Id == lecturaNueva.SensorId) is { } sensor)
            await notificador.LecturaAsync(LecturaDto.Desde(lecturaNueva, sensor), ct);

        await DifundirCambiosAsync(evaluacion, ct);
    }

    /// <summary>
    /// Arma la fotografía por comunidad, la manda a Alert y aplica a cada sensor el nivel
    /// devuelto. Las comunidades inactivas van vacías y marcadas: Alert cierra lo que tengan abierto.
    /// </summary>
    private async Task<RespuestaEvaluacion?> EvaluarAsync(
        IReadOnlyCollection<Sensor> sensores,
        IReadOnlyDictionary<int, ComunidadInfo> catalogo,
        CancellationToken ct,
        int? soloComunidad = null)
    {
        var ids = soloComunidad is { } unica ? [unica] : sensores.Select(s => s.ComunidadId).Distinct();

        var solicitud = new SolicitudEvaluacion(ids.Select(id =>
        {
            var info = catalogo.GetValueOrDefault(id);
            var activa = info?.Activa ?? false;
            return new ComunidadEvaluacion(
                id,
                info?.Nombre ?? $"Comunidad {id}",
                activa,
                activa
                    ? sensores.Where(s => s.ComunidadId == id)
                        .Select(s => new SensorEvaluacion(s.Id, s.Codigo, s.Nombre, s.Tipo, s.UnidadMedida, s.Estado,
                            s.ValorActual, s.ValorMinimo, s.ValorMaximo, s.UltimaLectura))
                        .ToList()
                    : []);
        }).ToList());

        var respuesta = await alertas.EvaluarAsync(solicitud, ct);
        var niveles = respuesta?.Niveles.ToDictionary(n => n.SensorId, n => n.Nivel);

        foreach (var sensor in sensores)
            sensor.NivelActual = niveles?.TryGetValue(sensor.Id, out var nivel) == true ? nivel : null;

        return respuesta;
    }

    private async Task DifundirCambiosAsync(RespuestaEvaluacion? evaluacion, CancellationToken ct)
    {
        foreach (var cambio in evaluacion?.Cambios ?? [])
            await notificador.AlertaAsync(cambio.Evento, cambio.ComunidadId, cambio.Alerta, ct);
    }

    private static bool EstaActiva(IReadOnlyDictionary<int, ComunidadInfo> catalogo, int comunidadId) =>
        catalogo.GetValueOrDefault(comunidadId)?.Activa ?? false;

    // -----------------------------------------------------------------------
    // Consultas del tablero
    // -----------------------------------------------------------------------

    public async Task<EstadoComunidadDto> ObtenerEstadoAsync(int comunidadId, CancellationToken ct = default)
    {
        var catalogo = await comunidades.ListarAsync(ct);
        var nombre = catalogo?.GetValueOrDefault(comunidadId)?.Nombre;
        if (catalogo is not null && nombre is null)
            throw new ExcepcionNoEncontrado("la comunidad", comunidadId);

        var sensores = await db.Sensores.AsNoTracking()
            .Where(s => s.ComunidadId == comunidadId)
            .OrderBy(s => s.Tipo).ThenBy(s => s.Codigo)
            .ToListAsync(ct);

        var activas = await alertas.ActivasAsync(comunidadId, ct);
        var nivelGlobal = activas?.NivelGlobal ?? NivelAlerta.Verde;

        return new EstadoComunidadDto
        {
            ComunidadId = comunidadId,
            ComunidadNombre = nombre ?? string.Empty,
            NivelGlobal = nivelGlobal,
            NivelGlobalNombre = nivelGlobal.ToString(),
            Sensores = sensores.Select(s => SensorDto.Desde(s, nombre)).ToList(),
            AlertasActivas = activas?.Alertas ?? [],
            AlertasDisponibles = activas is not null,
            Marca = DateTime.UtcNow
        };
    }

    public async Task<IReadOnlyList<SerieHistoricaDto>> ObtenerSeriesAsync(int comunidadId, int minutos, CancellationToken ct = default)
    {
        minutos = Math.Clamp(minutos, 1, 1440);
        var desde = DateTime.UtcNow.AddMinutes(-minutos);

        var sensores = await db.Sensores.AsNoTracking()
            .Where(s => s.ComunidadId == comunidadId)
            .OrderBy(s => s.Tipo)
            .ToListAsync(ct);
        var ids = sensores.Select(s => s.Id).ToList();

        var puntos = await db.Lecturas.AsNoTracking()
            .Where(l => ids.Contains(l.SensorId) && l.FechaHora >= desde)
            .OrderBy(l => l.FechaHora)
            .Select(l => new { l.SensorId, l.FechaHora, l.Valor })
            .ToListAsync(ct);
        var porSensor = puntos.ToLookup(p => p.SensorId);

        return sensores.Select(s => new SerieHistoricaDto
        {
            SensorId = s.Id,
            SensorCodigo = s.Codigo,
            SensorNombre = s.Nombre,
            Tipo = s.Tipo,
            UnidadMedida = s.UnidadMedida,
            Puntos = porSensor[s.Id].Select(p => new PuntoSerieDto(p.FechaHora, p.Valor)).ToList()
        }).ToList();
    }

    /// <summary>Indicadores de la red de sensores para el dashboard.</summary>
    public async Task<ResumenSensoresDto> ObtenerResumenAsync(int? comunidadId, CancellationToken ct = default)
    {
        var consulta = db.Sensores.AsNoTracking();
        if (comunidadId is { } id)
            consulta = consulta.Where(s => s.ComunidadId == id);

        var sensores = await consulta.Select(s => new { s.Tipo, s.Estado, s.NivelActual, s.UltimaLectura }).ToListAsync(ct);

        return new ResumenSensoresDto
        {
            TotalSensores = sensores.Count,
            Activos = sensores.Count(s => s.Estado == EstadoSensor.Activo),
            Inactivos = sensores.Count(s => s.Estado == EstadoSensor.Inactivo),
            SinSenal = sensores.Count(s => s.Estado == EstadoSensor.SinSenal),
            PorTipo = sensores.GroupBy(s => s.Tipo.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            PorNivel = sensores
                .Where(s => s.Estado == EstadoSensor.Activo && s.NivelActual is not null)
                .GroupBy(s => s.NivelActual!.Value.ToString())
                .ToDictionary(g => g.Key, g => g.Count()),
            UltimaLectura = sensores.Max(s => s.UltimaLectura)
        };
    }

    // -----------------------------------------------------------------------
    // Mantenimiento
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reinicia el monitoreo: descarta las lecturas (son simuladas), devuelve cada sensor a
    /// su valor de reposo y le pide a Alert que cierre las alertas abiertas. El historial
    /// de eventos lo conserva Alert.
    /// </summary>
    public async Task<object> ReiniciarAsync(CancellationToken ct = default)
    {
        var lecturasBorradas = await db.Lecturas.ExecuteDeleteAsync(ct);

        var ahora = DateTime.UtcNow;
        var sensores = await db.Sensores.ToListAsync(ct);
        foreach (var sensor in sensores)
        {
            sensor.ValorActual = PlantillaSensor.Para(sensor.Tipo).ValorReposo;
            sensor.Estado = EstadoSensor.Activo;
            sensor.UltimaLectura = ahora;
            sensor.NivelActual = NivelAlerta.Verde;
        }
        await db.SaveChangesAsync(ct);
        simulador.Reiniciar();

        var alertasCerradas = await alertas.ReiniciarAsync(ct);

        auditoria.Registrar("SistemaReiniciado", "Sistema", null,
            new { LecturasBorradas = lecturasBorradas, SensoresRestablecidos = sensores.Count, AlertasCerradas = alertasCerradas });
        await notificador.SistemaReiniciadoAsync(ct);

        logger.LogInformation("Monitoreo reiniciado: {Lecturas} lecturas borradas, {Sensores} sensores restablecidos.",
            lecturasBorradas, sensores.Count);

        return new
        {
            mensaje = alertasCerradas
                ? "Sistema de monitoreo reiniciado."
                : "Sensores reiniciados, pero Alert Service no respondió: las alertas abiertas siguen abiertas.",
            lecturasBorradas,
            sensoresRestablecidos = sensores.Count,
            alertasCerradas
        };
    }
}
