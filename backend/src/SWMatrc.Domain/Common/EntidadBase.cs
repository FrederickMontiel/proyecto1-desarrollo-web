namespace SWMatrc.Domain.Common;

/// <summary>
/// Raíz común de las entidades persistidas. Centraliza la clave primaria y las
/// marcas de tiempo de auditoría para no repetirlas en cada entidad.
/// </summary>
public abstract class EntidadBase
{
    public int Id { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? FechaModificacion { get; set; }
}
