using SWMatrc.Domain.Common;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Domain.Entities;

/// <summary>
/// Regla de alerta configurable desde el panel administrativo. Se dispara cuando la
/// lectura de un sensor del tipo indicado cae dentro del rango [mínimo, máximo]; un
/// extremo vacío deja el rango abierto por ese lado. Así se expresan tanto los umbrales
/// por exceso ("nivel del río ≥ 4.5 m") como por defecto ("temperatura ≤ 0 °C") o bandas
/// intermedias, sin tocar el código.
/// </summary>
public class ReglaAlerta : EntidadBase
{
    public string Nombre { get; set; } = string.Empty;

    public TipoSensor TipoSensor { get; set; }

    public decimal? ValorMinimo { get; set; }

    public decimal? ValorMaximo { get; set; }

    public NivelAlerta Nivel { get; set; }

    public TipoFenomeno Fenomeno { get; set; }

    /// <summary>
    /// Texto que verá el operador. Admite los marcadores {valor}, {unidad}, {sensor} y
    /// {comunidad}, que se sustituyen al generar la alerta.
    /// </summary>
    public string Mensaje { get; set; } = string.Empty;

    public bool Activa { get; set; } = true;

    /// <summary>Indica si una lectura incumple la regla, es decir, si cae dentro de su rango de disparo.</summary>
    public bool SeCumple(decimal valor) =>
        (ValorMinimo is not { } minimo || valor >= minimo) &&
        (ValorMaximo is not { } maximo || valor <= maximo);

    /// <summary>Umbral que se reporta en la alerta: el extremo del rango que la lectura cruzó.</summary>
    public decimal? UmbralReferencia => ValorMinimo ?? ValorMaximo;

    public string RedactarMensaje(Sensor sensor, decimal valor) =>
        Mensaje
            .Replace("{valor}", valor.ToString("0.##"))
            .Replace("{unidad}", sensor.UnidadMedida)
            .Replace("{sensor}", sensor.Codigo)
            .Replace("{comunidad}", sensor.Comunidad?.Nombre ?? string.Empty);
}
