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
/// Detección de sensores que dejan de reportar.
///
/// Es la diferencia entre «todo está en orden» y «ya no sé lo que ocurre». Un sistema de
/// alerta temprana que confunde las dos cosas transmite una calma que no puede garantizar.
/// </summary>
public class WatchdogSensoresTests
{
    private const int IntervaloSegundos = 3;
    private const int CiclosTolerancia = 3;

    private static ServicioMonitoreo CrearServicio(
        SwmatrcDbContext db, SimuladorProgramado simulador, NotificadorEspia notificador)
    {
        var motor = new MotorRiesgo(
        [
            new ReglaInundacion(), new ReglaTormenta(), new ReglaHelada(),
            new ReglaSequia(), new ReglaIncendioForestal()
        ]);

        var opciones = Options.Create(new OpcionesMonitoreo
        {
            IntervaloSegundos = IntervaloSegundos,
            CiclosToleranciaSinSenal = CiclosTolerancia
        });

        return new ServicioMonitoreo(
            db, motor, simulador, notificador, new BitacoraMuda(), opciones,
            NullLogger<ServicioMonitoreo>.Instance);
    }

    /// <summary>Comunidad con un único termómetro, que es cuanto necesitan estas pruebas.</summary>
    private static async Task<Sensor> SembrarSensorAsync(SwmatrcDbContext db, DateTime ultimaLectura)
    {
        var comunidad = new Comunidad { Nombre = "Comunidad de prueba", Activa = true };
        db.Comunidades.Add(comunidad);
        await db.SaveChangesAsync();

        var plantilla = PlantillaSensor.Para(TipoSensor.Temperatura);
        var sensor = new Sensor
        {
            ComunidadId = comunidad.Id,
            Codigo = "TEMP-01",
            Nombre = "Termómetro",
            Tipo = TipoSensor.Temperatura,
            UnidadMedida = plantilla.Unidad,
            Estado = EstadoSensor.Activo,
            ValorMinimo = plantilla.ValorMinimo,
            ValorMaximo = plantilla.ValorMaximo,
            VariacionMaxima = plantilla.VariacionMaxima,
            ValorActual = plantilla.ValorReposo,
            UltimaLectura = ultimaLectura,
            UmbralAmarilloAlto = plantilla.AmarilloAlto,
            UmbralNaranjaAlto = plantilla.NaranjaAlto,
            UmbralRojoAlto = plantilla.RojoAlto,
            UmbralAmarilloBajo = plantilla.AmarilloBajo,
            UmbralNaranjaBajo = plantilla.NaranjaBajo,
            UmbralRojoBajo = plantilla.RojoBajo
        };

        db.Sensores.Add(sensor);
        await db.SaveChangesAsync();

        return sensor;
    }

    [Fact]
    public async Task SilencioProlongado_marcaElSensorSinSenal()
    {
        using var db = Dobles.CrearContexto();

        // La última lectura quedó muy atrás: ya se superó la tolerancia.
        var sensor = await SembrarSensorAsync(db, DateTime.UtcNow.AddMinutes(-5));

        var notificador = new NotificadorEspia();
        var simulador = new SimuladorProgramado().SinRespuesta(); // el instrumento no responde

        await CrearServicio(db, simulador, notificador).EjecutarCicloAsync();

        Assert.Equal(EstadoSensor.SinSenal, sensor.Estado);

        // El cambio se difunde: el tablero tiene que enterarse sin recargar.
        Assert.Contains(notificador.SensoresCambiados, s => s.Id == sensor.Id && s.Estado == EstadoSensor.SinSenal);
    }

    [Fact]
    public async Task SilencioBreve_todaviaNoMarcaElSensor()
    {
        using var db = Dobles.CrearContexto();

        // Solo ha pasado un ciclo: un retraso puntual de la red no es una avería.
        var sensor = await SembrarSensorAsync(db, DateTime.UtcNow.AddSeconds(-IntervaloSegundos));

        var simulador = new SimuladorProgramado().SinRespuesta();

        await CrearServicio(db, simulador, new NotificadorEspia()).EjecutarCicloAsync();

        Assert.Equal(EstadoSensor.Activo, sensor.Estado);
    }

    [Fact]
    public async Task SensorQueNoReporta_noGeneraLecturaInventada()
    {
        using var db = Dobles.CrearContexto();
        await SembrarSensorAsync(db, DateTime.UtcNow.AddMinutes(-5));

        var notificador = new NotificadorEspia();
        var simulador = new SimuladorProgramado().SinRespuesta();

        await CrearServicio(db, simulador, notificador).EjecutarCicloAsync();

        // Ni se persiste una muestra ni se difunde: no hay dato que comunicar.
        Assert.Empty(db.Lecturas);
        Assert.Empty(notificador.Lecturas);
    }

    [Fact]
    public async Task SensorQueVuelveAReportar_recuperaElEstadoActivo()
    {
        using var db = Dobles.CrearContexto();

        var sensor = await SembrarSensorAsync(db, DateTime.UtcNow.AddMinutes(-5));
        sensor.Estado = EstadoSensor.SinSenal;
        await db.SaveChangesAsync();

        var notificador = new NotificadorEspia();
        var simulador = new SimuladorProgramado().Encolar(24.5m);

        await CrearServicio(db, simulador, notificador).EjecutarCicloAsync();

        Assert.Equal(EstadoSensor.Activo, sensor.Estado);
        Assert.Equal(24.5m, sensor.ValorActual);
        Assert.Single(notificador.Lecturas);
    }

    [Fact]
    public async Task SensorDesactivadoAMano_quedaFueraDelCiclo()
    {
        using var db = Dobles.CrearContexto();

        var sensor = await SembrarSensorAsync(db, DateTime.UtcNow.AddMinutes(-5));
        sensor.Estado = EstadoSensor.Inactivo;
        await db.SaveChangesAsync();

        var simulador = new SimuladorProgramado().Encolar(30m);

        await CrearServicio(db, simulador, new NotificadorEspia()).EjecutarCicloAsync();

        // Apagar un sensor es una decisión deliberada: no se le reactiva ni se le audita
        // como averiado, simplemente no participa.
        Assert.Equal(EstadoSensor.Inactivo, sensor.Estado);
        Assert.Empty(db.Lecturas);
    }

    [Fact]
    public async Task CicloNormal_persisteLecturaYLaDifunde()
    {
        using var db = Dobles.CrearContexto();
        var sensor = await SembrarSensorAsync(db, DateTime.UtcNow);

        var notificador = new NotificadorEspia();
        var simulador = new SimuladorProgramado().Encolar(26.75m);

        await CrearServicio(db, simulador, notificador).EjecutarCicloAsync();

        Assert.Single(db.Lecturas);
        Assert.Equal(26.75m, sensor.ValorActual);
        Assert.Equal(sensor.Codigo, Assert.Single(notificador.Lecturas).SensorCodigo);
    }
}
