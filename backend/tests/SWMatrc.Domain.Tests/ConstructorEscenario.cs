using SWMatrc.Domain.Entities;
using SWMatrc.Domain.Enums;
using SWMatrc.Domain.Riesgo;

namespace SWMatrc.Domain.Tests;

/// <summary>
/// Ayuda a describir escenarios meteorológicos en los tests con el vocabulario del
/// problema —«llueve 40 mm y el cauce está en 4,8 m»— en lugar de armar a mano una
/// comunidad con su lista de sensores en cada caso.
/// </summary>
internal sealed class ConstructorEscenario
{
    private readonly Comunidad _comunidad = new()
    {
        Id = 1,
        Nombre = "Comunidad de prueba",
        Activa = true
    };

    private readonly List<Sensor> _sensores = [];
    private int _siguienteId = 1;

    public ConstructorEscenario Con(TipoSensor tipo, decimal valor, EstadoSensor estado = EstadoSensor.Activo)
    {
        // Los umbrales salen de la plantilla del tipo, que es la calibración real del
        // sistema: así los tests validan los valores que se usan en producción.
        var plantilla = PlantillaSensor.Para(tipo);

        _sensores.Add(new Sensor
        {
            Id = _siguienteId++,
            ComunidadId = _comunidad.Id,
            Codigo = $"{tipo.ToString()[..3].ToUpperInvariant()}-{_siguienteId:00}",
            Nombre = tipo.ToString(),
            Tipo = tipo,
            UnidadMedida = plantilla.Unidad,
            Estado = estado,
            ValorMinimo = plantilla.ValorMinimo,
            ValorMaximo = plantilla.ValorMaximo,
            VariacionMaxima = plantilla.VariacionMaxima,
            ValorActual = valor,
            UltimaLectura = DateTime.UtcNow,
            UmbralAmarilloAlto = plantilla.AmarilloAlto,
            UmbralNaranjaAlto = plantilla.NaranjaAlto,
            UmbralRojoAlto = plantilla.RojoAlto,
            UmbralAmarilloBajo = plantilla.AmarilloBajo,
            UmbralNaranjaBajo = plantilla.NaranjaBajo,
            UmbralRojoBajo = plantilla.RojoBajo
        });

        return this;
    }

    /// <summary>Día tranquilo: los cinco sensores en sus valores de reposo.</summary>
    public static ConstructorEscenario EnCalma() =>
        new ConstructorEscenario()
            .Con(TipoSensor.Temperatura, PlantillaSensor.Para(TipoSensor.Temperatura).ValorReposo)
            .Con(TipoSensor.Humedad, PlantillaSensor.Para(TipoSensor.Humedad).ValorReposo)
            .Con(TipoSensor.Viento, PlantillaSensor.Para(TipoSensor.Viento).ValorReposo)
            .Con(TipoSensor.Lluvia, PlantillaSensor.Para(TipoSensor.Lluvia).ValorReposo)
            .Con(TipoSensor.NivelRio, PlantillaSensor.Para(TipoSensor.NivelRio).ValorReposo);

    /// <summary>Reemplaza la lectura de una magnitud ya presente en el escenario.</summary>
    public ConstructorEscenario Ajustar(TipoSensor tipo, decimal valor)
    {
        var sensor = _sensores.Single(s => s.Tipo == tipo);
        sensor.ValorActual = valor;
        return this;
    }

    public ContextoEvaluacion Construir()
    {
        _comunidad.Sensores = _sensores;
        return new ContextoEvaluacion(_comunidad, _sensores);
    }
}
