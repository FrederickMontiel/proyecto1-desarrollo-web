using Microsoft.EntityFrameworkCore;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Dtos;
using SWMatrc.Domain.Entities;
using SWMatrc.Domain.Enums;
using SWMatrc.Infrastructure.Persistencia;

namespace SWMatrc.Application.Tests;

/// <summary>
/// Dobles de prueba escritos a mano en lugar de una biblioteca de simulación. Son pocos y
/// simples, y así las pruebas no dependen de nada más que de xUnit.
/// </summary>
internal static class Dobles
{
    /// <summary>Contexto en memoria, aislado por nombre para que las pruebas no se pisen.</summary>
    public static SwmatrcDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<SwmatrcDbContext>()
            .UseInMemoryDatabase($"swmatrc-pruebas-{Guid.NewGuid()}")
            .Options;

        return new SwmatrcDbContext(opciones);
    }
}

/// <summary>Fuente de lecturas gobernada por la prueba: devuelve lo que se le indique.</summary>
internal sealed class SimuladorProgramado : ISimuladorClima
{
    private readonly Queue<decimal?> _valores = new();

    /// <summary>Valor que se entrega cuando la cola se agota.</summary>
    public decimal? ValorPorDefecto { get; set; } = 20m;

    public int Reinicios { get; private set; }

    public SimuladorProgramado Encolar(params decimal[] valores)
    {
        foreach (var valor in valores)
            _valores.Enqueue(valor);

        return this;
    }

    /// <summary>
    /// Encola ciclos en los que el instrumento no responde. Existe como método aparte
    /// porque <c>Encolar(null)</c> se interpretaría como «sin argumentos», no como
    /// «un valor nulo».
    /// </summary>
    public SimuladorProgramado SinRespuesta(int ciclos = 1)
    {
        for (var i = 0; i < ciclos; i++)
            _valores.Enqueue(null);

        return this;
    }

    public decimal? SiguienteValor(Sensor sensor) =>
        _valores.Count > 0 ? _valores.Dequeue() : ValorPorDefecto;

    public void Reiniciar() => Reinicios++;
}

/// <summary>Registra las notificaciones emitidas para poder comprobarlas.</summary>
internal sealed class NotificadorEspia : INotificadorTiempoReal
{
    public List<LecturaDto> Lecturas { get; } = [];
    public List<AlertaDto> AlertasGeneradas { get; } = [];
    public List<AlertaDto> AlertasCerradas { get; } = [];
    public List<SensorDto> SensoresCambiados { get; } = [];
    public int Reinicios { get; private set; }

    public Task LecturaRecibidaAsync(LecturaDto lectura, CancellationToken ct = default)
    {
        Lecturas.Add(lectura);
        return Task.CompletedTask;
    }

    public Task AlertaGeneradaAsync(AlertaDto alerta, CancellationToken ct = default)
    {
        AlertasGeneradas.Add(alerta);
        return Task.CompletedTask;
    }

    public Task AlertaCerradaAsync(AlertaDto alerta, CancellationToken ct = default)
    {
        AlertasCerradas.Add(alerta);
        return Task.CompletedTask;
    }

    public Task EstadoSensorCambiadoAsync(SensorDto sensor, CancellationToken ct = default)
    {
        SensoresCambiados.Add(sensor);
        return Task.CompletedTask;
    }

    public Task EstadoComunidadAsync(EstadoComunidadDto estado, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task SistemaReiniciadoAsync(CancellationToken ct = default)
    {
        Reinicios++;
        return Task.CompletedTask;
    }
}

/// <summary>Bitácora que no escribe nada: la auditoría se prueba por separado.</summary>
internal sealed class BitacoraMuda : IServicioBitacora
{
    public List<string> Acciones { get; } = [];

    public Task RegistrarAsync(
        string accion, string entidad, object? entidadId = null, object? detalle = null,
        CancellationToken ct = default)
    {
        Acciones.Add(accion);
        return Task.CompletedTask;
    }

    public Task RegistrarComoAsync(
        int usuarioId, string usuarioEmail, string accion, string entidad,
        object? entidadId = null, object? detalle = null, CancellationToken ct = default)
    {
        Acciones.Add(accion);
        return Task.CompletedTask;
    }

    public Task<PaginaDto<BitacoraDto>> ListarAsync(FiltroBitacora filtro, CancellationToken ct = default) =>
        Task.FromResult(new PaginaDto<BitacoraDto>());

    public Task<(IReadOnlyList<string> Acciones, IReadOnlyList<string> Entidades)> CatalogosAsync(
        CancellationToken ct = default) =>
        Task.FromResult<(IReadOnlyList<string>, IReadOnlyList<string>)>(([], []));
}

/// <summary>Identidad fija para las pruebas que dependen de quién ejecuta la acción.</summary>
internal sealed class UsuarioActualFijo(int? id = null, RolUsuario rol = RolUsuario.Administrador)
    : IUsuarioActual
{
    public int? Id { get; } = id;
    public string? Email { get; } = id is null ? null : $"usuario{id}@swmatrc.org";
    public RolUsuario? Rol { get; } = rol;
    public string? DireccionIp => "127.0.0.1";
    public bool Autenticado => Id is not null;
}

/// <summary>Hasheador trivial: las pruebas no necesitan el coste real de BCrypt.</summary>
internal sealed class HasheadorFalso : IHasheadorPassword
{
    public string Hashear(string password) => $"hash:{password}";

    public bool Verificar(string password, string hash) => hash == $"hash:{password}";
}

internal sealed class GeneradorTokenFalso : IGeneradorToken
{
    public (string Token, DateTime Expira) Generar(Usuario usuario) =>
        ($"token-{usuario.Id}", DateTime.UtcNow.AddHours(1));
}
