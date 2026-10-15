using Microsoft.Extensions.Options;
using SWMatrc.Sensores.Aplicacion;

namespace SWMatrc.Sensores.Simulacion;

/// <summary>Reloj del monitoreo: ejecuta un ciclo de adquisición por intervalo.</summary>
public sealed class ServicioSimulacion(
    IServiceScopeFactory ambitos,
    IOptions<OpcionesSimulacion> opciones,
    ILogger<ServicioSimulacion> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken detener)
    {
        if (!opciones.Value.Habilitada)
        {
            logger.LogWarning("Simulación deshabilitada por configuración: no se generarán lecturas.");
            return;
        }

        var intervalo = TimeSpan.FromSeconds(Math.Clamp(opciones.Value.IntervaloSegundos, 1, 60));
        logger.LogInformation("Simulación en marcha: un ciclo cada {Segundos} s.", intervalo.TotalSeconds);

        // El temporizador periódico no acumula retrasos: si un ciclo tarda de más, el
        // siguiente sale a su hora en lugar de encadenarse.
        using var temporizador = new PeriodicTimer(intervalo);

        try
        {
            while (await temporizador.WaitForNextTickAsync(detener))
            {
                try
                {
                    // Un ámbito por ciclo: el DbContext no puede compartirse entre iteraciones.
                    using var ambito = ambitos.CreateScope();
                    await ambito.ServiceProvider.GetRequiredService<ServicioMonitoreo>().EjecutarCicloAsync(detener);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    // Un fallo puntual (la base reiniciándose, por ejemplo) no detiene el
                    // monitoreo: se registra y el siguiente ciclo vuelve a intentarlo.
                    logger.LogError(error, "Fallo en el ciclo de simulación; se reintentará en el siguiente intervalo.");
                }
            }
        }
        catch (OperationCanceledException) when (detener.IsCancellationRequested)
        {
            // Apagado ordenado: el ciclo en curso terminó y no se lanza otro.
        }

        logger.LogInformation("Simulación detenida.");
    }
}
