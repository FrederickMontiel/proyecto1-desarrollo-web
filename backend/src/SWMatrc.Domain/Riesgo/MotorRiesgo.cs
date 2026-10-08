using SWMatrc.Domain.Enums;

namespace SWMatrc.Domain.Riesgo;

/// <inheritdoc cref="IMotorRiesgo"/>
public sealed class MotorRiesgo(IEnumerable<IReglaRiesgo> reglas) : IMotorRiesgo
{
    private readonly IReadOnlyList<IReglaRiesgo> _reglas = reglas.OrderBy(r => r.Prioridad).ToList();

    public IReadOnlyList<DiagnosticoRiesgo> Evaluar(ContextoEvaluacion contexto)
    {
        var hallazgos = new List<DiagnosticoRiesgo>();

        foreach (var regla in _reglas)
        {
            var diagnostico = regla.Evaluar(contexto);
            if (!diagnostico.EsRiesgo)
                continue;

            // Toda alerta debe poder rastrearse hasta la regla que la originó.
            hallazgos.Add(diagnostico.ReglaNombre is null
                ? diagnostico with { ReglaNombre = $"Regla integrada: {regla.Fenomeno}" }
                : diagnostico);
        }

        return hallazgos
            .OrderByDescending(d => d.Nivel)
            .ThenBy(d => _reglas.First(r => r.Fenomeno == d.Fenomeno).Prioridad)
            .ToList();
    }

    public NivelAlerta NivelGlobal(IReadOnlyList<DiagnosticoRiesgo> diagnosticos) =>
        diagnosticos.Count == 0 ? NivelAlerta.Verde : diagnosticos.Max(d => d.Nivel);
}
