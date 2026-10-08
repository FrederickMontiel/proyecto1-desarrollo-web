using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SWMatrc.Application.Comun;
using SWMatrc.Application.Servicios;
using SWMatrc.Domain.Entities;
using SWMatrc.Domain.Enums;
using SWMatrc.Domain.Riesgo;
using SWMatrc.Domain.Riesgo.Reglas;
using SWMatrc.Infrastructure.Persistencia;

namespace SWMatrc.Application.Tests;

/// <summary>
/// Reglas de alerta configurables y ciclo de vida de las alertas (Fase 2).
///
/// Comprueban que una regla definida desde el panel levanta la alerta con trazabilidad
/// completa —regla, umbral, valor— y que atender o cerrar deja constancia de quién lo hizo.
/// </summary>
public class ReglasYAlertasTests
{
    private const int UsuarioOperador = 7;

    private static ServicioMonitoreo CrearMonitoreo(SwmatrcDbContext db, NotificadorEspia notificador) =>
        new(
            db,
            new MotorRiesgo([new ReglaInundacion(), new ReglaTormenta(), new ReglaHelada(), new ReglaSequia(), new ReglaIncendioForestal()]),
            new SimuladorProgramado(),
            notificador,
            new BitacoraMuda(),
            Options.Create(new OpcionesMonitoreo()),
            NullLogger<ServicioMonitoreo>.Instance);

    /// <summary>Comunidad con un detector de humo, magnitud que ninguna regla integrada evalúa.</summary>
    private static async Task<Sensor> SembrarDetectorAsync(SwmatrcDbContext db, decimal valor, EstadoSensor estado = EstadoSensor.Activo)
    {
        var comunidad = new Comunidad { Nombre = "Valle de prueba", Pais = "Guatemala", Activa = true };
        db.Comunidades.Add(comunidad);
        await db.SaveChangesAsync();

        var sensor = new Sensor
        {
            ComunidadId = comunidad.Id,
            Codigo = "HUMO-01",
            Nombre = "Detector de humo",
            Tipo = TipoSensor.Humo,
            UnidadMedida = "µg/m³",
            Estado = estado,
            ValorMinimo = 0m,
            ValorMaximo = 500m,
            ValorActual = valor,
            UltimaLectura = DateTime.UtcNow
        };

        db.Sensores.Add(sensor);
        db.ReglasAlerta.Add(new ReglaAlerta
        {
            Nombre = "Humo denso",
            TipoSensor = TipoSensor.Humo,
            ValorMinimo = 150m,
            Nivel = NivelAlerta.Naranja,
            Fenomeno = TipoFenomeno.IncendioForestal,
            Mensaje = "Humo en {comunidad}: {valor} {unidad}",
            Activa = true
        });
        await db.SaveChangesAsync();

        return sensor;
    }

    [Fact]
    public async Task ReglaConfigurada_generaAlertaConTrazabilidad()
    {
        using var db = Dobles.CrearContexto();
        var sensor = await SembrarDetectorAsync(db, 180m);
        var regla = await db.ReglasAlerta.SingleAsync();

        await CrearMonitoreo(db, new NotificadorEspia()).EvaluarComunidadAsync(sensor.ComunidadId);

        var alerta = await db.Alertas.SingleAsync();
        Assert.Equal(EstadoAlerta.Activa, alerta.Estado);
        Assert.Equal(TipoFenomeno.IncendioForestal, alerta.Fenomeno);
        Assert.Equal(NivelAlerta.Naranja, alerta.Nivel);
        Assert.Equal(regla.Id, alerta.ReglaAlertaId);
        Assert.Equal("Humo denso", alerta.ReglaNombre);
        Assert.Equal(150m, alerta.Umbral);
        Assert.Equal(180m, alerta.ValorDisparo);
        Assert.Equal(sensor.Id, alerta.SensorId);
        Assert.Equal("Humo en Valle de prueba: 180 µg/m³", alerta.Mensaje);

        // El historial nace con el sensor enlazado.
        var evento = await db.Eventos.SingleAsync();
        Assert.Equal(sensor.Id, evento.SensorId);
        Assert.Equal(EstadoAlerta.Activa, evento.Estado);
    }

    [Fact]
    public async Task EmpateDeNivel_ganaLaReglaMasEspecifica()
    {
        using var db = Dobles.CrearContexto();
        var sensor = await SembrarDetectorAsync(db, 180m);

        // Misma severidad que "Humo denso" (≥ 150), pero con un umbral más cercano a 180.
        db.ReglasAlerta.Add(new ReglaAlerta
        {
            Nombre = "Humo cercano al límite",
            TipoSensor = TipoSensor.Humo,
            ValorMinimo = 175m,
            Nivel = NivelAlerta.Naranja,
            Fenomeno = TipoFenomeno.IncendioForestal,
            Mensaje = "Humo",
            Activa = true
        });
        await db.SaveChangesAsync();

        await CrearMonitoreo(db, new NotificadorEspia()).EvaluarComunidadAsync(sensor.ComunidadId);

        var alerta = await db.Alertas.SingleAsync();
        Assert.Equal("Humo cercano al límite", alerta.ReglaNombre);
        Assert.Equal(175m, alerta.Umbral);
    }

    [Fact]
    public async Task ReglaDesactivada_noGeneraAlerta()
    {
        using var db = Dobles.CrearContexto();
        var sensor = await SembrarDetectorAsync(db, 180m);
        (await db.ReglasAlerta.SingleAsync()).Activa = false;
        await db.SaveChangesAsync();

        await CrearMonitoreo(db, new NotificadorEspia()).EvaluarComunidadAsync(sensor.ComunidadId);

        Assert.Empty(db.Alertas);
    }

    [Fact]
    public async Task SensorDesactivado_noGeneraAlertas()
    {
        using var db = Dobles.CrearContexto();
        var sensor = await SembrarDetectorAsync(db, 180m, EstadoSensor.Inactivo);

        await CrearMonitoreo(db, new NotificadorEspia()).EvaluarComunidadAsync(sensor.ComunidadId);

        Assert.Empty(db.Alertas);
    }

    [Fact]
    public async Task SensorDesactivado_rechazaLecturasManuales()
    {
        using var db = Dobles.CrearContexto();
        var sensor = await SembrarDetectorAsync(db, 10m, EstadoSensor.Inactivo);
        var notificador = new NotificadorEspia();
        var sensores = new ServicioSensores(db, new BitacoraMuda(), notificador, CrearMonitoreo(db, notificador));

        await Assert.ThrowsAsync<ExcepcionValidacion>(() => sensores.EstablecerValorAsync(sensor.Id, 200m));
        Assert.Empty(db.Lecturas);
    }

    [Fact]
    public async Task AtenderYCerrar_registranAlResponsable()
    {
        using var db = Dobles.CrearContexto();
        var sensor = await SembrarDetectorAsync(db, 180m);
        var notificador = new NotificadorEspia();
        await CrearMonitoreo(db, notificador).EvaluarComunidadAsync(sensor.ComunidadId);
        var alertaId = (await db.Alertas.SingleAsync()).Id;

        var bitacora = new BitacoraMuda();
        var servicio = new ServicioAlertas(db, new UsuarioActualFijo(UsuarioOperador, RolUsuario.Operador), bitacora, notificador);

        var atendida = await servicio.AtenderAsync(alertaId, "Brigada en camino");
        Assert.Equal(EstadoAlerta.Atendida, atendida.Estado);

        var cerrada = await servicio.CerrarAsync(alertaId);
        Assert.Equal(EstadoAlerta.Cerrada, cerrada.Estado);
        Assert.False(cerrada.Activa);

        var alerta = await db.Alertas.AsNoTracking().SingleAsync();
        Assert.Equal(UsuarioOperador, alerta.ReconocidaPorUsuarioId);
        Assert.Equal(UsuarioOperador, alerta.CerradaPorUsuarioId);

        var evento = await db.Eventos.AsNoTracking().SingleAsync();
        Assert.Equal(EstadoAlerta.Cerrada, evento.Estado);
        Assert.Equal(UsuarioOperador, evento.UsuarioResponsableId);
        Assert.NotNull(evento.FechaFin);

        Assert.Equal(["AlertaAtendida", "AlertaCerrada"], bitacora.Acciones);

        // Una alerta cerrada no se vuelve a cerrar.
        await Assert.ThrowsAsync<ExcepcionValidacion>(() => servicio.CerrarAsync(alertaId));
    }

    [Fact]
    public async Task ComunidadDesactivada_cierraSusAlertas()
    {
        using var db = Dobles.CrearContexto();
        var sensor = await SembrarDetectorAsync(db, 180m);
        var monitoreo = CrearMonitoreo(db, new NotificadorEspia());
        await monitoreo.EvaluarComunidadAsync(sensor.ComunidadId);

        var comunidades = new ServicioComunidades(db, new BitacoraMuda(), monitoreo);
        await comunidades.CambiarEstadoAsync(sensor.ComunidadId, activa: false);

        var alerta = await db.Alertas.AsNoTracking().SingleAsync();
        Assert.Equal(EstadoAlerta.Cerrada, alerta.Estado);
    }

    [Theory]
    [InlineData(149.9, false)]
    [InlineData(150, true)]
    [InlineData(400, true)]
    public void Regla_seCumpleDentroDeSuRango(decimal valor, bool esperado)
    {
        var regla = new ReglaAlerta { ValorMinimo = 150m };
        Assert.Equal(esperado, regla.SeCumple(valor));
    }
}
