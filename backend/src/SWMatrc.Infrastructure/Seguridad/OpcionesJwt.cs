namespace SWMatrc.Infrastructure.Seguridad;

/// <summary>Parámetros de firma y vigencia de los tokens de acceso.</summary>
public sealed class OpcionesJwt
{
    public const string Seccion = "Jwt";

    /// <summary>Clave simétrica de firma. Se inyecta por variable de entorno, nunca se versiona.</summary>
    public string Clave { get; set; } = string.Empty;

    public string Emisor { get; set; } = "swmatrc-api";

    public string Audiencia { get; set; } = "swmatrc-web";

    public int MinutosVigencia { get; set; } = 480;
}
