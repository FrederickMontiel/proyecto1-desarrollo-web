using System.Collections.Concurrent;
using SWMatrc.Sensores.Dominio;

namespace SWMatrc.Sensores.Simulacion;

/// <summary>
/// Fuente de mediciones. Sustituye a la telemetría real: en una instalación de campo la
/// implementaría el adaptador del datalogger y nada más cambiaría.
/// </summary>
public interface ISimuladorClima
{
    /// <returns>
    /// El valor medido, o <c>null</c> si el instrumento no reporta en este ciclo (avería,
    /// batería agotada, pérdida del enlace). El sistema debe distinguir ese silencio.
    /// </returns>
    decimal? SiguienteValor(Sensor sensor);

    /// <summary>Descarta los episodios y averías en curso.</summary>
    void Reiniciar();
}

/// <summary>
/// Generador de telemetría sintética, portado sin cambios de la Fase 2. Mantiene por
/// comunidad un <i>episodio climático</i> con duración: mientras dura, todos sus sensores
/// convergen de forma coherente hacia el escenario. Así la lluvia sube junto con el cauce
/// y la humedad, y la evaluación de riesgo recibe situaciones plausibles en vez de ruido.
/// </summary>
/// <remarks>
/// El estado vive en memoria. Con varias réplicas, solo una debe simular
/// (<c>Simulacion__Habilitada=true</c> en una sola); las demás atienden la API y el hub.
/// </remarks>
public sealed class SimuladorClima(ILogger<SimuladorClima> logger) : ISimuladorClima
{
    private const double ProbabilidadInicioEpisodio = 0.65;
    private const int DuracionMinimaCiclos = 25;
    private const int DuracionMaximaCiclos = 70;
    private const decimal VelocidadConvergencia = 0.12m;
    private const double ProbabilidadAveria = 0.004;
    private const int DuracionMinimaAveria = 6;
    private const int DuracionMaximaAveria = 20;

    private readonly ConcurrentDictionary<int, EpisodioActivo> _episodios = new();
    private readonly ConcurrentDictionary<int, int> _averias = new();

    public decimal? SiguienteValor(Sensor sensor)
    {
        if (EstaAveriado(sensor.Id))
            return null;

        var episodio = ObtenerEpisodio(sensor.ComunidadId);
        var objetivo = sensor.ValorMinimo + (sensor.ValorMaximo - sensor.ValorMinimo) * PerfilFraccion(sensor.Tipo, episodio.Tipo);

        // Deriva determinista hacia el objetivo más un ruido que evita curvas artificialmente lisas.
        var deriva = (objetivo - sensor.ValorActual) * VelocidadConvergencia;
        var ruido = (decimal)(Random.Shared.NextDouble() * 2 - 1) * sensor.VariacionMaxima;

        return Math.Round(Math.Clamp(sensor.ValorActual + deriva + ruido, sensor.ValorMinimo, sensor.ValorMaximo), 2);
    }

    public void Reiniciar()
    {
        _episodios.Clear();
        _averias.Clear();
        logger.LogInformation("Estado de la simulación climática reiniciado.");
    }

    private bool EstaAveriado(int sensorId)
    {
        if (_averias.TryGetValue(sensorId, out var restantes))
        {
            if (restantes > 1)
            {
                _averias[sensorId] = restantes - 1;
                return true;
            }

            _averias.TryRemove(sensorId, out _);
            logger.LogInformation("El sensor {SensorId} vuelve a reportar.", sensorId);
            return false;
        }

        if (Random.Shared.NextDouble() >= ProbabilidadAveria)
            return false;

        var duracion = Random.Shared.Next(DuracionMinimaAveria, DuracionMaximaAveria);
        _averias[sensorId] = duracion;
        logger.LogWarning("El sensor {SensorId} dejó de reportar durante {Ciclos} ciclos.", sensorId, duracion);
        return true;
    }

    private EpisodioActivo ObtenerEpisodio(int comunidadId)
    {
        var episodio = _episodios.GetValueOrDefault(comunidadId) ?? EpisodioActivo.Calma();
        var restantes = episodio.CiclosRestantes - 1;

        if (restantes > 0)
        {
            episodio = episodio with { CiclosRestantes = restantes };
            _episodios[comunidadId] = episodio;
            return episodio;
        }

        // Encadenar dos escenarios extremos seguidos daría un comportamiento poco creíble.
        var arrancaEpisodio = episodio.Tipo == TipoEpisodio.Calma && Random.Shared.NextDouble() < ProbabilidadInicioEpisodio;
        var nuevo = arrancaEpisodio ? EpisodioActivo.Aleatorio() : EpisodioActivo.Calma();

        if (nuevo.Tipo != TipoEpisodio.Calma)
            logger.LogInformation("Episodio simulado '{Episodio}' iniciado en la comunidad {ComunidadId} por {Ciclos} ciclos",
                nuevo.Tipo, comunidadId, nuevo.CiclosRestantes);

        _episodios[comunidadId] = nuevo;
        return nuevo;
    }

    /// <summary>Perfiles como fracción del rango del sensor: sirven igual para un termómetro que para un limnímetro.</summary>
    private static decimal PerfilFraccion(TipoSensor tipo, TipoEpisodio episodio) => episodio switch
    {
        TipoEpisodio.LluviaIntensa => tipo switch
        {
            TipoSensor.Lluvia => 0.45m, TipoSensor.NivelRio => 0.68m, TipoSensor.Humedad => 0.92m,
            TipoSensor.Viento => 0.30m, TipoSensor.Temperatura => 0.42m, TipoSensor.NivelReservorio => 0.90m,
            TipoSensor.Humo => 0.03m, _ => 0.5m
        },
        TipoEpisodio.OlaDeCalor => tipo switch
        {
            TipoSensor.Temperatura => 0.88m, TipoSensor.Humedad => 0.12m, TipoSensor.Lluvia => 0.0m,
            TipoSensor.Viento => 0.28m, TipoSensor.NivelRio => 0.18m, TipoSensor.NivelReservorio => 0.40m,
            TipoSensor.Humo => 0.20m, _ => 0.5m
        },
        TipoEpisodio.FrenteFrio => tipo switch
        {
            TipoSensor.Temperatura => 0.10m, TipoSensor.Humedad => 0.90m, TipoSensor.Viento => 0.35m,
            TipoSensor.Lluvia => 0.08m, TipoSensor.NivelRio => 0.25m, TipoSensor.NivelReservorio => 0.62m,
            TipoSensor.Humo => 0.02m, _ => 0.5m
        },
        TipoEpisodio.SequiaProlongada => tipo switch
        {
            TipoSensor.Lluvia => 0.0m, TipoSensor.Humedad => 0.10m, TipoSensor.Temperatura => 0.72m,
            TipoSensor.Viento => 0.22m, TipoSensor.NivelRio => 0.08m, TipoSensor.NivelReservorio => 0.15m,
            TipoSensor.Humo => 0.34m, _ => 0.5m
        },
        TipoEpisodio.Ventarron => tipo switch
        {
            TipoSensor.Viento => 0.65m, TipoSensor.Lluvia => 0.25m, TipoSensor.Humedad => 0.55m,
            TipoSensor.Temperatura => 0.40m, TipoSensor.NivelRio => 0.32m, TipoSensor.NivelReservorio => 0.60m,
            TipoSensor.Humo => 0.05m, _ => 0.5m
        },
        _ => tipo switch
        {
            TipoSensor.Temperatura => 0.55m, TipoSensor.Humedad => 0.62m, TipoSensor.Viento => 0.10m,
            TipoSensor.Lluvia => 0.02m, TipoSensor.NivelRio => 0.22m, TipoSensor.NivelReservorio => 0.60m,
            TipoSensor.Humo => 0.02m, _ => 0.5m
        }
    };

    private enum TipoEpisodio { Calma, LluviaIntensa, OlaDeCalor, FrenteFrio, SequiaProlongada, Ventarron }

    private sealed record EpisodioActivo(TipoEpisodio Tipo, int CiclosRestantes)
    {
        public static EpisodioActivo Calma() =>
            new(TipoEpisodio.Calma, Random.Shared.Next(DuracionMinimaCiclos, DuracionMaximaCiclos));

        public static EpisodioActivo Aleatorio()
        {
            TipoEpisodio[] escenarios =
            [
                TipoEpisodio.LluviaIntensa, TipoEpisodio.OlaDeCalor, TipoEpisodio.FrenteFrio,
                TipoEpisodio.SequiaProlongada, TipoEpisodio.Ventarron
            ];
            return new EpisodioActivo(escenarios[Random.Shared.Next(escenarios.Length)],
                Random.Shared.Next(DuracionMinimaCiclos, DuracionMaximaCiclos));
        }
    }
}
