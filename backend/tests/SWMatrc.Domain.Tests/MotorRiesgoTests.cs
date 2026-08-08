using SWMatrc.Domain.Enums;
using SWMatrc.Domain.Riesgo;
using SWMatrc.Domain.Riesgo.Reglas;

namespace SWMatrc.Domain.Tests;

/// <summary>
/// Pruebas del motor de riesgo completo, con el mismo juego de reglas que registra la
/// aplicación. Verifican la propiedad más importante del sistema: que avise cuando hay
/// peligro y que guarde silencio cuando no lo hay.
/// </summary>
public class MotorRiesgoTests
{
    private static IMotorRiesgo CrearMotor() =>
        new MotorRiesgo(
        [
            new ReglaInundacion(),
            new ReglaTormenta(),
            new ReglaHelada(),
            new ReglaSequia(),
            new ReglaIncendioForestal()
        ]);

    [Fact]
    public void EnCalma_noEmiteNingunaAlerta()
    {
        // Es el caso que más fácilmente se rompe al recalibrar umbrales: un sistema que
        // avisa en un día tranquilo deja de ser creíble y se acaba ignorando.
        var contexto = ConstructorEscenario.EnCalma().Construir();

        var diagnosticos = CrearMotor().Evaluar(contexto);

        Assert.Empty(diagnosticos);
    }

    [Fact]
    public void CauceSobreLaCotaDeDesbordamiento_declaraEmergenciaPorInundacion()
    {
        var contexto = ConstructorEscenario.EnCalma()
            .Ajustar(TipoSensor.NivelRio, 4.8m) // umbral rojo: 4,5 m
            .Construir();

        var inundacion = CrearMotor()
            .Evaluar(contexto)
            .Single(d => d.Fenomeno == TipoFenomeno.Inundacion);

        Assert.Equal(NivelAlerta.Rojo, inundacion.Nivel);
        Assert.Contains("EMERGENCIA", inundacion.Mensaje);
    }

    [Fact]
    public void LluviaIntensaSimultanea_escalaLaInundacionUnPeldano()
    {
        // El cauce por sí solo daría amarillo; la lluvia en curso anticipa que seguirá
        // subiendo, así que el aviso debe salir ya en naranja.
        var soloCauce = ConstructorEscenario.EnCalma()
            .Ajustar(TipoSensor.NivelRio, 3.2m)
            .Construir();

        var conLluvia = ConstructorEscenario.EnCalma()
            .Ajustar(TipoSensor.NivelRio, 3.2m)
            .Ajustar(TipoSensor.Lluvia, 35m) // por encima del umbral naranja de lluvia
            .Construir();

        var motor = CrearMotor();

        var sinAgravante = motor.Evaluar(soloCauce).Single(d => d.Fenomeno == TipoFenomeno.Inundacion);
        var agravada = motor.Evaluar(conLluvia).Single(d => d.Fenomeno == TipoFenomeno.Inundacion);

        Assert.Equal(NivelAlerta.Amarillo, sinAgravante.Nivel);
        Assert.Equal(NivelAlerta.Naranja, agravada.Nivel);
    }

    [Fact]
    public void TemperaturaBajoCero_declaraEmergenciaPorHelada()
    {
        var contexto = ConstructorEscenario.EnCalma()
            .Ajustar(TipoSensor.Temperatura, -1m) // umbral rojo bajo: 0 °C
            .Ajustar(TipoSensor.Humedad, 50m)     // humedad moderada: sin agravante
            .Construir();

        var helada = CrearMotor()
            .Evaluar(contexto)
            .Single(d => d.Fenomeno == TipoFenomeno.Helada);

        Assert.Equal(NivelAlerta.Rojo, helada.Nivel);
    }

    [Fact]
    public void HumedadAltaConTemperaturaBaja_agravaLaHeladaPorEscarcha()
    {
        var contexto = ConstructorEscenario.EnCalma()
            .Ajustar(TipoSensor.Temperatura, 4m) // umbral amarillo bajo: 5 °C
            .Ajustar(TipoSensor.Humedad, 90m)    // por encima del umbral de escarcha
            .Construir();

        var helada = CrearMotor()
            .Evaluar(contexto)
            .Single(d => d.Fenomeno == TipoFenomeno.Helada);

        Assert.Equal(NivelAlerta.Naranja, helada.Nivel);
        Assert.Contains("escarcha", helada.Mensaje);
    }

    [Fact]
    public void CalorSequedadYViento_declaranPeligroDeIncendio()
    {
        var contexto = ConstructorEscenario.EnCalma()
            .Ajustar(TipoSensor.Temperatura, 42m)
            .Ajustar(TipoSensor.Humedad, 10m)
            .Ajustar(TipoSensor.Viento, 45m)
            .Ajustar(TipoSensor.Lluvia, 0m)
            .Construir();

        var incendio = CrearMotor()
            .Evaluar(contexto)
            .Single(d => d.Fenomeno == TipoFenomeno.IncendioForestal);

        Assert.Equal(NivelAlerta.Rojo, incendio.Nivel);
    }

    [Fact]
    public void LluviaReciente_suprimeElPeligroDeIncendio()
    {
        // El combustible fino mojado no arde por mucho calor y viento que haga.
        var contexto = ConstructorEscenario.EnCalma()
            .Ajustar(TipoSensor.Temperatura, 42m)
            .Ajustar(TipoSensor.Humedad, 10m)
            .Ajustar(TipoSensor.Viento, 45m)
            .Ajustar(TipoSensor.Lluvia, 8m) // por encima del umbral supresor de 5 mm
            .Construir();

        var diagnosticos = CrearMotor().Evaluar(contexto);

        Assert.DoesNotContain(diagnosticos, d => d.Fenomeno == TipoFenomeno.IncendioForestal);
    }

    [Fact]
    public void SequedadSostenidaConCauceEnMinimos_declaraEmergenciaPorSequia()
    {
        var contexto = ConstructorEscenario.EnCalma()
            .Ajustar(TipoSensor.Lluvia, 0m)
            .Ajustar(TipoSensor.Humedad, 12m)
            .Ajustar(TipoSensor.Temperatura, 35m)
            .Ajustar(TipoSensor.NivelRio, 0.5m)
            .Construir();

        var sequia = CrearMotor()
            .Evaluar(contexto)
            .Single(d => d.Fenomeno == TipoFenomeno.Sequia);

        Assert.Equal(NivelAlerta.Rojo, sequia.Nivel);
    }

    [Fact]
    public void VientoHuracanado_declaraEmergenciaPorTormenta()
    {
        var contexto = ConstructorEscenario.EnCalma()
            .Ajustar(TipoSensor.Viento, 95m) // umbral rojo: 89 km/h
            .Construir();

        var tormenta = CrearMotor()
            .Evaluar(contexto)
            .Single(d => d.Fenomeno == TipoFenomeno.Tormenta);

        Assert.Equal(NivelAlerta.Rojo, tormenta.Nivel);
    }

    [Fact]
    public void SensorDesactivado_quedaFueraDeLaEvaluacion()
    {
        // Un instrumento fuera de servicio no debe sostener una alerta con su última
        // lectura: el motor solo considera los sensores operativos.
        var contexto = new ConstructorEscenario()
            .Con(TipoSensor.NivelRio, 5m, EstadoSensor.Inactivo)
            .Con(TipoSensor.Lluvia, 1m)
            .Con(TipoSensor.Humedad, 60m)
            .Construir();

        var diagnosticos = CrearMotor().Evaluar(contexto);

        Assert.DoesNotContain(diagnosticos, d => d.Fenomeno == TipoFenomeno.Inundacion);
    }

    [Fact]
    public void SensorSinSenal_quedaFueraDeLaEvaluacion()
    {
        // Un instrumento averiado conserva su última lectura, pero esa lectura ya no
        // describe la realidad: sostener una alerta con ella sería inventarse el dato.
        var contexto = new ConstructorEscenario()
            .Con(TipoSensor.NivelRio, 5m, EstadoSensor.SinSenal)
            .Con(TipoSensor.Lluvia, 1m)
            .Con(TipoSensor.Humedad, 60m)
            .Construir();

        var diagnosticos = CrearMotor().Evaluar(contexto);

        Assert.DoesNotContain(diagnosticos, d => d.Fenomeno == TipoFenomeno.Inundacion);
    }

    [Fact]
    public void SinSensorDeLaMagnitud_laReglaGuardaSilencio()
    {
        // Ausencia de dato no es ausencia de riesgo, pero tampoco autoriza a inventarlo:
        // la regla no puede concluir nada y no debe emitir ningún diagnóstico.
        var contexto = new ConstructorEscenario()
            .Con(TipoSensor.Viento, 12m)
            .Construir();

        var diagnosticos = CrearMotor().Evaluar(contexto);

        Assert.DoesNotContain(diagnosticos, d => d.Fenomeno == TipoFenomeno.Inundacion);
        Assert.DoesNotContain(diagnosticos, d => d.Fenomeno == TipoFenomeno.Helada);
    }

    [Fact]
    public void ConVariosFenomenos_losDevuelveDelMasGraveAlMenosGrave()
    {
        var contexto = ConstructorEscenario.EnCalma()
            .Ajustar(TipoSensor.NivelRio, 4.9m) // inundación en rojo
            .Ajustar(TipoSensor.Viento, 45m)    // tormenta en amarillo
            .Ajustar(TipoSensor.Lluvia, 20m)
            .Construir();

        var motor = CrearMotor();
        var diagnosticos = motor.Evaluar(contexto);

        Assert.True(diagnosticos.Count >= 2);
        Assert.Equal(NivelAlerta.Rojo, diagnosticos[0].Nivel);

        // La secuencia debe venir ordenada de mayor a menor severidad.
        var niveles = diagnosticos.Select(d => (int)d.Nivel).ToList();
        Assert.Equal(niveles.OrderByDescending(n => n), niveles);

        Assert.Equal(NivelAlerta.Rojo, motor.NivelGlobal(diagnosticos));
    }

    [Fact]
    public void SinDiagnosticos_elNivelGlobalEsVerde()
    {
        Assert.Equal(NivelAlerta.Verde, CrearMotor().NivelGlobal([]));
    }
}
