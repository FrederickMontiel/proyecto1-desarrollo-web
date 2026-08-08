using SWMatrc.Domain.Entities;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Domain.Tests;

/// <summary>
/// Pruebas de la clasificación de una lectura contra los umbrales del sensor. Es la
/// operación más usada del dominio: la ejecuta el motor de riesgo en cada ciclo y también
/// la interfaz para colorear cada tarjeta.
/// </summary>
public class SensorTests
{
    /// <summary>Sensor con umbrales por exceso, como un anemómetro o un limnímetro.</summary>
    private static Sensor ConUmbralesAltos() => new()
    {
        Tipo = TipoSensor.Viento,
        ValorMinimo = 0,
        ValorMaximo = 150,
        UmbralAmarilloAlto = 39,
        UmbralNaranjaAlto = 62,
        UmbralRojoAlto = 89
    };

    /// <summary>Sensor con umbrales por defecto, como un higrómetro frente a la sequía.</summary>
    private static Sensor ConUmbralesBajos() => new()
    {
        Tipo = TipoSensor.Humedad,
        ValorMinimo = 0,
        ValorMaximo = 100,
        UmbralAmarilloBajo = 35,
        UmbralNaranjaBajo = 25,
        UmbralRojoBajo = 15
    };

    [Theory]
    [InlineData(10, NivelAlerta.Verde)]
    [InlineData(38.9, NivelAlerta.Verde)]
    [InlineData(39, NivelAlerta.Amarillo)]   // el umbral es inclusivo
    [InlineData(61, NivelAlerta.Amarillo)]
    [InlineData(62, NivelAlerta.Naranja)]
    [InlineData(88, NivelAlerta.Naranja)]
    [InlineData(89, NivelAlerta.Rojo)]
    [InlineData(140, NivelAlerta.Rojo)]
    public void ClasificaPorExceso(decimal valor, NivelAlerta esperado)
    {
        Assert.Equal(esperado, ConUmbralesAltos().Clasificar(valor));
    }

    [Theory]
    [InlineData(80, NivelAlerta.Verde)]
    [InlineData(35.1, NivelAlerta.Verde)]
    [InlineData(35, NivelAlerta.Amarillo)]
    [InlineData(26, NivelAlerta.Amarillo)]
    [InlineData(25, NivelAlerta.Naranja)]
    [InlineData(16, NivelAlerta.Naranja)]
    [InlineData(15, NivelAlerta.Rojo)]
    [InlineData(3, NivelAlerta.Rojo)]
    public void ClasificaPorDefecto(decimal valor, NivelAlerta esperado)
    {
        Assert.Equal(esperado, ConUmbralesBajos().Clasificar(valor));
    }

    [Fact]
    public void ConUmbralesEnAmbosSentidos_devuelveElNivelMasSevero()
    {
        // Un termómetro vigila los dos extremos: calor extremo e riesgo de helada.
        var termometro = new Sensor
        {
            Tipo = TipoSensor.Temperatura,
            ValorMinimo = -10,
            ValorMaximo = 50,
            UmbralAmarilloAlto = 33,
            UmbralNaranjaAlto = 38,
            UmbralRojoAlto = 42,
            UmbralAmarilloBajo = 5,
            UmbralNaranjaBajo = 2,
            UmbralRojoBajo = 0
        };

        Assert.Equal(NivelAlerta.Verde, termometro.Clasificar(22));
        Assert.Equal(NivelAlerta.Rojo, termometro.Clasificar(45));
        Assert.Equal(NivelAlerta.Rojo, termometro.Clasificar(-3));
        Assert.Equal(NivelAlerta.Amarillo, termometro.Clasificar(34));
        Assert.Equal(NivelAlerta.Amarillo, termometro.Clasificar(4));
    }

    [Fact]
    public void SinUmbralesConfigurados_siempreClasificaEnVerde()
    {
        var sensor = new Sensor { Tipo = TipoSensor.Lluvia, ValorMinimo = 0, ValorMaximo = 100 };

        Assert.Equal(NivelAlerta.Verde, sensor.Clasificar(99));
    }

    [Theory]
    [InlineData(EstadoSensor.Activo, true)]
    [InlineData(EstadoSensor.Inactivo, false)]
    [InlineData(EstadoSensor.SinSenal, false)]
    public void SoloEstaOperativoCuandoEstaActivo(EstadoSensor estado, bool esperado)
    {
        Assert.Equal(esperado, new Sensor { Estado = estado }.EstaOperativo);
    }
}
