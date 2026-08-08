using SWMatrc.Domain.Entities;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Domain.Riesgo;

/// <summary>
/// Fotografía de una comunidad en un instante dado. Agrupa los sensores operativos y
/// ofrece acceso por tipo de magnitud para que las reglas se lean como su enunciado
/// meteorológico y no como recorridos de colecciones.
/// </summary>
public sealed class ContextoEvaluacion
{
    private readonly Dictionary<TipoSensor, Sensor> _porTipo;

    public ContextoEvaluacion(Comunidad comunidad, IEnumerable<Sensor> sensores)
    {
        Comunidad = comunidad;
        Sensores = sensores.Where(s => s.EstaOperativo).ToList();

        // Si la comunidad tiene varios sensores de la misma magnitud se toma el más
        // reciente: es la lectura que mejor representa la situación actual.
        _porTipo = Sensores
            .GroupBy(s => s.Tipo)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.UltimaLectura ?? DateTime.MinValue).First());
    }

    public Comunidad Comunidad { get; }

    public IReadOnlyList<Sensor> Sensores { get; }

    public Sensor? Obtener(TipoSensor tipo) => _porTipo.GetValueOrDefault(tipo);

    /// <summary>Valor actual de una magnitud, o <c>null</c> si no hay sensor operativo que la mida.</summary>
    public decimal? Valor(TipoSensor tipo) => Obtener(tipo)?.ValorActual;
}
