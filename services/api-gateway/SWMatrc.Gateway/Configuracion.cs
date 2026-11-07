namespace SWMatrc.Gateway;

/// <summary>Nombres de las políticas que las rutas de YARP referencian desde la configuración.</summary>
public static class Politicas
{
    public const string Autenticado = "autenticado";

    /// <summary>Token válido con rol ADMIN: rutas administrativas (RNF-OAUTH-03).</summary>
    public const string Administrador = "administrador";
    public const string LimiteGeneral = "limite-general";
    public const string LimiteLogin = "limite-login";
    public const string CorsFrontend = "frontend";
}

/// <summary>Roles que emite el Auth Service en el claim <c>roles</c>.</summary>
public static class Roles
{
    public const string Claim = "roles";
    public const string Admin = "ADMIN";
    public static readonly string[] DeUsuario = ["ADMIN", "OPERATOR", "VIEWER"];
}

/// <summary>Cómo valida el gateway los tokens emitidos por el Auth Service.</summary>
public sealed class OpcionesAutenticacion
{
    public const string Seccion = "Autenticacion";

    /// <summary>Documento de descubrimiento (formato OpenID Connect) que apunta al JWKS.</summary>
    public string UrlMetadatos { get; init; } = string.Empty;

    public string Emisor { get; init; } = string.Empty;

    public string Audiencia { get; init; } = string.Empty;

    public bool RequiereHttps { get; init; }
}

/// <summary>Peticiones permitidas por IP y por minuto.</summary>
public sealed class OpcionesLimites
{
    public const string Seccion = "LimitePeticiones";

    public int GeneralPorMinuto { get; init; } = 600;

    public int LoginPorMinuto { get; init; } = 10;
}
