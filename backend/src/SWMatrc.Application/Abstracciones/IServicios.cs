using SWMatrc.Application.Dtos;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Application.Abstracciones;

public interface IServicioBitacora
{
    /// <summary>
    /// Deja constancia de una acción del usuario en curso. No lanza: auditar nunca debe
    /// tumbar la operación que el usuario ya completó.
    /// </summary>
    Task RegistrarAsync(string accion, string entidad, object? entidadId = null, object? detalle = null, CancellationToken ct = default);

    /// <summary>
    /// Variante para acciones que ocurren fuera de un contexto autenticado. El inicio de
    /// sesión es el caso típico: cuando se registra, el token todavía no se ha usado y el
    /// contexto de la petición sigue siendo anónimo, así que hay que indicar el autor.
    /// </summary>
    Task RegistrarComoAsync(int usuarioId, string usuarioEmail, string accion, string entidad,
        object? entidadId = null, object? detalle = null, CancellationToken ct = default);

    Task<PaginaDto<BitacoraDto>> ListarAsync(int pagina, int tamano, string? filtroAccion, CancellationToken ct = default);
}

public interface IServicioAutenticacion
{
    Task<SesionDto> IniciarSesionAsync(LoginRequest request, CancellationToken ct = default);
    Task<UsuarioDto> RegistrarAsync(RegistroRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<UsuarioDto>> ListarUsuariosAsync(CancellationToken ct = default);
    Task<UsuarioDto> CambiarEstadoAsync(int usuarioId, bool activo, CancellationToken ct = default);
    Task<UsuarioDto> ActualizarAsync(int usuarioId, ActualizarUsuarioRequest request, CancellationToken ct = default);
    Task RestablecerPasswordAsync(int usuarioId, string passwordNueva, CancellationToken ct = default);
}

public interface IServicioSensores
{
    Task<IReadOnlyList<SensorDto>> ListarAsync(int? comunidadId, CancellationToken ct = default);
    Task<SensorDto> ObtenerAsync(int id, CancellationToken ct = default);
    Task<SensorDto> CrearAsync(CrearSensorRequest request, CancellationToken ct = default);
    Task<SensorDto> ActualizarAsync(int id, ActualizarSensorRequest request, CancellationToken ct = default);
    Task<SensorDto> CambiarEstadoAsync(int id, EstadoSensor estado, CancellationToken ct = default);
    Task<SensorDto> EstablecerValorAsync(int id, decimal valor, CancellationToken ct = default);
    Task EliminarAsync(int id, CancellationToken ct = default);
}

public interface IServicioMonitoreo
{
    /// <summary>
    /// Ciclo del simulador: genera una lectura por sensor operativo, la persiste,
    /// vuelve a evaluar el riesgo de cada comunidad y difunde los cambios.
    /// </summary>
    Task EjecutarCicloAsync(CancellationToken ct = default);

    /// <summary>Reevalúa el riesgo de una comunidad sin generar lecturas nuevas.</summary>
    Task EvaluarComunidadAsync(int comunidadId, CancellationToken ct = default);

    Task<EstadoComunidadDto> ObtenerEstadoAsync(int comunidadId, CancellationToken ct = default);
    Task<IReadOnlyList<ComunidadDto>> ListarComunidadesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SerieHistoricaDto>> ObtenerSeriesAsync(int comunidadId, int minutos, CancellationToken ct = default);
    Task<ResumenDashboardDto> ObtenerResumenAsync(int? comunidadId, CancellationToken ct = default);

    /// <summary>Cierra alertas, purga lecturas y devuelve los sensores a su valor de reposo.</summary>
    Task ReiniciarSistemaAsync(CancellationToken ct = default);
}

public interface IServicioAlertas
{
    Task<IReadOnlyList<AlertaDto>> ListarActivasAsync(int? comunidadId, CancellationToken ct = default);
    Task<PaginaDto<AlertaDto>> ListarAsync(int pagina, int tamano, int? comunidadId, NivelAlerta? nivel, CancellationToken ct = default);
    Task<AlertaDto> ReconocerAsync(int alertaId, CancellationToken ct = default);
}

public interface IServicioHistorial
{
    Task<PaginaDto<EventoDto>> ListarAsync(
        int pagina, int tamano, int? comunidadId, TipoFenomeno? fenomeno,
        DateTime? desde, DateTime? hasta, CancellationToken ct = default);
}
