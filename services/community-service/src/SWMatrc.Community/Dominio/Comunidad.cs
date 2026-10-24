namespace SWMatrc.Community.Dominio;

/// <summary>
/// Comunidad monitoreada. Es la dueña de la información geográfica; los sensores la
/// referencian solo por <see cref="Id"/> (asociación lógica, sin clave foránea entre
/// bases), porque viven en otro servicio y en otro motor.
/// </summary>
public class Comunidad
{
    public int Id { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string Municipio { get; set; } = string.Empty;

    public string Departamento { get; set; } = string.Empty;

    public string Pais { get; set; } = string.Empty;

    public string? Descripcion { get; set; }

    /// <summary>Grados decimales, WGS84.</summary>
    public decimal Latitud { get; set; }

    /// <summary>Grados decimales, WGS84.</summary>
    public decimal Longitud { get; set; }

    public int Poblacion { get; set; }

    /// <summary>Desactivada, la comunidad deja de simular lecturas y de generar alertas.</summary>
    public bool Activa { get; set; } = true;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? FechaModificacion { get; set; }
}
