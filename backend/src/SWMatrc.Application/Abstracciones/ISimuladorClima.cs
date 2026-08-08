using SWMatrc.Domain.Entities;

namespace SWMatrc.Application.Abstracciones;

/// <summary>
/// Fuente de datos meteorológicos simulados. Sustituye a la telemetría real: en una
/// instalación de campo esta interfaz la implementaría el adaptador del datalogger,
/// y ni el servicio de monitoreo ni el motor de riesgo cambiarían.
/// </summary>
public interface ISimuladorClima
{
    /// <summary>
    /// Calcula la siguiente medición de un sensor a partir de su estado actual.
    /// </summary>
    /// <returns>
    /// El valor medido, o <c>null</c> si el instrumento no reporta en este ciclo. Una
    /// estación real se queda muda por avería, batería agotada o pérdida del enlace, y el
    /// sistema tiene que poder distinguir ese silencio de una lectura normal.
    /// </returns>
    decimal? SiguienteValor(Sensor sensor);

    /// <summary>Descarta el estado interno de la simulación (episodios y tendencias en curso).</summary>
    void Reiniciar();
}
