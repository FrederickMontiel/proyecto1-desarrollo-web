using SWMatrc.Domain.Common;

namespace SWMatrc.Domain.Entities;

/// <summary>
/// Localidad rural monitoreada. Es la unidad de escalabilidad del sistema: agregar
/// una comunidad nueva no requiere cambios de esquema ni de código.
/// </summary>
public class Comunidad : EntidadBase
{
    public string Nombre { get; set; } = string.Empty;

    public string Municipio { get; set; } = string.Empty;

    public string Departamento { get; set; } = string.Empty;

    public string Pais { get; set; } = string.Empty;

    public string? Descripcion { get; set; }

    public decimal Latitud { get; set; }

    public decimal Longitud { get; set; }

    /// <summary>Habitantes estimados. Se usa para priorizar la atención de emergencias.</summary>
    public int Poblacion { get; set; }

    public bool Activa { get; set; } = true;

    public ICollection<Sensor> Sensores { get; set; } = [];

    public ICollection<Alerta> Alertas { get; set; } = [];
}
