namespace SWMatrc.Domain.Riesgo;

/// <summary>Ejecuta el conjunto de reglas configuradas sobre el estado de una comunidad.</summary>
public interface IMotorRiesgo
{
    /// <summary>Devuelve solo los diagnósticos con riesgo, del más severo al menos severo.</summary>
    IReadOnlyList<DiagnosticoRiesgo> Evaluar(ContextoEvaluacion contexto);

    /// <summary>Nivel global de la comunidad: el más alto entre todos los diagnósticos.</summary>
    Enums.NivelAlerta NivelGlobal(IReadOnlyList<DiagnosticoRiesgo> diagnosticos);
}
