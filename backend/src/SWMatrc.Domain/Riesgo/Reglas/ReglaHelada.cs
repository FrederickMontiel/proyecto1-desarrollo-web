using SWMatrc.Domain.Enums;

namespace SWMatrc.Domain.Riesgo.Reglas;

/// <summary>
/// Helada. Depende del extremo inferior de la temperatura, por eso se apoya en los
/// umbrales "bajos" del sensor. La humedad alta adelanta la formación de escarcha sobre
/// el cultivo, así que agrava el diagnóstico.
/// </summary>
public sealed class ReglaHelada : IReglaRiesgo
{
    /// <summary>Por encima de esta humedad relativa la condensación acelera el daño por congelación.</summary>
    private const decimal HumedadCriticaEscarcha = 85m;

    public TipoFenomeno Fenomeno => TipoFenomeno.Helada;

    public int Prioridad => 3;

    public DiagnosticoRiesgo Evaluar(ContextoEvaluacion contexto)
    {
        var temperatura = contexto.Obtener(TipoSensor.Temperatura);
        if (temperatura is null)
            return DiagnosticoRiesgo.Normal(Fenomeno);

        // Solo interesa el descenso: se ignoran los umbrales por exceso del sensor.
        var valor = temperatura.ValorActual;
        var nivel = NivelAlerta.Verde;
        if (temperatura.UmbralRojoBajo is { } rb && valor <= rb) nivel = NivelAlerta.Rojo;
        else if (temperatura.UmbralNaranjaBajo is { } nb && valor <= nb) nivel = NivelAlerta.Naranja;
        else if (temperatura.UmbralAmarilloBajo is { } ab && valor <= ab) nivel = NivelAlerta.Amarillo;

        if (nivel == NivelAlerta.Verde)
            return DiagnosticoRiesgo.Normal(Fenomeno);

        var humedad = contexto.Valor(TipoSensor.Humedad);
        var escarcha = humedad >= HumedadCriticaEscarcha;
        if (escarcha && nivel < NivelAlerta.Rojo)
            nivel++;

        var mensaje = nivel switch
        {
            NivelAlerta.Rojo =>
                $"EMERGENCIA por helada: {valor:0.0} {temperatura.UnidadMedida}. Pérdida total de cultivos sensibles " +
                "en pocas horas. Proteger animales y activar los refugios comunitarios.",
            NivelAlerta.Naranja =>
                $"Alerta de helada: {valor:0.0} {temperatura.UnidadMedida}. Cubrir cultivos, encender fuentes de calor " +
                "y resguardar el ganado joven.",
            _ =>
                $"Precaución por descenso térmico: {valor:0.0} {temperatura.UnidadMedida}. " +
                "Preparar coberturas para los cultivos más sensibles."
        };

        if (escarcha)
            mensaje += " La humedad elevada favorece la formación de escarcha.";

        return new DiagnosticoRiesgo(Fenomeno, nivel, mensaje, temperatura.Id, valor);
    }
}
