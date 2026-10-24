using System.ComponentModel.DataAnnotations;
using SWMatrc.Community.Dominio;

namespace SWMatrc.Community.Aplicacion;

/// <summary>Alta o modificación de una comunidad.</summary>
public sealed record GuardarComunidadRequest
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [MaxLength(120, ErrorMessage = "El nombre admite como máximo 120 caracteres.")]
    public string Nombre { get; init; } = string.Empty;

    [Required(ErrorMessage = "El municipio es obligatorio.")]
    [MaxLength(120, ErrorMessage = "El municipio admite como máximo 120 caracteres.")]
    public string Municipio { get; init; } = string.Empty;

    [Required(ErrorMessage = "El departamento es obligatorio.")]
    [MaxLength(120, ErrorMessage = "El departamento admite como máximo 120 caracteres.")]
    public string Departamento { get; init; } = string.Empty;

    [Required(ErrorMessage = "El país es obligatorio.")]
    [MaxLength(80, ErrorMessage = "El país admite como máximo 80 caracteres.")]
    public string Pais { get; init; } = string.Empty;

    [Range(-90, 90, ErrorMessage = "La latitud debe estar entre -90 y 90.")]
    public decimal Latitud { get; init; }

    [Range(-180, 180, ErrorMessage = "La longitud debe estar entre -180 y 180.")]
    public decimal Longitud { get; init; }

    [Range(0, int.MaxValue, ErrorMessage = "La población no puede ser negativa.")]
    public int Poblacion { get; init; }

    [MaxLength(500, ErrorMessage = "La descripción admite como máximo 500 caracteres.")]
    public string? Descripcion { get; init; }

    public bool Activa { get; init; } = true;
}

public sealed record CambiarEstadoRequest
{
    [Required(ErrorMessage = "Indique si la comunidad queda activa.")]
    public bool? Activa { get; init; }
}

public sealed record FiltroComunidades
{
    /// <summary>Texto libre sobre nombre, municipio, departamento o país.</summary>
    public string? Busqueda { get; init; }
    public bool? Activa { get; init; }
    public string? Municipio { get; init; }
    public string? Departamento { get; init; }
}

/// <summary>Sensores de una comunidad según el Sensor Service.</summary>
public sealed record ConteoSensores(int ComunidadId, int Total, int Activos);

public sealed record ComunidadDto
{
    public int Id { get; init; }
    public string Nombre { get; init; } = string.Empty;
    public string Municipio { get; init; } = string.Empty;
    public string Departamento { get; init; } = string.Empty;
    public string Pais { get; init; } = string.Empty;
    public string? Descripcion { get; init; }
    public decimal Latitud { get; init; }
    public decimal Longitud { get; init; }
    public int Poblacion { get; init; }
    public bool Activa { get; init; }
    public int TotalSensores { get; init; }
    public int SensoresActivos { get; init; }

    /// <summary>
    /// Falso si el Sensor Service no respondió: los conteos valen 0 pero no significan
    /// "sin sensores". El frontend puede mostrar "—" en lugar de un cero engañoso.
    /// </summary>
    public bool ConteoSensoresDisponible { get; init; }

    public DateTime FechaCreacion { get; init; }
    public DateTime? FechaModificacion { get; init; }

    public static ComunidadDto Desde(Comunidad c, IReadOnlyDictionary<int, ConteoSensores>? conteos) => new()
    {
        Id = c.Id,
        Nombre = c.Nombre,
        Municipio = c.Municipio,
        Departamento = c.Departamento,
        Pais = c.Pais,
        Descripcion = c.Descripcion,
        Latitud = c.Latitud,
        Longitud = c.Longitud,
        Poblacion = c.Poblacion,
        Activa = c.Activa,
        TotalSensores = conteos?.GetValueOrDefault(c.Id)?.Total ?? 0,
        SensoresActivos = conteos?.GetValueOrDefault(c.Id)?.Activos ?? 0,
        ConteoSensoresDisponible = conteos is not null,
        FechaCreacion = c.FechaCreacion,
        FechaModificacion = c.FechaModificacion
    };
}

/// <summary>Vista compacta para otros microservicios. No consulta al Sensor Service: así Sensor puede pedirla sin crear un ciclo.</summary>
public sealed record ComunidadInternaDto(
    int Id,
    string Nombre,
    string Municipio,
    string Departamento,
    string Pais,
    decimal Latitud,
    decimal Longitud,
    int Poblacion,
    bool Activa)
{
    public static ComunidadInternaDto Desde(Comunidad c) =>
        new(c.Id, c.Nombre, c.Municipio, c.Departamento, c.Pais, c.Latitud, c.Longitud, c.Poblacion, c.Activa);
}
