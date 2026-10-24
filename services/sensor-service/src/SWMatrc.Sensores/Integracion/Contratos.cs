using System.Text.Json;
using SWMatrc.Sensores.Dominio;

namespace SWMatrc.Sensores.Integracion;

// ---------------------------------------------------------------------------
// Community Service — GET /internal/communities
// ---------------------------------------------------------------------------

/// <summary>Lo que Sensor necesita saber de una comunidad: su nombre (para mostrar) y si está activa.</summary>
public sealed record ComunidadInfo(int Id, string Nombre, bool Activa, decimal Latitud, decimal Longitud);

// ---------------------------------------------------------------------------
// Alert Service — POST /internal/evaluate
//
// Sensor manda, por comunidad, la fotografía de todos sus sensores con su último valor.
// Alert aplica reglas y umbrales, abre/escala/cierra alertas y devuelve:
//   - el nivel de cada sensor (para colorear lecturas y tarjetas), y
//   - los cambios de alertas, que Sensor difunde por SignalR sin interpretarlos.
// Una comunidad con Activa = false le indica a Alert que cierre lo que tenga abierto.
// ---------------------------------------------------------------------------

public sealed record SolicitudEvaluacion(IReadOnlyList<ComunidadEvaluacion> Comunidades);

public sealed record ComunidadEvaluacion(
    int ComunidadId,
    string ComunidadNombre,
    bool Activa,
    IReadOnlyList<SensorEvaluacion> Sensores);

/// <param name="ValorMinimo">Rango físico: la regla de sequía lo usa para saber si el cauce está en mínimos.</param>
/// <param name="UltimaLectura">Con varios sensores de la misma magnitud, Alert toma el más reciente.</param>
public sealed record SensorEvaluacion(
    int SensorId,
    string Codigo,
    string Nombre,
    TipoSensor Tipo,
    string UnidadMedida,
    EstadoSensor Estado,
    decimal Valor,
    decimal ValorMinimo,
    decimal ValorMaximo,
    DateTime? UltimaLectura);

public sealed record RespuestaEvaluacion(
    IReadOnlyList<NivelSensor> Niveles,
    IReadOnlyList<CambioAlerta> Cambios);

public sealed record NivelSensor(int SensorId, NivelAlerta Nivel);

/// <summary>
/// Un cambio que hay que avisar a los tableros. <see cref="Evento"/> es el nombre del
/// mensaje de SignalR (<c>AlertaGenerada</c>, <c>AlertaCerrada</c>); <see cref="Alerta"/>
/// viaja tal cual la redactó Alert Service.
/// </summary>
public sealed record CambioAlerta(string Evento, int ComunidadId, JsonElement Alerta);

// ---------------------------------------------------------------------------
// Alert Service — GET /internal/alerts/active?comunidadId=
// ---------------------------------------------------------------------------

public sealed record AlertasActivas(NivelAlerta NivelGlobal, IReadOnlyList<JsonElement> Alertas);

// ---------------------------------------------------------------------------
// Alert Service → Sensor — POST /internal/notify
//
// Cuando un usuario atiende o cierra una alerta en Alert Service, este avisa a Sensor para
// que los tableros conectados se enteren al instante.
// ---------------------------------------------------------------------------

public sealed record Notificacion(int? ComunidadId, string Evento, JsonElement Datos);
