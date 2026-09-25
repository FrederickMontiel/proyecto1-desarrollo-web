using System.ComponentModel.DataAnnotations;
using SWMatrc.Domain.Entities;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Application.Dtos;

// ---------------------------------------------------------------------------
// Comunidades
// ---------------------------------------------------------------------------

/// <summary>Alta o modificación de una comunidad.</summary>
public record GuardarComunidadRequest
{
    [Required, MaxLength(120)] public string Nombre { get; init; } = string.Empty;
    [Required, MaxLength(120)] public string Municipio { get; init; } = string.Empty;
    [Required, MaxLength(120)] public string Departamento { get; init; } = string.Empty;
    [Required, MaxLength(80)] public string Pais { get; init; } = string.Empty;
    [Range(-90, 90)] public decimal Latitud { get; init; }
    [Range(-180, 180)] public decimal Longitud { get; init; }
    [Range(0, int.MaxValue)] public int Poblacion { get; init; }
    [MaxLength(500)] public string? Descripcion { get; init; }
    public bool Activa { get; init; } = true;
}

public record FiltroComunidades
{
    /// <summary>Texto libre sobre nombre, municipio, departamento o país.</summary>
    public string? Busqueda { get; init; }
    public bool? Activa { get; init; }
    public string? Municipio { get; init; }
    public string? Departamento { get; init; }
}

// ---------------------------------------------------------------------------
// Sensores
// ---------------------------------------------------------------------------

public record FiltroSensores
{
    public int? ComunidadId { get; init; }
    public TipoSensor? Tipo { get; init; }
    public EstadoSensor? Estado { get; init; }

    /// <summary>Código completo o parcial, sin distinguir mayúsculas.</summary>
    public string? Codigo { get; init; }
}

// ---------------------------------------------------------------------------
// Reglas de alerta
// ---------------------------------------------------------------------------

public record ReglaAlertaDto
{
    public int Id { get; init; }
    public string Nombre { get; init; } = string.Empty;
    public TipoSensor TipoSensor { get; init; }
    public decimal? ValorMinimo { get; init; }
    public decimal? ValorMaximo { get; init; }
    public NivelAlerta Nivel { get; init; }
    public TipoFenomeno Fenomeno { get; init; }
    public string Mensaje { get; init; } = string.Empty;
    public bool Activa { get; init; }
    public DateTime FechaCreacion { get; init; }
    public DateTime? FechaModificacion { get; init; }

    public static ReglaAlertaDto Desde(ReglaAlerta r) => new()
    {
        Id = r.Id,
        Nombre = r.Nombre,
        TipoSensor = r.TipoSensor,
        ValorMinimo = r.ValorMinimo,
        ValorMaximo = r.ValorMaximo,
        Nivel = r.Nivel,
        Fenomeno = r.Fenomeno,
        Mensaje = r.Mensaje,
        Activa = r.Activa,
        FechaCreacion = r.FechaCreacion,
        FechaModificacion = r.FechaModificacion
    };
}

public record GuardarReglaRequest
{
    [Required, MaxLength(120)] public string Nombre { get; init; } = string.Empty;
    [Required] public TipoSensor TipoSensor { get; init; }
    public decimal? ValorMinimo { get; init; }
    public decimal? ValorMaximo { get; init; }
    [Required] public NivelAlerta Nivel { get; init; }
    [Required] public TipoFenomeno Fenomeno { get; init; }
    [Required, MaxLength(500)] public string Mensaje { get; init; } = string.Empty;
    public bool Activa { get; init; } = true;
}

public record FiltroReglas
{
    public string? Busqueda { get; init; }
    public TipoSensor? TipoSensor { get; init; }
    public TipoFenomeno? Fenomeno { get; init; }
    public NivelAlerta? Nivel { get; init; }
    public bool? Activa { get; init; }
}

// ---------------------------------------------------------------------------
// Lecturas
// ---------------------------------------------------------------------------

/// <summary>Lectura almacenada, tal como se muestra en la consulta histórica.</summary>
public record LecturaHistoricaDto
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
    public NivelAlerta Nivel { get; init; }
    public DateTime FechaHora { get; init; }

    public static LecturaHistoricaDto Desde(Lectura l) => new()
    {
        Id = l.Id,
        SensorId = l.SensorId,
        SensorCodigo = l.Sensor?.Codigo ?? string.Empty,
        SensorNombre = l.Sensor?.Nombre ?? string.Empty,
        ComunidadId = l.Sensor?.ComunidadId ?? 0,
        ComunidadNombre = l.Sensor?.Comunidad?.Nombre ?? string.Empty,
        Tipo = l.Sensor?.Tipo ?? default,
        Valor = l.Valor,
        UnidadMedida = string.IsNullOrEmpty(l.UnidadMedida) ? l.Sensor?.UnidadMedida ?? string.Empty : l.UnidadMedida,
        EstadoSensor = l.EstadoSensor,
        Nivel = l.Sensor?.Clasificar(l.Valor) ?? NivelAlerta.Verde,
        FechaHora = l.FechaHora
    };
}

public record FiltroLecturas
{
    public int? SensorId { get; init; }
    public int? ComunidadId { get; init; }
    public TipoSensor? Tipo { get; init; }
    public DateTime? Desde { get; init; }
    public DateTime? Hasta { get; init; }
    public int Pagina { get; init; } = 1;
    public int Tamano { get; init; } = 50;
}

/// <summary>Registro manual de una lectura por parte de un operador.</summary>
public record RegistrarLecturaRequest
{
    [Required] public int SensorId { get; init; }
    [Required] public decimal Valor { get; init; }
}

// ---------------------------------------------------------------------------
// Alertas
// ---------------------------------------------------------------------------

public record FiltroAlertas
{
    public int? ComunidadId { get; init; }
    public int? SensorId { get; init; }
    public TipoFenomeno? Fenomeno { get; init; }
    public NivelAlerta? Nivel { get; init; }
    public EstadoAlerta? Estado { get; init; }
    public DateTime? Desde { get; init; }
    public DateTime? Hasta { get; init; }
    public int Pagina { get; init; } = 1;
    public int Tamano { get; init; } = 20;
}

/// <summary>Observación opcional que el responsable deja al atender o cerrar una alerta.</summary>
public record GestionAlertaRequest
{
    [MaxLength(300)] public string? Comentario { get; init; }
}

// ---------------------------------------------------------------------------
// Historial, usuarios y bitácora
// ---------------------------------------------------------------------------

public record FiltroHistorial
{
    public int? ComunidadId { get; init; }
    public TipoFenomeno? Fenomeno { get; init; }
    public NivelAlerta? Nivel { get; init; }
    public EstadoAlerta? Estado { get; init; }
    public DateTime? Desde { get; init; }
    public DateTime? Hasta { get; init; }
    public int Pagina { get; init; } = 1;
    public int Tamano { get; init; } = 20;
}

/// <summary>Estadísticas agregadas sobre los eventos que cumplen el filtro.</summary>
public record EstadisticasHistorialDto
{
    public int TotalEventos { get; init; }
    public int EventosAbiertos { get; init; }
    public double? DuracionPromedioMinutos { get; init; }
    public IReadOnlyDictionary<string, int> PorFenomeno { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> PorNivel { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> PorComunidad { get; init; } = new Dictionary<string, int>();

    /// <summary>Eventos por día (aaaa-mm-dd), en orden cronológico.</summary>
    public IReadOnlyDictionary<string, int> PorDia { get; init; } = new Dictionary<string, int>();
}

public record FiltroUsuarios
{
    /// <summary>Texto libre sobre nombre o correo.</summary>
    public string? Busqueda { get; init; }
    public RolUsuario? Rol { get; init; }
    public bool? Activo { get; init; }
}

public record FiltroBitacora
{
    /// <summary>Correo (o parte de él) del usuario que ejecutó la acción.</summary>
    public string? Usuario { get; init; }
    public string? Accion { get; init; }
    public string? Entidad { get; init; }
    public DateTime? Desde { get; init; }
    public DateTime? Hasta { get; init; }
    public int Pagina { get; init; } = 1;
    public int Tamano { get; init; } = 30;
}
