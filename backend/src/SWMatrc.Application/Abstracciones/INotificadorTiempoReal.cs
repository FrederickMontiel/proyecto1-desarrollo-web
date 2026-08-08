using SWMatrc.Application.Dtos;

namespace SWMatrc.Application.Abstracciones;

/// <summary>
/// Canal de difusión hacia los clientes conectados. La capa de aplicación publica
/// eventos de negocio y desconoce que el transporte concreto es un WebSocket.
/// </summary>
public interface INotificadorTiempoReal
{
    Task LecturaRecibidaAsync(LecturaDto lectura, CancellationToken ct = default);

    Task AlertaGeneradaAsync(AlertaDto alerta, CancellationToken ct = default);

    Task AlertaCerradaAsync(AlertaDto alerta, CancellationToken ct = default);

    Task EstadoSensorCambiadoAsync(SensorDto sensor, CancellationToken ct = default);

    /// <summary>Instantánea completa para que un cliente recién conectado pinte el dashboard.</summary>
    Task EstadoComunidadAsync(EstadoComunidadDto estado, CancellationToken ct = default);

    Task SistemaReiniciadoAsync(CancellationToken ct = default);
}
