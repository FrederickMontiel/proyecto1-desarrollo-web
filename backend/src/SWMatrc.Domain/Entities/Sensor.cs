using SWMatrc.Domain.Common;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Domain.Entities;

/// <summary>
/// Estación de medición instalada en una comunidad. Además de identificar la magnitud
/// que mide, transporta sus propios umbrales de alerta: el motor de riesgo no lleva
/// valores fijos en el código, los lee de aquí, de modo que un administrador puede
/// recalibrar la red sin recompilar nada.
/// </summary>
public class Sensor : EntidadBase
{
    public int ComunidadId { get; set; }
    public Comunidad? Comunidad { get; set; }

    /// <summary>Código de inventario visible al operador, p. ej. "TEMP-01".</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;

    public TipoSensor Tipo { get; set; }

    /// <summary>Unidad en la que reporta el sensor: °C, %, km/h, mm, m.</summary>
    public string UnidadMedida { get; set; } = string.Empty;

    public EstadoSensor Estado { get; set; } = EstadoSensor.Activo;

    public decimal Latitud { get; set; }
    public decimal Longitud { get; set; }

    // --- Rango físico admisible. Acota la simulación y descarta lecturas imposibles. ---
    public decimal ValorMinimo { get; set; }
    public decimal ValorMaximo { get; set; }

    /// <summary>Amplitud máxima del paso de la caminata aleatoria en cada ciclo de simulación.</summary>
    public decimal VariacionMaxima { get; set; } = 1m;

    /// <summary>Última lectura conocida. Se desnormaliza aquí para que el dashboard no consulte el histórico.</summary>
    public decimal ValorActual { get; set; }

    public DateTime? UltimaLectura { get; set; }

    // --- Umbrales por exceso (el riesgo crece cuando el valor sube: crecida, viento, calor). ---
    public decimal? UmbralAmarilloAlto { get; set; }
    public decimal? UmbralNaranjaAlto { get; set; }
    public decimal? UmbralRojoAlto { get; set; }

    // --- Umbrales por defecto (el riesgo crece cuando el valor baja: helada, sequía). ---
    public decimal? UmbralAmarilloBajo { get; set; }
    public decimal? UmbralNaranjaBajo { get; set; }
    public decimal? UmbralRojoBajo { get; set; }

    public ICollection<Lectura> Lecturas { get; set; } = [];
    public ICollection<Alerta> Alertas { get; set; } = [];

    /// <summary>Un sensor solo alimenta el motor de riesgo mientras está en servicio.</summary>
    public bool EstaOperativo => Estado == EstadoSensor.Activo;

    /// <summary>
    /// Clasifica un valor contra los umbrales configurados y devuelve el nivel más severo
    /// que se cumpla. Evalúa de rojo a amarillo para quedarse con el peor caso.
    /// </summary>
    public NivelAlerta Clasificar(decimal valor)
    {
        if (UmbralRojoAlto is { } ra && valor >= ra) return NivelAlerta.Rojo;
        if (UmbralRojoBajo is { } rb && valor <= rb) return NivelAlerta.Rojo;
        if (UmbralNaranjaAlto is { } na && valor >= na) return NivelAlerta.Naranja;
        if (UmbralNaranjaBajo is { } nb && valor <= nb) return NivelAlerta.Naranja;
        if (UmbralAmarilloAlto is { } aa && valor >= aa) return NivelAlerta.Amarillo;
        if (UmbralAmarilloBajo is { } ab && valor <= ab) return NivelAlerta.Amarillo;
        return NivelAlerta.Verde;
    }
}
