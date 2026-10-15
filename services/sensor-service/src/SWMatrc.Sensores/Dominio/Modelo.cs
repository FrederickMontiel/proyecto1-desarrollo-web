namespace SWMatrc.Sensores.Dominio;

/// <summary>Magnitud física que mide un sensor. Los valores numéricos son los de la Fase 2.</summary>
public enum TipoSensor
{
    Temperatura = 1,
    Humedad = 2,
    Viento = 3,
    Lluvia = 4,
    NivelRio = 5,
    NivelReservorio = 6,
    /// <summary>Detector de humo o de focos de calor para incendios forestales.</summary>
    Humo = 7,
    /// <summary>Cualquier otra magnitud ambiental.</summary>
    Otro = 8
}

/// <summary>Estado operativo de un sensor dentro de la red.</summary>
public enum EstadoSensor
{
    Inactivo = 0,
    Activo = 1,
    /// <summary>Activo pero sin reportar dentro del tiempo esperado ("sin comunicación").</summary>
    SinSenal = 2
}

/// <summary>Código de color del protocolo de alerta temprana. A mayor valor, mayor severidad.</summary>
public enum NivelAlerta
{
    Verde = 0,
    Amarillo = 1,
    Naranja = 2,
    Rojo = 3
}

/// <summary>
/// Estación de medición instalada en una comunidad.
/// </summary>
/// <remarks>
/// En la Fase 2 el sensor llevaba también sus umbrales de alerta. En la Fase 3 los umbrales
/// pertenecen al Alert Service ("reglas y umbrales"); aquí queda la parte física: qué mide,
/// dónde está, su rango admisible y su última lectura. La comunidad se referencia solo por
/// <see cref="ComunidadId"/>: vive en otro servicio y en otro motor de base de datos.
/// </remarks>
public class Sensor
{
    public int Id { get; set; }

    /// <summary>Referencia lógica a Community Service. No hay clave foránea entre bases.</summary>
    public int ComunidadId { get; set; }

    /// <summary>Código de inventario visible al operador, p. ej. "TEMP-01".</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;

    public TipoSensor Tipo { get; set; }

    /// <summary>Unidad en la que reporta: °C, %, km/h, mm, m.</summary>
    public string UnidadMedida { get; set; } = string.Empty;

    public EstadoSensor Estado { get; set; } = EstadoSensor.Activo;

    /// <summary>Referencia legible del punto de instalación: "Puente de la entrada".</summary>
    public string? Ubicacion { get; set; }

    public string? Descripcion { get; set; }

    public DateTime? FechaInstalacion { get; set; }

    public decimal Latitud { get; set; }
    public decimal Longitud { get; set; }

    // --- Rango físico admisible. Acota la simulación y descarta lecturas imposibles. ---
    public decimal ValorMinimo { get; set; }
    public decimal ValorMaximo { get; set; }

    /// <summary>Amplitud máxima del ruido de la simulación en cada ciclo.</summary>
    public decimal VariacionMaxima { get; set; } = 1m;

    /// <summary>Última lectura conocida, desnormalizada para que el dashboard no recorra el histórico.</summary>
    public decimal ValorActual { get; set; }

    public DateTime? UltimaLectura { get; set; }

    /// <summary>
    /// Color de la última lectura según los umbrales del Alert Service. Nulo si Alert no
    /// respondió en la última evaluación: el sistema no inventa un "verde".
    /// </summary>
    public NivelAlerta? NivelActual { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? FechaModificacion { get; set; }

    public ICollection<Lectura> Lecturas { get; set; } = [];

    /// <summary>Solo un sensor en servicio alimenta la evaluación de riesgo.</summary>
    public bool EstaOperativo => Estado == EstadoSensor.Activo;

    /// <summary>La simulación le pide valor a los activos y a los sin señal, que podrían volver a reportar.</summary>
    public bool DebeReportar => Estado is EstadoSensor.Activo or EstadoSensor.SinSenal;
}

/// <summary>Una medición almacenada.</summary>
public class Lectura
{
    public long Id { get; set; }

    public int SensorId { get; set; }
    public Sensor? Sensor { get; set; }

    public decimal Valor { get; set; }

    public string UnidadMedida { get; set; } = string.Empty;

    public EstadoSensor EstadoSensor { get; set; } = EstadoSensor.Activo;

    /// <summary>
    /// Nivel que tenía la lectura al registrarse. Se guarda en lugar de recalcularse al
    /// consultar: si después se recalibran los umbrales, el historial sigue mostrando el
    /// color con el que se vio en su momento. Nulo si Alert no respondió.
    /// </summary>
    public NivelAlerta? Nivel { get; set; }

    public DateTime FechaHora { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Calibración de fábrica de un tipo de sensor: unidad, rango físico, valor de reposo y
/// amplitud del ruido. Los umbrales de alerta recomendados viven en el Alert Service.
/// </summary>
public sealed record PlantillaSensor(
    TipoSensor Tipo,
    string Unidad,
    decimal ValorMinimo,
    decimal ValorMaximo,
    decimal ValorReposo,
    decimal VariacionMaxima)
{
    private static readonly Dictionary<TipoSensor, PlantillaSensor> Catalogo = new()
    {
        [TipoSensor.Temperatura] = new(TipoSensor.Temperatura, "°C", -10m, 50m, 24m, 0.8m),
        [TipoSensor.Humedad] = new(TipoSensor.Humedad, "%", 0m, 100m, 62m, 2.5m),
        [TipoSensor.Viento] = new(TipoSensor.Viento, "km/h", 0m, 150m, 12m, 3.5m),
        // Precipitación acumulada en la última hora.
        [TipoSensor.Lluvia] = new(TipoSensor.Lluvia, "mm", 0m, 120m, 1.5m, 2.0m),
        // Altura del cauce sobre el lecho.
        [TipoSensor.NivelRio] = new(TipoSensor.NivelRio, "m", 0m, 8m, 1.8m, 0.15m),
        // Porcentaje de llenado del reservorio.
        [TipoSensor.NivelReservorio] = new(TipoSensor.NivelReservorio, "%", 0m, 100m, 60m, 1.5m),
        // Concentración de partículas finas (PM2.5).
        [TipoSensor.Humo] = new(TipoSensor.Humo, "µg/m³", 0m, 500m, 12m, 6m)
    };

    public static PlantillaSensor Para(TipoSensor tipo) =>
        Catalogo.TryGetValue(tipo, out var plantilla) ? plantilla : new PlantillaSensor(tipo, "", 0m, 100m, 50m, 1m);

    public static IEnumerable<PlantillaSensor> Todas => Enum.GetValues<TipoSensor>().Select(Para);
}
