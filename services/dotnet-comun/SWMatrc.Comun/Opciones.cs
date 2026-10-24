namespace SWMatrc.Comun;

/// <summary>Cómo se validan los tokens del Auth Service. Sección <c>Autenticacion</c>.</summary>
public sealed class OpcionesAutenticacion
{
    public const string Seccion = "Autenticacion";

    /// <summary>Documento de descubrimiento (formato OpenID Connect) que apunta al JWKS.</summary>
    public string UrlMetadatos { get; init; } = "http://auth-service:3000/.well-known/openid-configuration";

    public string Emisor { get; init; } = "swmatrc-auth";

    /// <summary>Audiencia de los tokens de usuario (los que trae el frontend).</summary>
    public string Audiencia { get; init; } = "swmatrc-web";

    /// <summary>Audiencia de los tokens de servicio a servicio.</summary>
    public string AudienciaInterna { get; init; } = "swmatrc-internal";

    public bool RequiereHttps { get; init; }
}

/// <summary>Identidad de este microservicio frente a los demás. Sección <c>Servicio</c>.</summary>
public sealed class OpcionesServicio
{
    public const string Seccion = "Servicio";

    /// <summary>Nombre con el que aparece en la auditoría y con el que pide su token.</summary>
    public string Nombre { get; init; } = string.Empty;

    /// <summary>Secreto que Auth Service le asignó en <c>CLIENTES_SERVICIO</c>.</summary>
    public string Secreto { get; init; } = string.Empty;

    public string UrlAuth { get; init; } = "http://auth-service:3000";

    public string UrlAuditoria { get; init; } = "http://audit-service:8000";
}
