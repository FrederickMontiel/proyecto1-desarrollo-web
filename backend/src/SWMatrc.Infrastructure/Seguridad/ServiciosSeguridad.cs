using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Domain.Entities;

namespace SWMatrc.Infrastructure.Seguridad;

/// <summary>
/// Hasheo de contraseñas con BCrypt. El factor de trabajo se fija en 12: suficiente para
/// que un ataque por fuerza bruta sea impráctico sin penalizar el inicio de sesión.
/// </summary>
public sealed class HasheadorPasswordBCrypt : IHasheadorPassword
{
    private const int FactorTrabajo = 12;

    public string Hashear(string password) => BCrypt.Net.BCrypt.HashPassword(password, FactorTrabajo);

    public bool Verificar(string password, string hash)
    {
        // Un hash corrupto en la base no debe propagarse como error 500: se trata como
        // credencial inválida y el intento de acceso simplemente falla.
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}

/// <summary>Emisor de tokens JWT firmados con HMAC-SHA256.</summary>
public sealed class GeneradorTokenJwt(IOptions<OpcionesJwt> opciones) : IGeneradorToken
{
    private readonly OpcionesJwt _opciones = opciones.Value;

    public (string Token, DateTime Expira) Generar(Usuario usuario)
    {
        var expira = DateTime.UtcNow.AddMinutes(_opciones.MinutosVigencia);

        var credenciales = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opciones.Clave)),
            SecurityAlgorithms.HmacSha256);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, usuario.Email),
                new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
                new Claim(ClaimTypes.Name, usuario.NombreCompleto),
                new Claim(ClaimTypes.Role, usuario.Rol.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ]),
            Expires = expira,
            Issuer = _opciones.Emisor,
            Audience = _opciones.Audiencia,
            SigningCredentials = credenciales
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return (token, expira);
    }
}
