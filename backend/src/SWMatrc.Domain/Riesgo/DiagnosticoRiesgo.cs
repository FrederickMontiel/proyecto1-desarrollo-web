using SWMatrc.Domain.Enums;

namespace SWMatrc.Domain.Riesgo;

/// <summary>
/// Veredicto de una regla de riesgo sobre el estado actual de una comunidad.
/// Es un objeto de valor: no toca la base de datos ni conoce la capa de aplicación.
/// </summary>
/// <param name="Fenomeno">Amenaza identificada.</param>
/// <param name="Nivel">Severidad en el código de colores del protocolo.</param>
/// <param name="Mensaje">Descripción del riesgo y recomendación para el operador.</param>
/// <param name="SensorId">Sensor disparador; nulo si la regla combina varias magnitudes.</param>
/// <param name="ValorDisparo">Medición que motivó el diagnóstico.</param>
/// <param name="ReglaId">Regla configurable que lo produjo; nulo para las reglas integradas.</param>
/// <param name="ReglaNombre">Nombre legible de la regla, configurable o integrada.</param>
/// <param name="Umbral">Umbral cruzado por la medición, si se conoce.</param>
public readonly record struct DiagnosticoRiesgo(
    TipoFenomeno Fenomeno,
    NivelAlerta Nivel,
    string Mensaje,
    int? SensorId,
    decimal? ValorDisparo,
    int? ReglaId = null,
    string? ReglaNombre = null,
    decimal? Umbral = null)
{
    /// <summary>Resultado neutro: la regla no encontró condiciones de riesgo.</summary>
    public static DiagnosticoRiesgo Normal(TipoFenomeno fenomeno) =>
        new(fenomeno, NivelAlerta.Verde, "Condiciones dentro de parámetros normales.", null, null);

    public bool EsRiesgo => Nivel > NivelAlerta.Verde;
}
