using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SWMatrc.Comun;
using SWMatrc.Sensores.Aplicacion;
using SWMatrc.Sensores.Datos;
using SWMatrc.Sensores.Dominio;
using SWMatrc.Sensores.Integracion;
using SWMatrc.Sensores.Simulacion;
using SWMatrc.Sensores.TiempoReal;

namespace SWMatrc.Sensores.Tests;

/// <summary>
/// Arma el servicio con dobles escritos a mano: una base en memoria, un Community y un
/// Alert simulados, un simulador programable y un hub que solo anota lo que se difundió.
/// </summary>
internal sealed class Escenario
{
    public SensoresDb Db { get; } = new(new DbContextOptionsBuilder<SensoresDb>()
        .UseInMemoryDatabase($"sensores-{Guid.NewGuid()}")
        .Options);

    public ComunidadesFijas Comunidades { get; } = new();
    public AlertasFalsas Alertas { get; } = new();
    public NotificadorEnMemoria Notificador { get; } = new();
    public SimuladorProgramado Simulador { get; } = new();
    public AuditoriaEnMemoria Auditoria { get; } = new();
    public OpcionesSimulacion Opciones { get; } = new() { IntervaloSegundos = 3, CiclosToleranciaSinSenal = 3 };

    public ServicioMonitoreo Monitoreo() => new(Db, Simulador, Comunidades, Alertas, Notificador, Auditoria,
        Options.Create(Opciones), NullLogger<ServicioMonitoreo>.Instance);

    public ServicioSensores Sensores() => new(Db, Comunidades, Monitoreo(), Notificador, Auditoria);

    public ServicioLecturas Lecturas() => new(Db, Comunidades, Sensores(), Auditoria);

    public async Task<Sensor> AgregarSensorAsync(
        int comunidadId = 1, TipoSensor tipo = TipoSensor.Temperatura, string codigo = "TEMP-01",
        EstadoSensor estado = EstadoSensor.Activo, DateTime? ultimaLectura = null)
    {
        var plantilla = PlantillaSensor.Para(tipo);
        var sensor = new Sensor
        {
            ComunidadId = comunidadId,
            Codigo = codigo,
            Nombre = $"Sensor {codigo}",
            Tipo = tipo,
            UnidadMedida = plantilla.Unidad,
            Estado = estado,
            ValorMinimo = plantilla.ValorMinimo,
            ValorMaximo = plantilla.ValorMaximo,
            VariacionMaxima = plantilla.VariacionMaxima,
            ValorActual = plantilla.ValorReposo,
            UltimaLectura = ultimaLectura ?? DateTime.UtcNow
        };
        Db.Sensores.Add(sensor);
        await Db.SaveChangesAsync();
        return sensor;
    }
}

internal sealed class ComunidadesFijas : IFuenteComunidades
{
    public Dictionary<int, ComunidadInfo>? Catalogo { get; set; } = new()
    {
        [1] = new ComunidadInfo(1, "Aldea El Rosario", true, 14.3m, -90.78m),
        [2] = new ComunidadInfo(2, "Caserío Las Flores", true, 16.93m, -89.89m)
    };

    public Task<IReadOnlyDictionary<int, ComunidadInfo>?> ListarAsync(CancellationToken ct, bool forzar = false) =>
        Task.FromResult<IReadOnlyDictionary<int, ComunidadInfo>?>(Catalogo);
}

internal sealed class AlertasFalsas : IClienteAlertas
{
    /// <summary>Si es falso, se comporta como un Alert Service caído.</summary>
    public bool Disponible { get; set; } = true;

    /// <summary>Nivel que se devuelve para cada sensor; los que no estén aquí, Verde.</summary>
    public Dictionary<int, NivelAlerta> Niveles { get; } = [];

    public List<CambioAlerta> CambiosPorDevolver { get; } = [];

    public List<SolicitudEvaluacion> Solicitudes { get; } = [];

    public Task<RespuestaEvaluacion?> EvaluarAsync(SolicitudEvaluacion solicitud, CancellationToken ct)
    {
        Solicitudes.Add(solicitud);
        if (!Disponible)
            return Task.FromResult<RespuestaEvaluacion?>(null);

        var niveles = solicitud.Comunidades
            .SelectMany(c => c.Sensores)
            .Select(s => new NivelSensor(s.SensorId, Niveles.GetValueOrDefault(s.SensorId, NivelAlerta.Verde)))
            .ToList();
        return Task.FromResult<RespuestaEvaluacion?>(new RespuestaEvaluacion(niveles, [.. CambiosPorDevolver]));
    }

    public Task<AlertasActivas?> ActivasAsync(int comunidadId, CancellationToken ct) =>
        Task.FromResult<AlertasActivas?>(Disponible ? new AlertasActivas(NivelAlerta.Verde, []) : null);

    public Task<bool> ReiniciarAsync(CancellationToken ct) => Task.FromResult(Disponible);
}

internal sealed class NotificadorEnMemoria : INotificador
{
    public List<LecturaDto> Lecturas { get; } = [];
    public List<SensorDto> Sensores { get; } = [];
    public List<(string Evento, int ComunidadId)> Alertas { get; } = [];

    public Task LecturaAsync(LecturaDto lectura, CancellationToken ct) { Lecturas.Add(lectura); return Task.CompletedTask; }
    public Task EstadoSensorAsync(SensorDto sensor, CancellationToken ct) { Sensores.Add(sensor); return Task.CompletedTask; }
    public Task AlertaAsync(string evento, int comunidadId, object alerta, CancellationToken ct) { Alertas.Add((evento, comunidadId)); return Task.CompletedTask; }
    public Task SistemaReiniciadoAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Devuelve el valor indicado para cada sensor; nulo simula un instrumento mudo.</summary>
internal sealed class SimuladorProgramado : ISimuladorClima
{
    public Dictionary<int, decimal?> Valores { get; } = [];
    public decimal? PorDefecto { get; set; } = 20m;

    public decimal? SiguienteValor(Sensor sensor) => Valores.TryGetValue(sensor.Id, out var v) ? v : PorDefecto;
    public void Reiniciar() { }
}

internal sealed class AuditoriaEnMemoria : IClienteAuditoria
{
    public List<string> Acciones { get; } = [];
    public void Registrar(string accion, string entidad, object? entidadId = null, object? detalle = null) => Acciones.Add(accion);
}

internal static class Json2
{
    public static JsonElement Alerta(string mensaje) => JsonSerializer.SerializeToElement(new { mensaje });
}
