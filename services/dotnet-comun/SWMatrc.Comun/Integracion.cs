using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SWMatrc.Comun;

/// <summary>
/// Obtiene del Auth Service el token con el que este servicio llama a los demás y lo
/// reutiliza hasta un minuto antes de que venza.
/// </summary>
public sealed class ProveedorTokenServicio(IHttpClientFactory fabrica, IOptions<OpcionesServicio> opciones)
{
    public const string ClienteHttp = "token-servicio";

    private readonly SemaphoreSlim _candado = new(1, 1);
    private string? _token;
    private DateTimeOffset _expira;

    public async Task<string> ObtenerAsync(CancellationToken ct)
    {
        if (_token is not null && _expira - DateTimeOffset.UtcNow > TimeSpan.FromMinutes(1))
            return _token;

        await _candado.WaitAsync(ct);
        try
        {
            // Otra petición pudo renovarlo mientras esta esperaba el candado.
            if (_token is not null && _expira - DateTimeOffset.UtcNow > TimeSpan.FromMinutes(1))
                return _token;

            var servicio = opciones.Value;
            var respuesta = await fabrica.CreateClient(ClienteHttp).PostAsJsonAsync(
                $"{servicio.UrlAuth}/internal/token",
                new { clienteId = servicio.Nombre, secreto = servicio.Secreto }, ct);
            respuesta.EnsureSuccessStatusCode();

            var cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaToken>(ct)
                         ?? throw new InvalidOperationException("Auth Service devolvió una respuesta vacía.");
            (_token, _expira) = (cuerpo.Token, cuerpo.Expira);
            return _token;
        }
        finally
        {
            _candado.Release();
        }
    }

    private sealed record RespuestaToken(string Token, DateTimeOffset Expira);
}

/// <summary>
/// Agrega a cada llamada saliente el token de servicio y el id de correlación de la
/// petición en curso, para que la operación pueda seguirse a través de los servicios.
/// </summary>
public sealed class ManejadorTokenServicio(ProveedorTokenServicio tokens, IHttpContextAccessor accesor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
    {
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.ObtenerAsync(ct));

        var correlacion = accesor.HttpContext?.Request.Headers[Correlacion.Cabecera].FirstOrDefault();
        if (!string.IsNullOrEmpty(correlacion))
            peticion.Headers.TryAddWithoutValidation(Correlacion.Cabecera, correlacion);

        return await base.SendAsync(peticion, ct);
    }
}

/// <summary>Registra acciones en el Audit Service.</summary>
public interface IClienteAuditoria
{
    /// <summary>
    /// Encola el evento y vuelve de inmediato: la operación del usuario nunca espera a la
    /// bitácora ni falla si el Audit Service está caído.
    /// </summary>
    void Registrar(string accion, string entidad, object? entidadId = null, object? detalle = null);
}

/// <summary>
/// Implementación con una cola en memoria que vacía un servicio en segundo plano. Los datos
/// del autor (usuario, IP, correlación) se capturan al encolar, mientras la petición HTTP
/// sigue viva; el envío ocurre después.
/// </summary>
public sealed class ClienteAuditoria(UsuarioActual usuario, IOptions<OpcionesServicio> opciones, ColaAuditoria cola)
    : IClienteAuditoria
{
    public void Registrar(string accion, string entidad, object? entidadId = null, object? detalle = null)
    {
        cola.Encolar(new EventoAuditoria(
            opciones.Value.Nombre,
            usuario.Id,
            usuario.Email,
            accion,
            entidad,
            entidadId?.ToString(),
            detalle,
            usuario.DireccionIp,
            usuario.Correlacion,
            DateTimeOffset.UtcNow));
    }
}

public sealed record EventoAuditoria(
    string Servicio,
    int? UsuarioId,
    string UsuarioEmail,
    string Accion,
    string Entidad,
    string? EntidadId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Detalle,
    string? DireccionIp,
    string? Correlacion,
    DateTimeOffset FechaHora);

/// <summary>Cola acotada: si Audit lleva mucho tiempo caído se descartan los más viejos en lugar de agotar la memoria.</summary>
public sealed class ColaAuditoria
{
    private readonly Channel<EventoAuditoria> _canal = Channel.CreateBounded<EventoAuditoria>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public void Encolar(EventoAuditoria evento) => _canal.Writer.TryWrite(evento);

    internal ChannelReader<EventoAuditoria> Lector => _canal.Reader;

    internal void Completar() => _canal.Writer.TryComplete();
}

/// <summary>Envía en lotes los eventos encolados. Al apagarse, vacía lo pendiente antes de salir.</summary>
public sealed class EnviadorAuditoria(
    ColaAuditoria cola,
    IHttpClientFactory fabrica,
    IOptions<OpcionesServicio> opciones,
    ILogger<EnviadorAuditoria> logger) : BackgroundService
{
    public const string ClienteHttp = "auditoria";
    private const int TamanoLote = 200;

    protected override async Task ExecuteAsync(CancellationToken detener)
    {
        var lote = new List<EventoAuditoria>(TamanoLote);

        try
        {
            while (await cola.Lector.WaitToReadAsync(detener))
            {
                while (lote.Count < TamanoLote && cola.Lector.TryRead(out var evento))
                    lote.Add(evento);

                await EnviarAsync(lote, CancellationToken.None);
                lote.Clear();
            }
        }
        catch (OperationCanceledException)
        {
            // Apagado: lo que quede se envía en StopAsync.
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        await base.StopAsync(ct);
        cola.Completar();

        var pendientes = new List<EventoAuditoria>();
        while (cola.Lector.TryRead(out var evento))
            pendientes.Add(evento);

        if (pendientes.Count > 0)
            await EnviarAsync(pendientes, ct);
    }

    private async Task EnviarAsync(List<EventoAuditoria> eventos, CancellationToken ct)
    {
        if (eventos.Count == 0)
            return;

        try
        {
            var respuesta = await fabrica.CreateClient(ClienteHttp).PostAsJsonAsync(
                $"{opciones.Value.UrlAuditoria}/internal/events/batch", new { eventos }, Json.Opciones, ct);
            respuesta.EnsureSuccessStatusCode();
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning("No se pudieron registrar {Cantidad} eventos en auditoría: {Error}", eventos.Count, error.Message);
        }
    }
}

public static class ExtensionesIntegracion
{
    /// <summary>
    /// Registra el token de servicio, la auditoría y un helper para crear clientes HTTP
    /// hacia otros microservicios con el token, la correlación y la resiliencia ya puestos.
    /// </summary>
    public static IServiceCollection AgregarIntegracionSwmatrc(this IServiceCollection services)
    {
        services.AddOptions<OpcionesServicio>()
            .BindConfiguration(OpcionesServicio.Seccion)
            .Validate(o => !string.IsNullOrWhiteSpace(o.Nombre) && !string.IsNullOrWhiteSpace(o.Secreto),
                "Defina Servicio__Nombre y Servicio__Secreto.")
            .ValidateOnStart();

        // Singleton: el token cacheado se comparte entre todas las peticiones.
        services.AddHttpClient(ProveedorTokenServicio.ClienteHttp, c => c.Timeout = TimeSpan.FromSeconds(5));
        services.AddSingleton<ProveedorTokenServicio>();
        services.AddTransient<ManejadorTokenServicio>();

        services.AddSingleton<ColaAuditoria>();
        services.AddScoped<IClienteAuditoria, ClienteAuditoria>();
        services.AddHostedService<EnviadorAuditoria>();
        services.AgregarClienteInterno(EnviadorAuditoria.ClienteHttp);

        return services;
    }

    /// <summary>Cliente con nombre hacia otro microservicio, autenticado con el token de servicio.</summary>
    /// <param name="tiempoTotal">
    /// Tope de la llamada, reintento incluido. Por defecto 8 s; las consultas que solo
    /// complementan una respuesta (un conteo, por ejemplo) conviene acortarlas.
    /// </param>
    public static IHttpClientBuilder AgregarClienteInterno(
        this IServiceCollection services, string nombre, TimeSpan? tiempoTotal = null) =>
        ConResiliencia(services.AddHttpClient(nombre), tiempoTotal);

    /// <summary>Igual que <see cref="AgregarClienteInterno(IServiceCollection, string, TimeSpan?)"/>, para un cliente tipado.</summary>
    public static IHttpClientBuilder AgregarClienteInterno<TCliente>(
        this IServiceCollection services, TimeSpan? tiempoTotal = null) where TCliente : class =>
        ConResiliencia(services.AddHttpClient<TCliente>(), tiempoTotal);

    private static IHttpClientBuilder ConResiliencia(IHttpClientBuilder builder, TimeSpan? tiempoTotal)
    {
        var total = tiempoTotal ?? TimeSpan.FromSeconds(8);
        builder.AddHttpMessageHandler<ManejadorTokenServicio>();
        // Reintento corto, tiempo de espera y cortocircuito: si el otro servicio cae, las
        // llamadas fallan rápido en lugar de acumular hilos esperando.
        builder.AddStandardResilienceHandler(o =>
        {
            o.TotalRequestTimeout.Timeout = total;
            o.AttemptTimeout.Timeout = total / 2.5;
            o.Retry.MaxRetryAttempts = 1;
            // El cortocircuito evalúa en una ventana de al menos el doble del intento.
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(Math.Max(30, (total / 2.5).TotalSeconds * 2));
        });
        return builder;
    }
}
