using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using SWMatrc.Comun;

namespace SWMatrc.Sensores.Integracion;

/// <summary>Comunidades conocidas por Community Service.</summary>
public interface IFuenteComunidades
{
    /// <summary>
    /// Todas las comunidades, por id. Si Community no responde se devuelve la última lista
    /// conocida, para que la simulación no se detenga por una caída breve; nulo solo si
    /// nunca se ha logrado obtenerla.
    /// </summary>
    Task<IReadOnlyDictionary<int, ComunidadInfo>?> ListarAsync(CancellationToken ct, bool forzar = false);
}

public sealed class ClienteComunidades(
    HttpClient http,
    IMemoryCache cache,
    IConfiguration configuracion,
    ILogger<ClienteComunidades> logger) : IFuenteComunidades
{
    private const string ClaveVigente = "comunidades:vigente";
    private const string ClaveUltimaConocida = "comunidades:ultima";
    private static readonly TimeSpan Vigencia = TimeSpan.FromSeconds(10);

    private readonly string _url = $"{configuracion["Servicios:UrlCommunity"] ?? "http://community-service:8080"}/internal/communities";

    public async Task<IReadOnlyDictionary<int, ComunidadInfo>?> ListarAsync(CancellationToken ct, bool forzar = false)
    {
        if (!forzar && cache.TryGetValue(ClaveVigente, out IReadOnlyDictionary<int, ComunidadInfo>? vigente))
            return vigente;

        try
        {
            var lista = await http.GetFromJsonAsync<List<ComunidadInfo>>(_url, Json.Opciones, ct) ?? [];
            IReadOnlyDictionary<int, ComunidadInfo> porId = lista.ToDictionary(c => c.Id);
            cache.Set(ClaveVigente, porId, Vigencia);
            cache.Set(ClaveUltimaConocida, porId);
            return porId;
        }
        catch (Exception error) when (EsFalloDeRed(error, ct))
        {
            // Quien fuerza la consulta necesita el dato actual (validar una comunidad que no
            // está en la caché): una lista vieja le haría responder "no existe" cuando en
            // realidad no se pudo verificar.
            if (forzar)
                return null;

            logger.LogWarning("Community Service no respondió; se usa la última lista conocida. {Error}", error.Message);
            var ultima = cache.Get<IReadOnlyDictionary<int, ComunidadInfo>>(ClaveUltimaConocida);
            // Se reintenta en el siguiente ciclo en lugar de en cada petición mientras siga caído.
            if (ultima is not null)
                cache.Set(ClaveVigente, ultima, Vigencia);
            return ultima;
        }
    }

    internal static bool EsFalloDeRed(Exception error, CancellationToken ct) =>
        error is HttpRequestException or System.Text.Json.JsonException or Polly.ExecutionRejectedException
        || (error is TaskCanceledException && !ct.IsCancellationRequested);
}

/// <summary>Evaluación de riesgo y consulta de alertas en Alert Service.</summary>
public interface IClienteAlertas
{
    /// <summary>Nulo si Alert no respondió: las lecturas se guardan igual, sin nivel.</summary>
    Task<RespuestaEvaluacion?> EvaluarAsync(SolicitudEvaluacion solicitud, CancellationToken ct);

    /// <summary>Nulo si Alert no respondió.</summary>
    Task<AlertasActivas?> ActivasAsync(int comunidadId, CancellationToken ct);

    /// <summary>Cierra todas las alertas abiertas (reinicio del sistema). Falso si Alert no respondió.</summary>
    Task<bool> ReiniciarAsync(CancellationToken ct);
}

/// <remarks>
/// Si Alert falla, la evaluación se pausa 15 segundos en lugar de esperar el tiempo de
/// espera completo en cada ciclo: con un ciclo cada 3 s, unos pocos fallos seguidos no
/// alcanzan para abrir el cortocircuito de Polly y la adquisición se ralentizaría.
/// </remarks>
public sealed class ClienteAlertas(
    HttpClient http,
    IMemoryCache cache,
    IConfiguration configuracion,
    ILogger<ClienteAlertas> logger) : IClienteAlertas
{
    private const string ClavePausa = "alertas:pausa";
    private static readonly TimeSpan Pausa = TimeSpan.FromSeconds(15);

    private readonly string _base = configuracion["Servicios:UrlAlert"] ?? "http://alert-service:8080";

    public async Task<RespuestaEvaluacion?> EvaluarAsync(SolicitudEvaluacion solicitud, CancellationToken ct)
    {
        if (solicitud.Comunidades.Count == 0)
            return new RespuestaEvaluacion([], []);
        if (cache.TryGetValue(ClavePausa, out _))
            return null;

        try
        {
            var respuesta = await http.PostAsJsonAsync($"{_base}/internal/evaluate", solicitud, Json.Opciones, ct);
            respuesta.EnsureSuccessStatusCode();
            return await respuesta.Content.ReadFromJsonAsync<RespuestaEvaluacion>(Json.Opciones, ct);
        }
        catch (Exception error) when (ClienteComunidades.EsFalloDeRed(error, ct))
        {
            logger.LogWarning("Alert Service no evaluó {Cantidad} comunidades; se reintenta en {Segundos} s. {Error}",
                solicitud.Comunidades.Count, Pausa.TotalSeconds, error.Message);
            cache.Set(ClavePausa, true, Pausa);
            return null;
        }
    }

    public async Task<AlertasActivas?> ActivasAsync(int comunidadId, CancellationToken ct)
    {
        if (cache.TryGetValue(ClavePausa, out _))
            return null;

        try
        {
            return await http.GetFromJsonAsync<AlertasActivas>(
                $"{_base}/internal/alerts/active?comunidadId={comunidadId}", Json.Opciones, ct);
        }
        catch (Exception error) when (ClienteComunidades.EsFalloDeRed(error, ct))
        {
            logger.LogWarning("Alert Service no devolvió las alertas de la comunidad {Comunidad}: {Error}", comunidadId, error.Message);
            cache.Set(ClavePausa, true, Pausa);
            return null;
        }
    }

    public async Task<bool> ReiniciarAsync(CancellationToken ct)
    {
        try
        {
            var respuesta = await http.PostAsync($"{_base}/internal/reset", null, ct);
            respuesta.EnsureSuccessStatusCode();
            return true;
        }
        catch (Exception error) when (ClienteComunidades.EsFalloDeRed(error, ct))
        {
            logger.LogWarning("Alert Service no atendió el reinicio: {Error}", error.Message);
            return false;
        }
    }
}
