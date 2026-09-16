using System.ComponentModel.DataAnnotations;
using SWMatrc.Domain.Entities;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Application.Dtos;

/// <summary>Proyección de un sensor para el dashboard y la pantalla de administración.</summary>
public record SensorDto
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
    public decimal? UmbralAmarilloAlto { get; init; }
    public decimal? UmbralNaranjaAlto { get; init; }
    public decimal? UmbralRojoAlto { get; init; }
    public decimal? UmbralAmarilloBajo { get; init; }
    public decimal? UmbralNaranjaBajo { get; init; }
    public decimal? UmbralRojoBajo { get; init; }

    /// <summary>Color que corresponde a la lectura actual según los umbrales del propio sensor.</summary>
    public NivelAlerta NivelActual { get; init; }

    public static SensorDto Desde(Sensor s) => new()
    {
        Id = s.Id,
        ComunidadId = s.ComunidadId,
        ComunidadNombre = s.Comunidad?.Nombre ?? string.Empty,
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
        UmbralAmarilloAlto = s.UmbralAmarilloAlto,
        UmbralNaranjaAlto = s.UmbralNaranjaAlto,
        UmbralRojoAlto = s.UmbralRojoAlto,
        UmbralAmarilloBajo = s.UmbralAmarilloBajo,
        UmbralNaranjaBajo = s.UmbralNaranjaBajo,
        UmbralRojoBajo = s.UmbralRojoBajo,
        NivelActual = s.EstaOperativo ? s.Clasificar(s.ValorActual) : NivelAlerta.Verde
    };
}

/// <summary>Datos de alta de un sensor simulado.</summary>
public record CrearSensorRequest
{
    [Required] public int ComunidadId { get; init; }
    [Required, MaxLength(30)] public string Codigo { get; init; } = string.Empty;
    [Required, MaxLength(120)] public string Nombre { get; init; } = string.Empty;
    [Required] public TipoSensor Tipo { get; init; }
    [MaxLength(15)] public string? UnidadMedida { get; init; }
    [MaxLength(200)] public string? Ubicacion { get; init; }
    [MaxLength(500)] public string? Descripcion { get; init; }
    public DateTime? FechaInstalacion { get; init; }
    public EstadoSensor Estado { get; init; } = EstadoSensor.Activo;
    public decimal Latitud { get; init; }
    public decimal Longitud { get; init; }
    public decimal? ValorMinimo { get; init; }
    public decimal? ValorMaximo { get; init; }
    public decimal? VariacionMaxima { get; init; }
    public decimal? ValorInicial { get; init; }
    public decimal? UmbralAmarilloAlto { get; init; }
    public decimal? UmbralNaranjaAlto { get; init; }
    public decimal? UmbralRojoAlto { get; init; }
    public decimal? UmbralAmarilloBajo { get; init; }
    public decimal? UmbralNaranjaBajo { get; init; }
    public decimal? UmbralRojoBajo { get; init; }
}

/// <summary>Modificación de un sensor existente. Los campos nulos conservan su valor actual.</summary>
public record ActualizarSensorRequest
{
    [MaxLength(120)] public string? Nombre { get; init; }
    [MaxLength(15)] public string? UnidadMedida { get; init; }
    public int? ComunidadId { get; init; }
    [MaxLength(200)] public string? Ubicacion { get; init; }
    [MaxLength(500)] public string? Descripcion { get; init; }
    public DateTime? FechaInstalacion { get; init; }
    public decimal? Latitud { get; init; }
    public decimal? Longitud { get; init; }
    public decimal? ValorMinimo { get; init; }
    public decimal? ValorMaximo { get; init; }
    public decimal? VariacionMaxima { get; init; }
    public decimal? UmbralAmarilloAlto { get; init; }
    public decimal? UmbralNaranjaAlto { get; init; }
    public decimal? UmbralRojoAlto { get; init; }
    public decimal? UmbralAmarilloBajo { get; init; }
    public decimal? UmbralNaranjaBajo { get; init; }
    public decimal? UmbralRojoBajo { get; init; }
}

/// <summary>Inyección manual de una lectura, usada por el operador para ensayar escenarios.</summary>
public record EstablecerValorRequest
{
    [Required] public decimal Valor { get; init; }
}
