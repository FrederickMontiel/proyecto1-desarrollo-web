using SWMatrc.Domain.Entities;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Application.Abstracciones;

/// <summary>Cifrado y verificación de contraseñas.</summary>
public interface IHasheadorPassword
{
    string Hashear(string password);
    bool Verificar(string password, string hash);
}

/// <summary>Emisión de tokens de acceso.</summary>
public interface IGeneradorToken
{
    /// <returns>El token firmado y el instante en que expira.</returns>
    (string Token, DateTime Expira) Generar(Usuario usuario);
}

/// <summary>Identidad del usuario que origina la petición en curso.</summary>
public interface IUsuarioActual
{
    int? Id { get; }
    string? Email { get; }
    RolUsuario? Rol { get; }
    string? DireccionIp { get; }
    bool Autenticado { get; }
}
