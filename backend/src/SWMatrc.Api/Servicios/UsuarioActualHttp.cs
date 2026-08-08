using System.Security.Claims;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Api.Servicios;

/// <summary>
/// Lee la identidad del token de la petición en curso. Fuera de una petición HTTP —por
/// ejemplo durante un ciclo del simulador— todas sus propiedades son nulas, y la bitácora
/// atribuye entonces la acción al sistema.
/// </summary>
public sealed class UsuarioActualHttp(IHttpContextAccessor accessor) : IUsuarioActual
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public int? Id =>
        int.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    public RolUsuario? Rol =>
        Enum.TryParse<RolUsuario>(Principal?.FindFirstValue(ClaimTypes.Role), out var rol) ? rol : null;

    /// <summary>
    /// Detrás del proxy de nginx la dirección remota es la del propio contenedor, así que
    /// se prefiere la cabecera reenviada cuando está presente.
    /// </summary>
    public string? DireccionIp
    {
        get
        {
            var contexto = accessor.HttpContext;
            if (contexto is null) return null;

            var reenviada = contexto.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(reenviada))
                return reenviada.Split(',')[0].Trim();

            return contexto.Connection.RemoteIpAddress?.ToString();
        }
    }

    public bool Autenticado => Principal?.Identity?.IsAuthenticated ?? false;
}
