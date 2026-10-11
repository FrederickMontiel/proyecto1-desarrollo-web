using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace SWMatrc.Comun;

/// <summary>Códigos de permiso emitidos por el Auth Service (ver auth-service/src/usuarios/catalogo.ts).</summary>
public static class Permisos
{
    public const string ComunidadesLeer = "comunidades:leer";
    public const string ComunidadesEditar = "comunidades:editar";
    public const string SensoresLeer = "sensores:leer";
    public const string SensoresEditar = "sensores:editar";
    public const string SensoresOperar = "sensores:operar";
    public const string LecturasEditar = "lecturas:editar";
    public const string AlertasLeer = "alertas:leer";
    public const string AlertasAtender = "alertas:atender";
    public const string ReglasEditar = "reglas:editar";
    public const string ReportesGenerar = "reportes:generar";
    public const string ReportesDescargar = "reportes:descargar";
    public const string AuditoriaLeer = "auditoria:leer";
    public const string UsuariosAdministrar = "usuarios:administrar";
    public const string SistemaAdministrar = "sistema:administrar";

    internal static readonly string[] Todos =
    [
        ComunidadesLeer, ComunidadesEditar, SensoresLeer, SensoresEditar, SensoresOperar, LecturasEditar,
        AlertasLeer, AlertasAtender, ReglasEditar, ReportesGenerar, ReportesDescargar,
        AuditoriaLeer, UsuariosAdministrar, SistemaAdministrar
    ];
}

/// <summary>Nombres de los esquemas y políticas de autorización.</summary>
public static class Politicas
{
    /// <summary>Esquema de los tokens de usuario (audiencia del frontend). Es el predeterminado.</summary>
    public const string EsquemaUsuario = "Usuario";

    /// <summary>Esquema de los tokens de servicio (audiencia interna).</summary>
    public const string EsquemaServicio = "Servicio";

    /// <summary>Política de las rutas <c>/internal</c>: solo otros microservicios.</summary>
    public const string SoloServicios = "solo-servicios";

    internal const string PermisoInterno = "interno";
    internal const string ClaimPermisos = "permissions";
}

public static class ExtensionesSeguridad
{
    /// <summary>
    /// Valida los JWT RS256 del Auth Service con la llave publicada en su JWKS. Registra dos
    /// esquemas —usuario y servicio— que solo se distinguen por la audiencia, de modo que un
    /// token de un tipo nunca sirve donde se espera el otro.
    ///
    /// Cada permiso del catálogo queda registrado como una política con su mismo nombre:
    /// <c>[Authorize(Policy = Permisos.ComunidadesEditar)]</c>.
    /// </summary>
    /// <param name="rutaTokenEnQuery">
    /// Prefijo de ruta donde se acepta el token en <c>?access_token=</c> (el hub de SignalR,
    /// porque el navegador no puede mandar cabeceras en el handshake del WebSocket).
    /// </param>
    public static IServiceCollection AgregarSeguridadSwmatrc(
        this IServiceCollection services, IConfiguration configuracion, string? rutaTokenEnQuery = null)
    {
        var opciones = configuracion.GetSection(OpcionesAutenticacion.Seccion).Get<OpcionesAutenticacion>() ?? new();

        void Configurar(JwtBearerOptions o, string audiencia)
        {
            o.MetadataAddress = opciones.UrlMetadatos;
            o.RequireHttpsMetadata = opciones.RequiereHttps;
            // Los claims se leen con su nombre del JWT (sub, role, permissions), sin la
            // traducción a los URI largos de WS-Federation.
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = opciones.Emisor,
                ValidAudience = audiencia,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                NameClaimType = "email",
                RoleClaimType = "role",
                ClockSkew = TimeSpan.FromMinutes(1)
            };

            if (rutaTokenEnQuery is not null)
            {
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = contexto =>
                    {
                        var token = contexto.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && contexto.Request.Path.StartsWithSegments(rutaTokenEnQuery))
                            contexto.Token = token;
                        return Task.CompletedTask;
                    }
                };
            }
        }

        services
            .AddAuthentication(Politicas.EsquemaUsuario)
            .AddJwtBearer(Politicas.EsquemaUsuario, o => Configurar(o, opciones.Audiencia))
            .AddJwtBearer(Politicas.EsquemaServicio, o => Configurar(o, opciones.AudienciaInterna));

        services.AddAuthorization(autorizacion =>
        {
            // Sin [Authorize] explícito, toda ruta exige al menos un token de usuario válido.
            autorizacion.FallbackPolicy = new AuthorizationPolicyBuilder(Politicas.EsquemaUsuario)
                .RequireAuthenticatedUser()
                .Build();

            foreach (var permiso in Permisos.Todos)
            {
                autorizacion.AddPolicy(permiso, p => p
                    .AddAuthenticationSchemes(Politicas.EsquemaUsuario)
                    .RequireAuthenticatedUser()
                    .RequireClaim(Politicas.ClaimPermisos, permiso));
            }

            autorizacion.AddPolicy(Politicas.SoloServicios, p => p
                .AddAuthenticationSchemes(Politicas.EsquemaServicio)
                .RequireAuthenticatedUser()
                .RequireClaim(Politicas.ClaimPermisos, Politicas.PermisoInterno));
        });

        services.AddHttpContextAccessor();
        services.AddScoped<UsuarioActual>();
        return services;
    }
}

/// <summary>Datos de quien hace la petición en curso, leídos del token y de las cabeceras.</summary>
public sealed class UsuarioActual(IHttpContextAccessor accesor)
{
    private ClaimsPrincipal? Principal => accesor.HttpContext?.User;

    /// <summary>Id del usuario; nulo para tokens de servicio (<c>sub = svc:…</c>) o peticiones anónimas.</summary>
    public int? Id => int.TryParse(Principal?.FindFirst("sub")?.Value, out var id) ? id : null;

    public string Email => Principal?.FindFirst("email")?.Value ?? "sistema";

    public string? Rol => Principal?.FindFirst("role")?.Value;

    public bool TienePermiso(string permiso) => Principal?.HasClaim(Politicas.ClaimPermisos, permiso) ?? false;

    public string? DireccionIp => accesor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? Correlacion => accesor.HttpContext?.Request.Headers[SWMatrc.Comun.Correlacion.Cabecera].FirstOrDefault();
}
