using SWMatrc.Domain.Enums;

namespace SWMatrc.Domain.Riesgo;

/// <summary>
/// Regla de decisión para un fenómeno concreto. Cada amenaza se implementa por separado,
/// de modo que incorporar una nueva (deslizamiento, granizada) es agregar una clase y
/// registrarla, sin modificar el motor ni las reglas existentes.
/// </summary>
public interface IReglaRiesgo
{
    TipoFenomeno Fenomeno { get; }

    /// <summary>Orden de presentación cuando varias reglas disparan al mismo nivel.</summary>
    int Prioridad { get; }

    DiagnosticoRiesgo Evaluar(ContextoEvaluacion contexto);
}
