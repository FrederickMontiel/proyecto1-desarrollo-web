using SWMatrc.Domain.Enums;

namespace SWMatrc.Domain.Riesgo.Reglas;

/// <summary>
/// Peligro de incendio forestal. Se calcula un índice 0–100 inspirado en los sistemas de
/// peligro meteorológico de incendios (tipo FWI): el calor y el aire seco preparan el
/// combustible, el viento propaga el fuego, y la lluvia reciente lo suprime.
/// </summary>
public sealed class ReglaIncendioForestal : IReglaRiesgo
{
    // Rangos de normalización de cada variable a la escala 0–1.
    private const decimal TempBase = 20m, TempTope = 45m;
    private const decimal VientoTope = 60m;

    // Peso de cada factor en el índice. Suman 1.
    private const decimal PesoTemperatura = 0.35m;
    private const decimal PesoSequedad = 0.40m;
    private const decimal PesoViento = 0.25m;

    /// <summary>Precipitación a partir de la cual el combustible fino deja de estar disponible.</summary>
    private const decimal LluviaSupresora = 5m;

    public TipoFenomeno Fenomeno => TipoFenomeno.IncendioForestal;

    public int Prioridad => 5;

    public DiagnosticoRiesgo Evaluar(ContextoEvaluacion contexto)
    {
        var temperatura = contexto.Valor(TipoSensor.Temperatura);
        var humedad = contexto.Valor(TipoSensor.Humedad);
        var viento = contexto.Valor(TipoSensor.Viento) ?? 0m;
        var lluvia = contexto.Valor(TipoSensor.Lluvia) ?? 0m;

        if (temperatura is null || humedad is null)
            return DiagnosticoRiesgo.Normal(Fenomeno);

        // Lluvia significativa: el combustible está mojado, no hay peligro que reportar.
        if (lluvia >= LluviaSupresora)
            return DiagnosticoRiesgo.Normal(Fenomeno);

        var fTemp = Normalizar(temperatura.Value, TempBase, TempTope);
        var fSequedad = Normalizar(100m - humedad.Value, 0m, 100m);
        var fViento = Normalizar(viento, 0m, VientoTope);

        var indice = (fTemp * PesoTemperatura + fSequedad * PesoSequedad + fViento * PesoViento) * 100m;

        var nivel = indice switch
        {
            >= 80m => NivelAlerta.Rojo,
            >= 65m => NivelAlerta.Naranja,
            >= 50m => NivelAlerta.Amarillo,
            _ => NivelAlerta.Verde
        };

        if (nivel == NivelAlerta.Verde)
            return DiagnosticoRiesgo.Normal(Fenomeno);

        var condiciones = $"{temperatura:0.0} °C, humedad {humedad:0} % y viento {viento:0.0} km/h";

        var mensaje = nivel switch
        {
            NivelAlerta.Rojo =>
                $"EMERGENCIA por incendio forestal: índice de peligro {indice:0} sobre 100 ({condiciones}). " +
                "Prohibir toda quema, activar brigadas y preparar la evacuación de caseríos cercanos al bosque.",
            NivelAlerta.Naranja =>
                $"Alerta de incendio forestal: índice de peligro {indice:0} sobre 100 ({condiciones}). " +
                "Suspender quemas agrícolas y mantener las brigadas en disponibilidad.",
            _ =>
                $"Precaución por riesgo de incendio: índice de peligro {indice:0} sobre 100 ({condiciones}). " +
                "Evitar fuegos abiertos y vigilar los linderos con vegetación seca."
        };

        return new DiagnosticoRiesgo(Fenomeno, nivel, mensaje, contexto.Obtener(TipoSensor.Temperatura)?.Id, Math.Round(indice, 1));
    }

    /// <summary>Lleva un valor a la escala 0–1 recortándolo contra sus extremos.</summary>
    private static decimal Normalizar(decimal valor, decimal minimo, decimal maximo)
    {
        if (maximo <= minimo) return 0m;
        var escalado = (valor - minimo) / (maximo - minimo);
        return Math.Clamp(escalado, 0m, 1m);
    }
}
