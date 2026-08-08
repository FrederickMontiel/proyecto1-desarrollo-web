using SWMatrc.Domain.Enums;

namespace SWMatrc.Domain.Riesgo.Reglas;

/// <summary>
/// Tormenta o vendaval. Se dispara por viento fuerte, y se agrava cuando además llueve:
/// la combinación de racha y precipitación es la que derriba techos y tendido eléctrico.
/// </summary>
public sealed class ReglaTormenta : IReglaRiesgo
{
    public TipoFenomeno Fenomeno => TipoFenomeno.Tormenta;

    public int Prioridad => 2;

    public DiagnosticoRiesgo Evaluar(ContextoEvaluacion contexto)
    {
        var viento = contexto.Obtener(TipoSensor.Viento);
        var lluvia = contexto.Obtener(TipoSensor.Lluvia);
        if (viento is null)
            return DiagnosticoRiesgo.Normal(Fenomeno);

        var nivelViento = viento.Clasificar(viento.ValorActual);
        var nivelLluvia = lluvia is null ? NivelAlerta.Verde : lluvia.Clasificar(lluvia.ValorActual);

        // Ni el viento solo ni la lluvia sola bastan si ambos están por debajo de precaución.
        if (nivelViento == NivelAlerta.Verde && nivelLluvia == NivelAlerta.Verde)
            return DiagnosticoRiesgo.Normal(Fenomeno);

        var nivel = (NivelAlerta)Math.Max((int)nivelViento, (int)nivelLluvia);

        // Viento y lluvia simultáneos por encima de precaución escalan un peldaño.
        if (nivelViento >= NivelAlerta.Amarillo && nivelLluvia >= NivelAlerta.Amarillo && nivel < NivelAlerta.Rojo)
            nivel++;

        var detalleLluvia = lluvia is null ? "" : $" y precipitación de {lluvia.ValorActual:0.0} {lluvia.UnidadMedida}";

        var mensaje = nivel switch
        {
            NivelAlerta.Rojo =>
                $"EMERGENCIA por tormenta: rachas de {viento.ValorActual:0.0} {viento.UnidadMedida}{detalleLluvia}. " +
                "Suspender toda actividad al aire libre y resguardarse en estructuras seguras.",
            NivelAlerta.Naranja =>
                $"Alerta de tormenta: viento de {viento.ValorActual:0.0} {viento.UnidadMedida}{detalleLluvia}. " +
                "Asegurar techos, cortar energía en zonas expuestas y proteger al ganado.",
            _ =>
                $"Precaución por tormenta: viento de {viento.ValorActual:0.0} {viento.UnidadMedida}{detalleLluvia}. " +
                "Evitar desplazamientos innecesarios y retirar objetos sueltos."
        };

        return new DiagnosticoRiesgo(Fenomeno, nivel, mensaje, viento.Id, viento.ValorActual);
    }
}
