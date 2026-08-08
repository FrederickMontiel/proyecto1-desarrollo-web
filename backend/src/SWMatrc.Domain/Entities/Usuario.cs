using SWMatrc.Domain.Common;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Domain.Entities;

/// <summary>Cuenta con la que una persona accede al sistema de monitoreo.</summary>
public class Usuario : EntidadBase
{
    public string NombreCompleto { get; set; } = string.Empty;

    /// <summary>Identificador de inicio de sesión. Único en toda la instalación.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Hash BCrypt de la contraseña. Nunca se expone fuera de la capa de infraestructura.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public RolUsuario Rol { get; set; } = RolUsuario.Consulta;

    public bool Activo { get; set; } = true;

    public DateTime? UltimoAcceso { get; set; }

    public ICollection<Bitacora> AccionesRegistradas { get; set; } = [];
}
