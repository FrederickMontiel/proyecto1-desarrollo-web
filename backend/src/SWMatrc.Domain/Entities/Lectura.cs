using SWMatrc.Domain.Common;

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

    public DateTime FechaHora { get; set; } = DateTime.UtcNow;
}
