using SWMatrc.Domain.Enums;

namespace SWMatrc.Domain.Riesgo.Reglas;

/// <summary>
/// Crecida de río o reservorio. El nivel del cauce manda; la lluvia actúa como agravante
/// porque el agua que ya está cayendo aún no se refleja en el nivel medido aguas abajo.
/// </summary>
public sealed class ReglaInundacion : IReglaRiesgo
{
    public TipoFenomeno Fenomeno => TipoFenomeno.Inundacion;

    public int Prioridad => 1;

    public DiagnosticoRiesgo Evaluar(ContextoEvaluacion contexto)
    {
        var rio = contexto.Obtener(TipoSensor.NivelRio);
        if (rio is null)
            return DiagnosticoRiesgo.Normal(Fenomeno);

        var nivel = rio.Clasificar(rio.ValorActual);
        if (nivel == NivelAlerta.Verde)
            return DiagnosticoRiesgo.Normal(Fenomeno);

        // Lluvia intensa simultánea escala un peldaño: el caudal seguirá subiendo.
        var lluvia = contexto.Obtener(TipoSensor.Lluvia);
        var agravado = false;
        if (lluvia is not null && lluvia.Clasificar(lluvia.ValorActual) >= NivelAlerta.Naranja)
        {
            agravado = nivel < NivelAlerta.Rojo;
            if (agravado) nivel++;
        }

        var mensaje = nivel switch
        {
            NivelAlerta.Rojo =>
                $"EMERGENCIA por inundación: el cauce alcanzó {rio.ValorActual:0.00} {rio.UnidadMedida}, " +
                "por encima del nivel de desbordamiento. Evacuar de inmediato las viviendas ribereñas.",
            NivelAlerta.Naranja =>
                $"Alerta de inundación: nivel del cauce en {rio.ValorActual:0.00} {rio.UnidadMedida}. " +
                "Preparar la evacuación de zonas bajas y despejar las rutas de salida.",
            _ =>
                $"Precaución por crecida: el cauce subió a {rio.ValorActual:0.00} {rio.UnidadMedida}. " +
                "Vigilar la evolución y avisar a las familias cercanas al río."
        };

        if (agravado)
            mensaje += " Nivel escalado por lluvia intensa en curso.";

        return new DiagnosticoRiesgo(Fenomeno, nivel, mensaje, rio.Id, rio.ValorActual);
    }
}
