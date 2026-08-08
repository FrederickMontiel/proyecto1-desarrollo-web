using SWMatrc.Domain.Common;

namespace SWMatrc.Domain.Entities;

/// <summary>
/// Pista de auditoría de las acciones ejecutadas por los usuarios. Se escribe siempre
/// desde el servidor, nunca desde el cliente, y no se expone ninguna operación de
/// modificación ni de borrado sobre ella.
/// </summary>
public class Bitacora
{
    public long Id { get; set; }

    public int? UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }

    /// <summary>Se copia el correo por si la cuenta se elimina más adelante.</summary>
    public string UsuarioEmail { get; set; } = string.Empty;

    /// <summary>Verbo de negocio: "SensorCreado", "AlertaReconocida", "SistemaReiniciado".</summary>
    public string Accion { get; set; } = string.Empty;

    public string Entidad { get; set; } = string.Empty;

    public string? EntidadId { get; set; }

    /// <summary>Contexto adicional serializado como JSON.</summary>
    public string? Detalle { get; set; }

    public string? DireccionIp { get; set; }

    public DateTime FechaHora { get; set; } = DateTime.UtcNow;
}
