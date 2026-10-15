using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using SWMatrc.Sensores.Dominio;

namespace SWMatrc.Sensores.Aplicacion;

// ---------------------------------------------------------------------------
// Sensores
// ---------------------------------------------------------------------------

public sealed record SensorDto
{
    public int Id { get; init; }
    public int ComunidadId { get; init; }
    public string ComunidadNombre { get; init; } = string.Empty;
    public string Codigo { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public TipoSensor Tipo { get; init; }
    public string TipoNombre { get; init; } = string.Empty;
    public string UnidadMedida { get; init; } = string.Empty;
    public EstadoSensor Estado { get; init; }
    public string? Ubicacion { get; init; }
    public string? Descripcion { get; init; }
    public DateTime? FechaInstalacion { get; init; }
    public decimal Latitud { get; init; }
    public decimal Longitud { get; init; }
    public decimal ValorMinimo { get; init; }
    public decimal ValorMaximo { get; init; }
    public decimal VariacionMaxima { get; init; }
    public decimal ValorActual { get; init; }
    public DateTime? UltimaLectura { get; init; }

    /// <summary>Color de la lectura actual según Alert Service. Nulo si no se pudo evaluar.</summary>
    public NivelAlerta? NivelActual { get; init; }

    public DateTime FechaCreacion { get; init; }

    public static SensorDto Desde(Sensor s, string? comunidadNombre) => new()
    {
        Id = s.Id,
        ComunidadId = s.ComunidadId,
        ComunidadNombre = comunidadNombre ?? string.Empty,
        Codigo = s.Codigo,
        Nombre = s.Nombre,
        Tipo = s.Tipo,
        TipoNombre = s.Tipo.ToString(),
        UnidadMedida = s.UnidadMedida,
        Estado = s.Estado,
        Ubicacion = s.Ubicacion,
        Descripcion = s.Descripcion,
        FechaInstalacion = s.FechaInstalacion,
        Latitud = s.Latitud,
        Longitud = s.Longitud,
        ValorMinimo = s.ValorMinimo,
        ValorMaximo = s.ValorMaximo,
        VariacionMaxima = s.VariacionMaxima,
        ValorActual = s.ValorActual,
        UltimaLectura = s.UltimaLectura,
        NivelActual = s.EstaOperativo ? s.NivelActual : NivelAlerta.Verde,
        FechaCreacion = s.FechaCreacion
    };
}

public sealed record FiltroSensores
{
    public int? ComunidadId { get; init; }
    public TipoSensor? Tipo { get; init; }
    public EstadoSensor? Estado { get; init; }

    /// <summary>Código completo o parcial, sin distinguir mayúsculas.</summary>
    public string? Codigo { get; init; }
}

public sealed record CrearSensorRequest
{
    [Required(ErrorMessage = "Indique la comunidad.")]
    public int ComunidadId { get; init; }

    [Required(ErrorMessage = "El código es obligatorio.")]
    [MaxLength(30, ErrorMessage = "El código admite como máximo 30 caracteres.")]
    public string Codigo { get; init; } = string.Empty;

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [MaxLength(120, ErrorMessage = "El nombre admite como máximo 120 caracteres.")]
    public string Nombre { get; init; } = string.Empty;

    [Required(ErrorMessage = "Indique el tipo de sensor.")]
    public TipoSensor Tipo { get; init; }

    [MaxLength(15, ErrorMessage = "La unidad admite como máximo 15 caracteres.")]
    public string? UnidadMedida { get; init; }

    [MaxLength(200, ErrorMessage = "La ubicación admite como máximo 200 caracteres.")]
    public string? Ubicacion { get; init; }

    [MaxLength(500, ErrorMessage = "La descripción admite como máximo 500 caracteres.")]
    public string? Descripcion { get; init; }

    public DateTime? FechaInstalacion { get; init; }
    public EstadoSensor Estado { get; init; } = EstadoSensor.Activo;

    [Range(-90, 90, ErrorMessage = "La latitud debe estar entre -90 y 90.")]
    public decimal Latitud { get; init; }

    [Range(-180, 180, ErrorMessage = "La longitud debe estar entre -180 y 180.")]
    public decimal Longitud { get; init; }

    public decimal? ValorMinimo { get; init; }
    public decimal? ValorMaximo { get; init; }
    public decimal? VariacionMaxima { get; init; }
    public decimal? ValorInicial { get; init; }
}

/// <summary>Modificación parcial: lo que llega nulo se conserva.</summary>
public sealed record ActualizarSensorRequest
{
    [MaxLength(120, ErrorMessage = "El nombre admite como máximo 120 caracteres.")]
    public string? Nombre { get; init; }

    [MaxLength(15, ErrorMessage = "La unidad admite como máximo 15 caracteres.")]
    public string? UnidadMedida { get; init; }

    public int? ComunidadId { get; init; }

    [MaxLength(200, ErrorMessage = "La ubicación admite como máximo 200 caracteres.")]
    public string? Ubicacion { get; init; }

    [MaxLength(500, ErrorMessage = "La descripción admite como máximo 500 caracteres.")]
    public string? Descripcion { get; init; }

    public DateTime? FechaInstalacion { get; init; }

    [Range(-90, 90, ErrorMessage = "La latitud debe estar entre -90 y 90.")]
    public decimal? Latitud { get; init; }

    [Range(-180, 180, ErrorMessage = "La longitud debe estar entre -180 y 180.")]
    public decimal? Longitud { get; init; }

    public decimal? ValorMinimo { get; init; }
    public decimal? ValorMaximo { get; init; }
    public decimal? VariacionMaxima { get; init; }
}

public sealed record CambiarEstadoSensorRequest
{
    [Required(ErrorMessage = "Indique el estado: Activo o Inactivo.")]
    public EstadoSensor? Estado { get; init; }
}

public sealed record EstablecerValorRequest
{
    [Required(ErrorMessage = "Indique el valor.")]
    public decimal? Valor { get; init; }
}

/// <summary>Sensores de una comunidad. Lo consume Community Service (RF-REP-01).</summary>
public sealed record ConteoSensoresDto(int ComunidadId, int Total, int Activos);

public sealed record ResumenSensoresDto
{
    public int TotalSensores { get; init; }
    public int Activos { get; init; }
    public int Inactivos { get; init; }
    public int SinSenal { get; init; }
    public IReadOnlyDictionary<string, int> PorTipo { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> PorNivel { get; init; } = new Dictionary<string, int>();
    public DateTime? UltimaLectura { get; init; }
}

// ---------------------------------------------------------------------------
// Lecturas
// ---------------------------------------------------------------------------

/// <summary>Lectura difundida por el canal en tiempo real.</summary>
public sealed record LecturaDto
{
    public long Id { get; init; }
    public int SensorId { get; init; }
    public string SensorCodigo { get; init; } = string.Empty;
    public int ComunidadId { get; init; }
    public TipoSensor Tipo { get; init; }
    public decimal Valor { get; init; }
    public string UnidadMedida { get; init; } = string.Empty;
    public NivelAlerta? Nivel { get; init; }
    public DateTime FechaHora { get; init; }

    public static LecturaDto Desde(Lectura l, Sensor s) => new()
    {
        Id = l.Id,
        SensorId = s.Id,
        SensorCodigo = s.Codigo,
        ComunidadId = s.ComunidadId,
        Tipo = s.Tipo,
        Valor = l.Valor,
        UnidadMedida = l.UnidadMedida,
        Nivel = l.Nivel,
        FechaHora = l.FechaHora
    };
}

/// <summary>Lectura almacenada, tal como se muestra en la consulta histórica y en los reportes.</summary>
public sealed record LecturaHistoricaDto
{
    public long Id { get; init; }
    public int SensorId { get; init; }
    public string SensorCodigo { get; init; } = string.Empty;
    public string SensorNombre { get; init; } = string.Empty;
    public int ComunidadId { get; init; }
    public string ComunidadNombre { get; init; } = string.Empty;
    public TipoSensor Tipo { get; init; }
    public decimal Valor { get; init; }
    public string UnidadMedida { get; init; } = string.Empty;
    public EstadoSensor EstadoSensor { get; init; }
    public NivelAlerta? Nivel { get; init; }
    public DateTime FechaHora { get; init; }
}

public sealed record FiltroLecturas
{
    public int? SensorId { get; init; }
    public int? ComunidadId { get; init; }
    public TipoSensor? Tipo { get; init; }
    public DateTime? Desde { get; init; }
    public DateTime? Hasta { get; init; }
    public int Pagina { get; init; } = 1;
    public int Tamano { get; init; } = 50;
}

public sealed record RegistrarLecturaRequest
{
    [Required(ErrorMessage = "Indique el sensor.")]
    public int? SensorId { get; init; }

    [Required(ErrorMessage = "Indique el valor.")]
    public decimal? Valor { get; init; }
}

public sealed record PaginaDto<T>
{
    public IReadOnlyList<T> Elementos { get; init; } = [];
    public int Pagina { get; init; }
    public int TamanoPagina { get; init; }
    public int TotalElementos { get; init; }
    public int TotalPaginas => TamanoPagina == 0 ? 0 : (int)Math.Ceiling(TotalElementos / (double)TamanoPagina);
}

/// <summary>Estadísticas por tipo de sensor en un periodo (RF-REP-07).</summary>
public sealed record EstadisticaTipoDto(
    TipoSensor Tipo,
    string UnidadMedida,
    int Lecturas,
    decimal Promedio,
    decimal Minimo,
    decimal Maximo,
    decimal Suma);

// ---------------------------------------------------------------------------
// Monitoreo
// ---------------------------------------------------------------------------

public sealed record SerieHistoricaDto
{
    public int SensorId { get; init; }
    public string SensorCodigo { get; init; } = string.Empty;
    public string SensorNombre { get; init; } = string.Empty;
    public TipoSensor Tipo { get; init; }
    public string UnidadMedida { get; init; } = string.Empty;
    public IReadOnlyList<PuntoSerieDto> Puntos { get; init; } = [];
}

public sealed record PuntoSerieDto(DateTime FechaHora, decimal Valor);

/// <summary>Fotografía completa de una comunidad para pintar el tablero de golpe.</summary>
public sealed record EstadoComunidadDto
{
    public int ComunidadId { get; init; }
    public string ComunidadNombre { get; init; } = string.Empty;
    public NivelAlerta NivelGlobal { get; init; }
    public string NivelGlobalNombre { get; init; } = string.Empty;
    public IReadOnlyList<SensorDto> Sensores { get; init; } = [];

    /// <summary>Tal como las redacta Alert Service.</summary>
    public IReadOnlyList<JsonElement> AlertasActivas { get; init; } = [];

    /// <summary>Falso si Alert no respondió: la lista vacía no significa "sin alertas".</summary>
    public bool AlertasDisponibles { get; init; }

    public DateTime Marca { get; init; } = DateTime.UtcNow;
}
