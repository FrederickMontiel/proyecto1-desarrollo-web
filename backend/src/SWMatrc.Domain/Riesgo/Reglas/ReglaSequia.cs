using SWMatrc.Domain.Enums;

namespace SWMatrc.Domain.Riesgo.Reglas;

/// <summary>
/// Déficit hídrico. Ninguna magnitud aislada define una sequía, así que se puntúa la
/// concurrencia de tres señales —lluvia escasa, aire seco y calor— y el total decide la
/// severidad. Es una aproximación al criterio multivariable que usan los índices de sequía.
/// </summary>
public sealed class ReglaSequia : IReglaRiesgo
{
    // Los umbrales se fijan por debajo de los valores de reposo de la red (1,5 mm de
    // lluvia y 1,8 m de cauce): de lo contrario una jornada tranquila ya sumaría dos
    // puntos y el sistema avisaría de una sequía inexistente.
    private const decimal LluviaEscasa = 0.5m;    // mm acumulados
    private const decimal HumedadBaja = 35m;      // % humedad relativa
    private const decimal TemperaturaAlta = 32m;  // °C
    private const decimal NivelRioBajoFactor = 0.15m; // fracción del rango útil del cauce

    public TipoFenomeno Fenomeno => TipoFenomeno.Sequia;

    public int Prioridad => 4;

    public DiagnosticoRiesgo Evaluar(ContextoEvaluacion contexto)
    {
        var lluvia = contexto.Valor(TipoSensor.Lluvia);
        var humedad = contexto.Valor(TipoSensor.Humedad);
        var temperatura = contexto.Valor(TipoSensor.Temperatura);

        // Sin lluvia ni humedad no hay evidencia suficiente para hablar de sequía.
        if (lluvia is null || humedad is null)
            return DiagnosticoRiesgo.Normal(Fenomeno);

        var puntos = 0;
        if (lluvia <= LluviaEscasa) puntos++;
        if (humedad <= HumedadBaja) puntos++;
        if (temperatura >= TemperaturaAlta) puntos++;

        // Un cauce en mínimos confirma que el déficit ya afecta la disponibilidad de agua.
        var rio = contexto.Obtener(TipoSensor.NivelRio);
        if (rio is not null)
        {
            var umbralCauceBajo = rio.ValorMinimo + (rio.ValorMaximo - rio.ValorMinimo) * NivelRioBajoFactor;
            if (rio.ValorActual <= umbralCauceBajo) puntos++;
        }

        var nivel = puntos switch
        {
            >= 4 => NivelAlerta.Rojo,
            3 => NivelAlerta.Naranja,
            2 => NivelAlerta.Amarillo,
            _ => NivelAlerta.Verde
        };

        if (nivel == NivelAlerta.Verde)
            return DiagnosticoRiesgo.Normal(Fenomeno);

        var mensaje = nivel switch
        {
            NivelAlerta.Rojo =>
                $"EMERGENCIA por sequía: precipitación de {lluvia:0.0} mm, humedad {humedad:0} % y cauce en mínimos. " +
                "Racionar el agua potable y habilitar el abastecimiento por cisterna.",
            NivelAlerta.Naranja =>
                $"Alerta por sequía: precipitación de {lluvia:0.0} mm con humedad de {humedad:0} %. " +
                "Restringir el riego y priorizar el consumo humano y del ganado.",
            _ =>
                $"Precaución por déficit hídrico: precipitación de {lluvia:0.0} mm y humedad de {humedad:0} %. " +
                "Iniciar el ahorro de agua y vigilar las reservas."
        };

        return new DiagnosticoRiesgo(Fenomeno, nivel, mensaje, contexto.Obtener(TipoSensor.Lluvia)?.Id, lluvia);
    }
}
