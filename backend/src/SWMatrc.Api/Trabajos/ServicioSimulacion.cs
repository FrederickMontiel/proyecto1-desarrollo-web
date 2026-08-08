using SWMatrc.Application.Abstracciones;

namespace SWMatrc.Api.Trabajos;

/// <summary>Ajustes del ciclo de adquisición de datos.</summary>
public sealed class OpcionesSimulacion
{
    public const string Seccion = "Simulacion";

    /// <summary>Periodo entre ciclos de medición, en segundos.</summary>
    public int IntervaloSegundos { get; set; } = 3;

    /// <summary>Permite arrancar la API con la simulación detenida, p. ej. en pruebas.</summary>
    public bool Habilitada { get; set; } = true;
}

/// <summary>
/// Reloj del sistema de monitoreo. Ejecuta un ciclo de adquisición por intervalo y deja
/// que el servicio de monitoreo haga el resto: persistir, evaluar riesgo y difundir.
/// </summary>
public sealed class ServicioSimulacion(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ServicioSimulacion> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opciones = configuration.GetSection(OpcionesSimulacion.Seccion).Get<OpcionesSimulacion>()
                       ?? new OpcionesSimulacion();

        if (!opciones.Habilitada)
        {
            logger.LogWarning("Simulación deshabilitada por configuración: no se generarán lecturas.");
            return;
        }

        var intervalo = TimeSpan.FromSeconds(Math.Clamp(opciones.IntervaloSegundos, 1, 60));
        logger.LogInformation("Simulación en marcha: un ciclo cada {Segundos} s", intervalo.TotalSeconds);

        // El temporizador periódico no acumula retrasos: si un ciclo tarda de más,
        // el siguiente sale a su hora en lugar de encadenarse.
        using var temporizador = new PeriodicTimer(intervalo);

        while (await temporizador.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                // Cada ciclo abre su propio ámbito: el DbContext es scoped y no puede
                // compartirse entre iteraciones de un servicio singleton.
                using var ambito = scopeFactory.CreateScope();
                var monitoreo = ambito.ServiceProvider.GetRequiredService<IServicioMonitoreo>();

                await monitoreo.EjecutarCicloAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un fallo puntual —la base reiniciándose, por ejemplo— no debe detener
                // el monitoreo: se registra y el siguiente ciclo vuelve a intentarlo.
                logger.LogError(ex, "Fallo en el ciclo de simulación; se reintentará en el siguiente intervalo.");
            }
        }

        logger.LogInformation("Simulación detenida.");
    }
}
