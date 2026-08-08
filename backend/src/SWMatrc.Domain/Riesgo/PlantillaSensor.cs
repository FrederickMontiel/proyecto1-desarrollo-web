using SWMatrc.Domain.Enums;

namespace SWMatrc.Domain.Riesgo;

/// <summary>
/// Configuración de fábrica de un tipo de sensor: unidad, rango físico y umbrales de
/// alerta recomendados. Permite dar de alta un sensor indicando solo su tipo, y sirve
/// como referencia de calibración cuando un administrador ajusta los valores a mano.
/// </summary>
/// <remarks>
/// Los umbrales siguen las escalas de aviso usadas por los servicios meteorológicos
/// nacionales: viento en la escala Beaufort, precipitación por intensidad horaria y
/// nivel de cauce respecto a la cota de desbordamiento.
/// </remarks>
public sealed record PlantillaSensor(
    TipoSensor Tipo,
    string Unidad,
    decimal ValorMinimo,
    decimal ValorMaximo,
    decimal ValorReposo,
    decimal VariacionMaxima,
    decimal? AmarilloAlto,
    decimal? NaranjaAlto,
    decimal? RojoAlto,
    decimal? AmarilloBajo,
    decimal? NaranjaBajo,
    decimal? RojoBajo)
{
    private static readonly Dictionary<TipoSensor, PlantillaSensor> Catalogo = new()
    {
        // Calor extremo por arriba; riesgo de helada por abajo.
        [TipoSensor.Temperatura] = new(TipoSensor.Temperatura, "°C", -10m, 50m, 24m, 0.8m,
            AmarilloAlto: 33m, NaranjaAlto: 38m, RojoAlto: 42m,
            AmarilloBajo: 5m, NaranjaBajo: 2m, RojoBajo: 0m),

        // Solo la humedad baja es relevante para el riesgo (sequía e incendio).
        [TipoSensor.Humedad] = new(TipoSensor.Humedad, "%", 0m, 100m, 62m, 2.5m,
            AmarilloAlto: null, NaranjaAlto: null, RojoAlto: null,
            AmarilloBajo: 35m, NaranjaBajo: 25m, RojoBajo: 15m),

        // Escala Beaufort: 39 km/h temporal, 62 km/h vendaval, 89 km/h temporal duro.
        [TipoSensor.Viento] = new(TipoSensor.Viento, "km/h", 0m, 150m, 12m, 3.5m,
            AmarilloAlto: 39m, NaranjaAlto: 62m, RojoAlto: 89m,
            AmarilloBajo: null, NaranjaBajo: null, RojoBajo: null),

        // Precipitación acumulada en la última hora.
        [TipoSensor.Lluvia] = new(TipoSensor.Lluvia, "mm", 0m, 120m, 1.5m, 2.0m,
            AmarilloAlto: 15m, NaranjaAlto: 30m, RojoAlto: 60m,
            AmarilloBajo: null, NaranjaBajo: null, RojoBajo: null),

        // Altura del cauce sobre el lecho; 4.5 m es la cota de desbordamiento de referencia.
        [TipoSensor.NivelRio] = new(TipoSensor.NivelRio, "m", 0m, 8m, 1.8m, 0.15m,
            AmarilloAlto: 3.0m, NaranjaAlto: 3.8m, RojoAlto: 4.5m,
            AmarilloBajo: null, NaranjaBajo: null, RojoBajo: null)
    };

    public static PlantillaSensor Para(TipoSensor tipo) =>
        Catalogo.TryGetValue(tipo, out var plantilla)
            ? plantilla
            : new PlantillaSensor(tipo, "", 0m, 100m, 50m, 1m, null, null, null, null, null, null);

    public static IReadOnlyCollection<PlantillaSensor> Todas => Catalogo.Values;
}
