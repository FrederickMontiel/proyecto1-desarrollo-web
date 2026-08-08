namespace SWMatrc.Application.Comun;

/// <summary>
/// Parámetros del ciclo de monitoreo. Se enlazan a la misma sección de configuración que
/// usa el servicio en segundo plano, de modo que el periodo del ciclo y la tolerancia ante
/// el silencio de un sensor no puedan quedar desincronizados.
/// </summary>
public sealed class OpcionesMonitoreo
{
    public const string Seccion = "Simulacion";

    /// <summary>Periodo entre ciclos de adquisición, en segundos.</summary>
    public int IntervaloSegundos { get; set; } = 3;

    /// <summary>
    /// Ciclos consecutivos que un sensor puede dejar de reportar antes de declararlo sin
    /// señal. Se admite más de uno para que un retraso puntual de la red no lo marque como
    /// averiado, pero pocos: el objetivo es enterarse pronto.
    /// </summary>
    public int CiclosToleranciaSinSenal { get; set; } = 3;

    /// <summary>Tiempo máximo que puede pasar sin noticias de un sensor antes de marcarlo.</summary>
    public TimeSpan ToleranciaSinSenal =>
        TimeSpan.FromSeconds(Math.Max(1, IntervaloSegundos) * Math.Max(1, CiclosToleranciaSinSenal));
}
