using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Domain.Entities;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Infrastructure.Simulacion;

/// <summary>
/// Generador de telemetría sintética. En lugar de sacar números al azar en cada ciclo,
/// mantiene por comunidad un <i>episodio climático</i> con una duración: mientras dura,
/// todos sus sensores convergen de forma coherente hacia el escenario correspondiente.
/// Así la lluvia sube junto con el cauce y la humedad, y el motor de riesgo recibe
/// situaciones plausibles en vez de ruido.
/// </summary>
public sealed class SimuladorClimaEnMemoria(ILogger<SimuladorClimaEnMemoria> logger) : ISimuladorClima
{
    /// <summary>
    /// Probabilidad de que, al terminar un tramo de calma, arranque un episodio extremo
    /// en vez de otro tramo de calma. Se evalúa una sola vez por tramo, no en cada ciclo.
    /// </summary>
    private const double ProbabilidadInicioEpisodio = 0.65;

    private const int DuracionMinimaCiclos = 25;
    private const int DuracionMaximaCiclos = 70;

    /// <summary>Fracción del desvío hacia el objetivo que se recorre en cada ciclo.</summary>
    private const decimal VelocidadConvergencia = 0.12m;

    /// <summary>Probabilidad, por ciclo y por sensor, de que el instrumento sufra una avería.</summary>
    private const double ProbabilidadAveria = 0.004;

    private const int DuracionMinimaAveria = 6;
    private const int DuracionMaximaAveria = 20;

    private readonly ConcurrentDictionary<int, EpisodioActivo> _episodios = new();

    /// <summary>Ciclos que le quedan a cada sensor averiado antes de volver a reportar.</summary>
    private readonly ConcurrentDictionary<int, int> _averias = new();

    public decimal? SiguienteValor(Sensor sensor)
    {
        // Una estación de campo no siempre responde: se queda muda por avería, batería
        // agotada o pérdida del enlace. Simularlo es lo que permite ejercitar de verdad la
        // detección de sensores sin señal en lugar de dejarla como código que nunca corre.
        if (EstaAveriado(sensor.Id))
            return null;

        var episodio = ObtenerEpisodio(sensor.ComunidadId);
        var objetivo = CalcularObjetivo(sensor, episodio.Tipo);

        // Movimiento en dos partes: una deriva determinista hacia el objetivo del
        // escenario y un ruido aleatorio que evita curvas artificialmente lisas.
        var deriva = (objetivo - sensor.ValorActual) * VelocidadConvergencia;
        var ruido = (decimal)(Random.Shared.NextDouble() * 2 - 1) * sensor.VariacionMaxima;

        var valor = sensor.ValorActual + deriva + ruido;

        // Nunca se reporta fuera del rango físico declarado para el sensor.
        valor = Math.Clamp(valor, sensor.ValorMinimo, sensor.ValorMaximo);

        return Math.Round(valor, 2);
    }

    public void Reiniciar()
    {
        _episodios.Clear();
        _averias.Clear();
        logger.LogInformation("Estado de la simulación climática reiniciado.");
    }

    /// <summary>
    /// Avanza el reloj de la avería de un sensor y decide si empieza una nueva.
    /// </summary>
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

    /// <summary>Avanza el reloj del episodio de una comunidad y decide si empieza uno nuevo.</summary>
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

        // El episodio terminó. Se vuelve a la calma salvo que toque uno nuevo; encadenar
        // dos escenarios extremos seguidos daría un comportamiento poco creíble.
        var arrancaEpisodio = episodio.Tipo == TipoEpisodio.Calma
                              && Random.Shared.NextDouble() < ProbabilidadInicioEpisodio;

        var nuevo = arrancaEpisodio ? EpisodioActivo.Aleatorio() : EpisodioActivo.Calma();

        if (nuevo.Tipo != TipoEpisodio.Calma)
            logger.LogInformation("Episodio simulado '{Episodio}' iniciado en la comunidad {ComunidadId} por {Ciclos} ciclos",
                nuevo.Tipo, comunidadId, nuevo.CiclosRestantes);

        _episodios[comunidadId] = nuevo;
        return nuevo;
    }

    /// <summary>
    /// Traduce el escenario a un valor concreto para este sensor. Los perfiles se expresan
    /// como fracción del rango del sensor, de modo que sirven igual para un termómetro que
    /// para un limnímetro sin números mágicos por instrumento.
    /// </summary>
    private static decimal CalcularObjetivo(Sensor sensor, TipoEpisodio episodio)
    {
        var fraccion = PerfilFraccion(sensor.Tipo, episodio);
        return sensor.ValorMinimo + (sensor.ValorMaximo - sensor.ValorMinimo) * fraccion;
    }

    private static decimal PerfilFraccion(TipoSensor tipo, TipoEpisodio episodio) => episodio switch
    {
        TipoEpisodio.LluviaIntensa => tipo switch
        {
            TipoSensor.Lluvia => 0.45m,
            TipoSensor.NivelRio => 0.68m,
            TipoSensor.Humedad => 0.92m,
            TipoSensor.Viento => 0.30m,
            TipoSensor.Temperatura => 0.42m,
            _ => 0.5m
        },
        TipoEpisodio.OlaDeCalor => tipo switch
        {
            TipoSensor.Temperatura => 0.88m,
            TipoSensor.Humedad => 0.12m,
            TipoSensor.Lluvia => 0.0m,
            TipoSensor.Viento => 0.28m,
            TipoSensor.NivelRio => 0.18m,
            _ => 0.5m
        },
        TipoEpisodio.FrenteFrio => tipo switch
        {
            TipoSensor.Temperatura => 0.10m,
            TipoSensor.Humedad => 0.90m,
            TipoSensor.Viento => 0.35m,
            TipoSensor.Lluvia => 0.08m,
            TipoSensor.NivelRio => 0.25m,
            _ => 0.5m
        },
        TipoEpisodio.SequiaProlongada => tipo switch
        {
            TipoSensor.Lluvia => 0.0m,
            TipoSensor.Humedad => 0.10m,
            TipoSensor.Temperatura => 0.72m,
            TipoSensor.Viento => 0.22m,
            TipoSensor.NivelRio => 0.08m,
            _ => 0.5m
        },
        TipoEpisodio.Ventarron => tipo switch
        {
            TipoSensor.Viento => 0.65m,
            TipoSensor.Lluvia => 0.25m,
            TipoSensor.Humedad => 0.55m,
            TipoSensor.Temperatura => 0.40m,
            TipoSensor.NivelRio => 0.32m,
            _ => 0.5m
        },
        // Calma: cada magnitud gravita hacia su valor de reposo habitual.
        _ => tipo switch
        {
            TipoSensor.Temperatura => 0.55m,
            TipoSensor.Humedad => 0.62m,
            TipoSensor.Viento => 0.10m,
            TipoSensor.Lluvia => 0.02m,
            TipoSensor.NivelRio => 0.22m,
            _ => 0.5m
        }
    };

    private enum TipoEpisodio
    {
        Calma,
        LluviaIntensa,
        OlaDeCalor,
        FrenteFrio,
        SequiaProlongada,
        Ventarron
    }

    private sealed record EpisodioActivo(TipoEpisodio Tipo, int CiclosRestantes)
    {
        /// <summary>Tramo de normalidad entre episodios; también es el estado inicial.</summary>
        public static EpisodioActivo Calma() =>
            new(TipoEpisodio.Calma, Random.Shared.Next(DuracionMinimaCiclos, DuracionMaximaCiclos));

        public static EpisodioActivo Aleatorio()
        {
            TipoEpisodio[] escenarios =
            [
                TipoEpisodio.LluviaIntensa,
                TipoEpisodio.OlaDeCalor,
                TipoEpisodio.FrenteFrio,
                TipoEpisodio.SequiaProlongada,
                TipoEpisodio.Ventarron
            ];

            return new EpisodioActivo(
                escenarios[Random.Shared.Next(escenarios.Length)],
                Random.Shared.Next(DuracionMinimaCiclos, DuracionMaximaCiclos));
        }
    }
}
