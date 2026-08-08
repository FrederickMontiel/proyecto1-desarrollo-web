using SWMatrc.Domain.Common;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Domain.Entities;

/// <summary>
/// Asiento inmutable del historial de incidentes. Mientras <see cref="Alerta"/> refleja
/// el estado vivo del sistema, esta tabla conserva el registro definitivo de cada
/// episodio para consulta e informes, incluso si la alerta original se depura.
/// </summary>
public class EventoHistorial : EntidadBase
{
    public int ComunidadId { get; set; }
    public Comunidad? Comunidad { get; set; }

    public int? AlertaId { get; set; }
    public Alerta? Alerta { get; set; }

    public TipoFenomeno Fenomeno { get; set; }

    public NivelAlerta NivelMaximo { get; set; }

    public string Descripcion { get; set; } = string.Empty;

    public DateTime FechaInicio { get; set; } = DateTime.UtcNow;

    public DateTime? FechaFin { get; set; }

    /// <summary>Nombre del sensor al momento del evento; se copia para que el historial sobreviva al borrado del sensor.</summary>
    public string OrigenSensor { get; set; } = string.Empty;

    public decimal? ValorRegistrado { get; set; }

    public TimeSpan? Duracion => FechaFin is null ? null : FechaFin - FechaInicio;
}
