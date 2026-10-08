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

    Task<PaginaDto<BitacoraDto>> ListarAsync(FiltroBitacora filtro, CancellationToken ct = default);

    /// <summary>Valores distintos de acción y entidad registrados, para poblar los filtros.</summary>
    Task<(IReadOnlyList<string> Acciones, IReadOnlyList<string> Entidades)> CatalogosAsync(CancellationToken ct = default);
}

public interface IServicioAutenticacion
{
    Task<SesionDto> IniciarSesionAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>
    /// Registra el cierre de sesión. El JWT es autocontenido y sin estado, así que el
    /// servidor no lo revoca: el cliente lo descarta y la bitácora deja constancia.
    /// </summary>
    Task CerrarSesionAsync(CancellationToken ct = default);

    Task<UsuarioDto> RegistrarAsync(RegistroRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<UsuarioDto>> ListarUsuariosAsync(FiltroUsuarios? filtro = null, CancellationToken ct = default);
    Task<UsuarioDto> CambiarEstadoAsync(int usuarioId, bool activo, CancellationToken ct = default);
    Task<UsuarioDto> ActualizarAsync(int usuarioId, ActualizarUsuarioRequest request, CancellationToken ct = default);
    Task RestablecerPasswordAsync(int usuarioId, string passwordNueva, CancellationToken ct = default);
}

public interface IServicioComunidades
{
    Task<IReadOnlyList<ComunidadDto>> ListarAsync(FiltroComunidades filtro, CancellationToken ct = default);
    Task<ComunidadDto> ObtenerAsync(int id, CancellationToken ct = default);
    Task<ComunidadDto> CrearAsync(GuardarComunidadRequest request, CancellationToken ct = default);
    Task<ComunidadDto> ActualizarAsync(int id, GuardarComunidadRequest request, CancellationToken ct = default);
    Task<ComunidadDto> CambiarEstadoAsync(int id, bool activa, CancellationToken ct = default);
}

public interface IServicioSensores
{
    Task<IReadOnlyList<SensorDto>> ListarAsync(FiltroSensores filtro, CancellationToken ct = default);
    Task<SensorDto> ObtenerAsync(int id, CancellationToken ct = default);
    Task<SensorDto> CrearAsync(CrearSensorRequest request, CancellationToken ct = default);
    Task<SensorDto> ActualizarAsync(int id, ActualizarSensorRequest request, CancellationToken ct = default);
    Task<SensorDto> CambiarEstadoAsync(int id, EstadoSensor estado, CancellationToken ct = default);
    Task<SensorDto> EstablecerValorAsync(int id, decimal valor, CancellationToken ct = default);
    Task EliminarAsync(int id, CancellationToken ct = default);
}

public interface IServicioReglas
{
    Task<IReadOnlyList<ReglaAlertaDto>> ListarAsync(FiltroReglas filtro, CancellationToken ct = default);
    Task<ReglaAlertaDto> ObtenerAsync(int id, CancellationToken ct = default);
    Task<ReglaAlertaDto> CrearAsync(GuardarReglaRequest request, CancellationToken ct = default);
    Task<ReglaAlertaDto> ActualizarAsync(int id, GuardarReglaRequest request, CancellationToken ct = default);
    Task<ReglaAlertaDto> CambiarEstadoAsync(int id, bool activa, CancellationToken ct = default);
    Task EliminarAsync(int id, CancellationToken ct = default);
}

public interface IServicioLecturas
{
    Task<PaginaDto<LecturaHistoricaDto>> ListarAsync(FiltroLecturas filtro, CancellationToken ct = default);

    /// <summary>Registro manual: pasa por el mismo camino que una lectura del simulador.</summary>
    Task<LecturaHistoricaDto> RegistrarAsync(RegistrarLecturaRequest request, CancellationToken ct = default);

    Task EliminarAsync(long id, CancellationToken ct = default);
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
    Task<PaginaDto<AlertaDto>> ListarAsync(FiltroAlertas filtro, CancellationToken ct = default);
    Task<AlertaDto> ObtenerAsync(int alertaId, CancellationToken ct = default);

    /// <summary>El responsable se hace cargo de la alerta: pasa de Activa a Atendida.</summary>
    Task<AlertaDto> AtenderAsync(int alertaId, string? comentario = null, CancellationToken ct = default);

    /// <summary>Cierre manual. Deja constancia de quién la cerró.</summary>
    Task<AlertaDto> CerrarAsync(int alertaId, string? comentario = null, CancellationToken ct = default);
}

public interface IServicioHistorial
{
    Task<PaginaDto<EventoDto>> ListarAsync(FiltroHistorial filtro, CancellationToken ct = default);
    Task<EstadisticasHistorialDto> EstadisticasAsync(FiltroHistorial filtro, CancellationToken ct = default);
}
