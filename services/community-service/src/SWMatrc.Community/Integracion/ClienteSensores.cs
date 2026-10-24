using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using SWMatrc.Comun;
using SWMatrc.Community.Aplicacion;

namespace SWMatrc.Community.Integracion;

/// <summary>Cuántos sensores tiene cada comunidad. Nulo si no se pudo saber.</summary>
public interface IFuenteConteoSensores
{
    Task<IReadOnlyDictionary<int, ConteoSensores>?> ObtenerAsync(CancellationToken ct);
}

/// <summary>
/// Pregunta al Sensor Service por su API interna, nunca a su base de datos. La respuesta se
/// guarda 15 segundos: el listado de comunidades se pide seguido y los sensores cambian poco.
/// Si Sensor no responde, el listado sale igual, con los conteos marcados como no disponibles.
/// </summary>
public sealed class ClienteSensores(
    HttpClient http,
    IMemoryCache cache,
    IConfiguration configuracion,
    ILogger<ClienteSensores> logger) : IFuenteConteoSensores
{
    private const string ClaveCache = "conteo-sensores";
    private readonly string _url = $"{configuracion["Servicios:UrlSensor"] ?? "http://sensor-service:8080"}/internal/sensors/count-by-community";

    public async Task<IReadOnlyDictionary<int, ConteoSensores>?> ObtenerAsync(CancellationToken ct)
    {
        // También se guarda el fallo (como nulo): mientras Sensor esté caído, cada listado no
        // tiene que esperar de nuevo a que venza el tiempo de espera.
        if (cache.TryGetValue(ClaveCache, out IReadOnlyDictionary<int, ConteoSensores>? guardado))
            return guardado;

        IReadOnlyDictionary<int, ConteoSensores>? resultado;
        try
        {
            var conteos = await http.GetFromJsonAsync<List<ConteoSensores>>(_url, Json.Opciones, ct) ?? [];
            resultado = conteos.ToDictionary(c => c.ComunidadId);
        }
        catch (Exception error) when (error is HttpRequestException or System.Text.Json.JsonException
                                          // Tiempo agotado o circuito abierto en la capa de resiliencia.
                                          or Polly.ExecutionRejectedException
                                          || (error is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning("Sensor Service no respondió el conteo de sensores: {Error}", error.Message);
            resultado = null;
        }

        cache.Set(ClaveCache, resultado, TimeSpan.FromSeconds(15));
        return resultado;
    }
}
