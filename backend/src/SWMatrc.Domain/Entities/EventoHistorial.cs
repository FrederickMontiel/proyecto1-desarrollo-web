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

    /// <summary>Sensor disparador. Queda en nulo si el sensor se elimina; <see cref="OrigenSensor"/> conserva su nombre.</summary>
    public int? SensorId { get; set; }
    public Sensor? Sensor { get; set; }

    /// <summary>Refleja el estado de la alerta asociada: activa, atendida o cerrada.</summary>
    public EstadoAlerta Estado { get; set; } = EstadoAlerta.Activa;

    /// <summary>Usuario que atendió o cerró el episodio, cuando corresponde.</summary>
    public int? UsuarioResponsableId { get; set; }
    public Usuario? UsuarioResponsable { get; set; }

    public TimeSpan? Duracion => FechaFin is null ? null : FechaFin - FechaInicio;
}
