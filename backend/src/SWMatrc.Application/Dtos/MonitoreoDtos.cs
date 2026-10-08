using SWMatrc.Domain.Entities;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Application.Dtos;

/// <summary>Muestra difundida por el canal en tiempo real cada vez que un sensor reporta.</summary>
public record LecturaDto
{
    public long Id { get; init; }
    public int SensorId { get; init; }
    public string SensorCodigo { get; init; } = string.Empty;
    public int ComunidadId { get; init; }
    public TipoSensor Tipo { get; init; }
    public decimal Valor { get; init; }
    public string UnidadMedida { get; init; } = string.Empty;
    public NivelAlerta Nivel { get; init; }
    public DateTime FechaHora { get; init; }

    public static LecturaDto Desde(Lectura l, Sensor s) => new()
    {
        Id = l.Id,
        SensorId = s.Id,
        SensorCodigo = s.Codigo,
        ComunidadId = s.ComunidadId,
        Tipo = s.Tipo,
        Valor = l.Valor,
        UnidadMedida = string.IsNullOrEmpty(l.UnidadMedida) ? s.UnidadMedida : l.UnidadMedida,
        Nivel = s.Clasificar(l.Valor),
        FechaHora = l.FechaHora
    };
}

/// <summary>Alerta tal como la consume el frontend.</summary>
public record AlertaDto
{
    public int Id { get; init; }
    public int ComunidadId { get; init; }
    public string ComunidadNombre { get; init; } = string.Empty;
    public int? SensorId { get; init; }
    public string? SensorCodigo { get; init; }
    public string? SensorNombre { get; init; }
    public NivelAlerta Nivel { get; init; }
    public string NivelNombre { get; init; } = string.Empty;
    public TipoFenomeno Fenomeno { get; init; }
    public string FenomenoNombre { get; init; } = string.Empty;
    public string Mensaje { get; init; } = string.Empty;
    public decimal? ValorDisparo { get; init; }
    public decimal? Umbral { get; init; }
    public string? UnidadMedida { get; init; }
    public int? ReglaId { get; init; }
    public string ReglaNombre { get; init; } = string.Empty;
    public EstadoAlerta Estado { get; init; }
    public DateTime FechaHora { get; init; }
    public DateTime? FechaCierre { get; init; }
    public bool Activa { get; init; }
    public bool Reconocida { get; init; }
    public string? ReconocidaPor { get; init; }
    public DateTime? FechaReconocimiento { get; init; }
    public string? CerradaPor { get; init; }

    public static AlertaDto Desde(Alerta a) => new()
    {
        Id = a.Id,
        ComunidadId = a.ComunidadId,
        ComunidadNombre = a.Comunidad?.Nombre ?? string.Empty,
        SensorId = a.SensorId,
        SensorCodigo = a.Sensor?.Codigo,
        SensorNombre = a.Sensor?.Nombre,
        Nivel = a.Nivel,
        NivelNombre = a.Nivel.ToString(),
        Fenomeno = a.Fenomeno,
        FenomenoNombre = a.Fenomeno.ToString(),
        Mensaje = a.Mensaje,
        ValorDisparo = a.ValorDisparo,
        Umbral = a.Umbral,
        UnidadMedida = a.Sensor?.UnidadMedida,
        ReglaId = a.ReglaAlertaId,
        ReglaNombre = a.ReglaNombre,
        Estado = a.Estado,
        FechaHora = a.FechaHora,
        FechaCierre = a.FechaCierre,
        Activa = a.Activa,
        Reconocida = a.Reconocida,
        ReconocidaPor = a.ReconocidaPorUsuario?.NombreCompleto,
        FechaReconocimiento = a.FechaReconocimiento,
        CerradaPor = a.CerradaPorUsuario?.NombreCompleto
    };
}

/// <summary>Asiento del historial de incidentes.</summary>
public record EventoDto
{
    public int Id { get; init; }
    public int ComunidadId { get; init; }
    public string ComunidadNombre { get; init; } = string.Empty;
    public TipoFenomeno Fenomeno { get; init; }
    public string FenomenoNombre { get; init; } = string.Empty;
    public NivelAlerta NivelMaximo { get; init; }
    public string NivelNombre { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    public string OrigenSensor { get; init; } = string.Empty;
    public int? SensorId { get; init; }
    public decimal? ValorRegistrado { get; init; }
    public EstadoAlerta Estado { get; init; }
    public string? UsuarioResponsable { get; init; }
    public DateTime FechaInicio { get; init; }
    public DateTime? FechaFin { get; init; }
    public double? DuracionMinutos { get; init; }

    public static EventoDto Desde(EventoHistorial e) => new()
    {
        Id = e.Id,
        ComunidadId = e.ComunidadId,
        ComunidadNombre = e.Comunidad?.Nombre ?? string.Empty,
        Fenomeno = e.Fenomeno,
        FenomenoNombre = e.Fenomeno.ToString(),
        NivelMaximo = e.NivelMaximo,
        NivelNombre = e.NivelMaximo.ToString(),
        Descripcion = e.Descripcion,
        OrigenSensor = e.OrigenSensor,
        SensorId = e.SensorId,
        ValorRegistrado = e.ValorRegistrado,
        Estado = e.Estado,
        UsuarioResponsable = e.UsuarioResponsable?.NombreCompleto,
        FechaInicio = e.FechaInicio,
        FechaFin = e.FechaFin,
        DuracionMinutos = e.Duracion?.TotalMinutes
    };
}

/// <summary>Instantánea completa del tablero de una comunidad.</summary>
public record EstadoComunidadDto
{
    public int ComunidadId { get; init; }
    public string ComunidadNombre { get; init; } = string.Empty;
    public NivelAlerta NivelGlobal { get; init; }
    public string NivelGlobalNombre { get; init; } = string.Empty;
    public IReadOnlyList<SensorDto> Sensores { get; init; } = [];
    public IReadOnlyList<AlertaDto> AlertasActivas { get; init; } = [];
    public DateTime Marca { get; init; } = DateTime.UtcNow;
}

/// <summary>Serie temporal de un sensor para los gráficos de evolución.</summary>
public record SerieHistoricaDto
{
    public int SensorId { get; init; }
    public string SensorCodigo { get; init; } = string.Empty;
    public string SensorNombre { get; init; } = string.Empty;
    public TipoSensor Tipo { get; init; }
    public string UnidadMedida { get; init; } = string.Empty;
    public IReadOnlyList<PuntoSerieDto> Puntos { get; init; } = [];
}

public record PuntoSerieDto(DateTime FechaHora, decimal Valor);

/// <summary>Indicadores agregados que encabezan el dashboard.</summary>
public record ResumenDashboardDto
{
    public int TotalComunidades { get; init; }
    public int TotalSensores { get; init; }
    public int SensoresActivos { get; init; }

    /// <summary>Sensores apagados a propósito por un administrador u operador.</summary>
    public int SensoresInactivos { get; init; }

    /// <summary>
    /// Sensores en servicio que han dejado de reportar. Se expone aparte de los inactivos
    /// porque significan algo distinto: no es que estén apagados, es que el sistema ya no
    /// sabe qué ocurre en ese punto de la comunidad.
    /// </summary>
    public int SensoresSinSenal { get; init; }
    public int AlertasActivas { get; init; }
    public int EventosUltimas24h { get; init; }
    public NivelAlerta NivelGlobal { get; init; }
    public IReadOnlyDictionary<string, int> AlertasPorNivel { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> EventosPorFenomeno { get; init; } = new Dictionary<string, int>();
}
