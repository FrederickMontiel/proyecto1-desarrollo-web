using SWMatrc.Domain.Common;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Domain.Entities;

/// <summary>
/// Muestra puntual tomada por un sensor. Es la tabla de mayor crecimiento del sistema,
/// por eso se mantiene deliberadamente estrecha y se indexa por (SensorId, FechaHora).
/// </summary>
public class Lectura
{
    public long Id { get; set; }

    public int SensorId { get; set; }
    public Sensor? Sensor { get; set; }

    public decimal Valor { get; set; }

    /// <summary>Unidad vigente al tomar la muestra; se copia por si el sensor se recalibra después.</summary>
    public string UnidadMedida { get; set; } = string.Empty;

    /// <summary>Estado del sensor en el instante de la lectura.</summary>
    public EstadoSensor EstadoSensor { get; set; } = EstadoSensor.Activo;

    public DateTime FechaHora { get; set; } = DateTime.UtcNow;
}
