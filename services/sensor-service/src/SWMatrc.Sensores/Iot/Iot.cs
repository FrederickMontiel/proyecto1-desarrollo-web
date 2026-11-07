using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SWMatrc.Comun;
using SWMatrc.Sensores.Datos;
using SWMatrc.Sensores.Dominio;

namespace SWMatrc.Sensores.Iot;

/// <summary>
/// Dispositivos físicos (Raspberry Pi, ESP32…) autorizados a reportar lecturas. Sección
/// <c>Iot</c>. En Kubernetes llega del Secret <c>swmatrc-iot</c>:
/// <code>
/// Iot__Dispositivos__0__Id=raspberry-01
/// Iot__Dispositivos__0__Llave=&lt;secreto largo y aleatorio&gt;
/// Iot__Dispositivos__0__Sensores=RPI-TEMP-01,RPI-HUM-01,RPI-VIB-01
/// </code>
/// </summary>
public sealed class OpcionesIot
{
    public const string Seccion = "Iot";

    public List<DispositivoIot> Dispositivos { get; set; } = [];

    /// <summary>
    /// Silencio tolerado a un sensor físico antes de marcarlo "sin señal". Es mayor que el de
    /// los simulados: el DHT11 falla lecturas sueltas y la red de la comunidad no es perfecta.
    /// </summary>
    public int ToleranciaSinSenalSegundos { get; set; } = 30;
}

public sealed class DispositivoIot
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Llave que el dispositivo manda en la cabecera <c>X-Api-Key</c>.</summary>
    public string Llave { get; set; } = string.Empty;

    /// <summary>Códigos de sensor que este dispositivo puede reportar, separados por coma.</summary>
    public string Sensores { get; set; } = string.Empty;

    public IReadOnlySet<string> Codigos => _codigos ??= Sensores
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private HashSet<string>? _codigos;
}

/// <summary>
/// Qué sensores son físicos y qué dispositivo los reporta. Lo usan tanto las réplicas de la
/// API (que reciben las lecturas) como el pod del simulador (que deja de inventar valores
/// para esos sensores): ambos leen la misma configuración.
/// </summary>
public sealed class RegistroIot
{
    private readonly List<(DispositivoIot Dispositivo, byte[] Huella)> _dispositivos;
    private readonly HashSet<string> _fisicos;

    public TimeSpan ToleranciaSinSenal { get; }

    public RegistroIot(IOptions<OpcionesIot> opciones)
    {
        var valor = opciones.Value;
        _dispositivos = valor.Dispositivos
            .Where(d => !string.IsNullOrWhiteSpace(d.Id) && d.Llave.Length >= 16)
            .Select(d => (d, Huella(d.Llave)))
            .ToList();
        // Qué sensores son físicos no depende de la llave: el pod del simulador solo necesita
        // saber a cuáles no inventarles valores, y no recibe el secreto.
        _fisicos = valor.Dispositivos
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .SelectMany(d => d.Codigos)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        ToleranciaSinSenal = TimeSpan.FromSeconds(Math.Max(5, valor.ToleranciaSinSenalSegundos));
    }

    /// <summary>El sensor lo reporta un dispositivo real: el simulador no le inventa valores.</summary>
    public bool EsFisico(string codigo) => _fisicos.Contains(codigo);

    public int CantidadDispositivos => _dispositivos.Count;

    /// <summary>
    /// Busca el dispositivo dueño de la llave. Compara resúmenes de longitud fija en tiempo
    /// constante y recorre siempre todos: el tiempo de respuesta no revela nada de la llave.
    /// </summary>
    public DispositivoIot? Autenticar(string? llave)
    {
        if (string.IsNullOrEmpty(llave))
            return null;

        var huella = Huella(llave);
        DispositivoIot? encontrado = null;
        foreach (var (dispositivo, esperada) in _dispositivos)
            if (CryptographicOperations.FixedTimeEquals(huella, esperada))
                encontrado = dispositivo;
        return encontrado;
    }

    private static byte[] Huella(string texto) => SHA256.HashData(Encoding.UTF8.GetBytes(texto));
}

// --- Contrato HTTP ------------------------------------------------------------------------

/// <summary>Lote de mediciones de un dispositivo: <c>{ "lecturas": [ { "codigo", "valor" } ] }</c>.</summary>
public sealed record LoteIotRequest(IReadOnlyList<LecturaIotRequest>? Lecturas);

public sealed record LecturaIotRequest(string? Codigo, decimal? Valor);

public sealed record LecturaRechazada(string? Codigo, string Motivo);

public sealed record ResultadoIot(string Dispositivo, int Aceptadas, IReadOnlyList<LecturaRechazada> Rechazadas);

/// <summary>
/// Recepción de lecturas reales. El valor queda en el sensor como "recibido" y el ciclo de
/// adquisición lo consume en su siguiente vuelta: la lectura recorre el mismo camino que una
/// simulada (evaluación en Alert, nivel, almacenamiento y difusión por SignalR).
/// </summary>
public sealed class ServicioIot(SensoresDb db, ILogger<ServicioIot> logger)
{
    public const int MaximoPorLote = 50;

    public async Task<ResultadoIot> RecibirAsync(DispositivoIot dispositivo, LoteIotRequest lote, CancellationToken ct = default)
    {
        var lecturas = lote.Lecturas ?? [];
        if (lecturas.Count == 0)
            throw new ExcepcionValidacion("El lote no trae lecturas.");
        if (lecturas.Count > MaximoPorLote)
            throw new ExcepcionValidacion($"Un lote admite como máximo {MaximoPorLote} lecturas.");

        var codigos = lecturas.Select(l => l.Codigo ?? string.Empty).Distinct().ToList();
        var sensores = (await db.Sensores.Where(s => codigos.Contains(s.Codigo)).ToListAsync(ct))
            .ToDictionary(s => s.Codigo, StringComparer.OrdinalIgnoreCase);

        var ahora = DateTime.UtcNow;
        var rechazadas = new List<LecturaRechazada>();
        var aceptadas = 0;

        foreach (var lectura in lecturas)
        {
            var motivo = Validar(dispositivo, lectura, sensores, out var sensor);
            if (motivo is not null)
            {
                rechazadas.Add(new LecturaRechazada(lectura.Codigo, motivo));
                continue;
            }

            // Solo se tocan estas dos columnas: el ciclo escribe ValorActual y UltimaLectura,
            // así que la API y el simulador no se pisan aunque corran en pods distintos.
            sensor!.ValorRecibido = lectura.Valor!.Value;
            sensor.RecibidoEn = ahora;
            aceptadas++;
        }

        await db.SaveChangesAsync(ct);

        if (rechazadas.Count > 0)
            logger.LogWarning("{Dispositivo}: {Rechazadas} lecturas rechazadas ({Motivos}).", dispositivo.Id,
                rechazadas.Count, string.Join("; ", rechazadas.Select(r => $"{r.Codigo}: {r.Motivo}")));

        return new ResultadoIot(dispositivo.Id, aceptadas, rechazadas);
    }

    private static string? Validar(DispositivoIot dispositivo, LecturaIotRequest lectura,
        Dictionary<string, Sensor> sensores, out Sensor? sensor)
    {
        sensor = null;
        if (string.IsNullOrWhiteSpace(lectura.Codigo))
            return "Falta el código del sensor.";
        if (lectura.Valor is null)
            return "Falta el valor.";
        // Un dispositivo solo reporta sus propios sensores: una llave robada a una comunidad
        // no sirve para falsear las lecturas de otra.
        if (!dispositivo.Codigos.Contains(lectura.Codigo))
            return "El dispositivo no está autorizado para este sensor.";
        if (!sensores.TryGetValue(lectura.Codigo, out sensor))
            return "No existe un sensor con ese código.";
        if (sensor.Estado == EstadoSensor.Inactivo)
            return "El sensor está inactivo.";
        if (lectura.Valor < sensor.ValorMinimo || lectura.Valor > sensor.ValorMaximo)
            return $"Valor fuera del rango físico del sensor ({sensor.ValorMinimo} a {sensor.ValorMaximo} {sensor.UnidadMedida}).";
        return null;
    }
}
