using SWMatrc.Domain.Common;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Domain.Entities;

/// <summary>
/// Aviso emitido por el motor de riesgo. Una alerta permanece abierta mientras la
/// condición que la originó siga presente; al normalizarse se cierra con
/// <see cref="FechaCierre"/> en lugar de borrarse, para conservar la trazabilidad.
/// </summary>
public class Alerta : EntidadBase
{
    public int ComunidadId { get; set; }
    public Comunidad? Comunidad { get; set; }

    /// <summary>Sensor disparador. Es nulo en alertas compuestas por varias magnitudes.</summary>
    public int? SensorId { get; set; }
    public Sensor? Sensor { get; set; }

    public NivelAlerta Nivel { get; set; }

    public TipoFenomeno Fenomeno { get; set; }

    /// <summary>Texto orientado al operador: qué se detectó y qué se recomienda hacer.</summary>
    public string Mensaje { get; set; } = string.Empty;

    /// <summary>Valor medido que cruzó el umbral, conservado para la investigación posterior.</summary>
    public decimal? ValorDisparo { get; set; }

    public DateTime FechaHora { get; set; } = DateTime.UtcNow;

    /// <summary>Se llena cuando las condiciones vuelven a la normalidad.</summary>
    public DateTime? FechaCierre { get; set; }

    /// <summary>
    /// Evaluaciones consecutivas sin riesgo acumuladas desde la última vez que la
    /// condición se cumplió. Sostiene la histéresis del cierre: una medición que oscila
    /// alrededor del umbral no debe abrir y cerrar el aviso cada pocos segundos.
    /// </summary>
    public int CiclosSinRiesgo { get; set; }

    public bool Activa => FechaCierre is null;

    public EstadoAlerta Estado { get; set; } = EstadoAlerta.Activa;

    // --- Trazabilidad: qué regla levantó el aviso y contra qué umbral. ---

    /// <summary>Regla configurable que disparó la alerta; nulo si la emitió una regla integrada del motor.</summary>
    public int? ReglaAlertaId { get; set; }
    public ReglaAlerta? ReglaAlerta { get; set; }

    /// <summary>Nombre de la regla, copiado para que la alerta siga siendo legible si la regla se edita.</summary>
    public string ReglaNombre { get; set; } = string.Empty;

    public decimal? Umbral { get; set; }

    // --- Atención: deja constancia de que un humano vio la alerta y se hizo cargo. ---
    public bool Reconocida { get; set; }
    public int? ReconocidaPorUsuarioId { get; set; }
    public Usuario? ReconocidaPorUsuario { get; set; }
    public DateTime? FechaReconocimiento { get; set; }

    // --- Cierre manual. Si lo cerró el sistema al normalizarse, queda en nulo. ---
    public int? CerradaPorUsuarioId { get; set; }
    public Usuario? CerradaPorUsuario { get; set; }

    /// <summary>Marca la alerta como atendida por un usuario.</summary>
    public void Atender(int? usuarioId, DateTime ahora)
    {
        Reconocida = true;
        ReconocidaPorUsuarioId = usuarioId;
        FechaReconocimiento = ahora;
        Estado = EstadoAlerta.Atendida;
    }

    /// <summary>Cierra la alerta. Sin usuario significa que la cerró el sistema al normalizarse.</summary>
    public void Cerrar(int? usuarioId, DateTime ahora)
    {
        FechaCierre = ahora;
        CerradaPorUsuarioId = usuarioId;
        Estado = EstadoAlerta.Cerrada;
    }
}
