using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Comun;
using SWMatrc.Application.Dtos;
using SWMatrc.Domain.Entities;
using SWMatrc.Domain.Enums;
using SWMatrc.Domain.Riesgo;

namespace SWMatrc.Application.Servicios;

/// <summary>
/// Núcleo del sistema de alerta temprana. Enlaza las tres etapas del ciclo: adquirir la
/// medición, evaluarla contra las reglas de riesgo y difundir el resultado a los
/// clientes conectados.
/// </summary>
public sealed class ServicioMonitoreo(
    ISwmatrcDbContext db,
    IMotorRiesgo motor,
    ISimuladorClima simulador,
    INotificadorTiempoReal notificador,
    IServicioBitacora bitacora,
    IOptions<OpcionesMonitoreo> opciones,
    ILogger<ServicioMonitoreo> logger) : IServicioMonitoreo
{
    private readonly OpcionesMonitoreo _opciones = opciones.Value;

    /// <summary>
    /// Evaluaciones consecutivas en normalidad que debe acumular una alerta antes de
    /// cerrarse. Con el ciclo por omisión equivale a unos quince segundos: suficiente
    /// para distinguir una mejora real del ruido de la medición en torno al umbral.
    /// </summary>
    private const int CiclosParaCierre = 5;

    public async Task EjecutarCicloAsync(CancellationToken ct = default)
    {
        // Se incluyen los sensores sin señal: siguen en servicio y hay que darles la
        // oportunidad de volver a reportar. Los desactivados a mano quedan fuera, igual
        // que los de comunidades desactivadas.
        var sensores = await db.Sensores
            .Include(s => s.Comunidad)
            .Where(s => s.Estado == EstadoSensor.Activo || s.Estado == EstadoSensor.SinSenal)
            .Where(s => s.Comunidad!.Activa)
            .ToListAsync(ct);

        if (sensores.Count == 0)
            return;

        var ahora = DateTime.UtcNow;
        var lecturas = new List<(Lectura Lectura, Sensor Sensor)>(sensores.Count);
        var cambiosDeEstado = new List<Sensor>();

        foreach (var sensor in sensores)
        {
            var valor = simulador.SiguienteValor(sensor);

            if (valor is null)
            {
                // El instrumento no respondió. No se inventa una lectura: si el silencio se
                // prolonga, el sensor pasa a "sin señal" y deja de alimentar el motor de riesgo.
                if (MarcarSinSenalSiProcede(sensor, ahora))
                    cambiosDeEstado.Add(sensor);

                continue;
            }

            if (sensor.Estado == EstadoSensor.SinSenal)
            {
                sensor.Estado = EstadoSensor.Activo;
                cambiosDeEstado.Add(sensor);
                logger.LogInformation("El sensor {Codigo} recuperó la señal.", sensor.Codigo);
            }

            var lectura = new Lectura
            {
                SensorId = sensor.Id,
                Valor = valor.Value,
                UnidadMedida = sensor.UnidadMedida,
                EstadoSensor = sensor.Estado,
                FechaHora = ahora
            };
            db.Lecturas.Add(lectura);

            // La última medición se copia al sensor para que el dashboard y el motor de
            // riesgo trabajen sin recorrer el histórico en cada ciclo.
            sensor.ValorActual = valor.Value;
            sensor.UltimaLectura = ahora;

            lecturas.Add((lectura, sensor));
        }

        // Un único guardado por ciclo: las lecturas se insertan en lote.
        await db.SaveChangesAsync(ct);

        foreach (var (lectura, sensor) in lecturas)
            await notificador.LecturaRecibidaAsync(LecturaDto.Desde(lectura, sensor), ct);

        foreach (var sensor in cambiosDeEstado)
            await notificador.EstadoSensorCambiadoAsync(SensorDto.Desde(sensor), ct);

        foreach (var comunidadId in sensores.Select(s => s.ComunidadId).Distinct())
            await EvaluarComunidadAsync(comunidadId, ct);
    }

    /// <summary>
    /// Declara un sensor sin señal cuando lleva demasiado tiempo sin reportar.
    /// </summary>
    /// <remarks>
    /// La distinción importa: un sensor <i>inactivo</i> lo apagó alguien a propósito, uno
    /// <i>sin señal</i> está averiado. Mostrar el segundo como si todo fuera bien, con su
    /// última lectura congelada, daría una falsa sensación de normalidad justo donde el
    /// sistema ya no sabe lo que ocurre.
    /// </remarks>
    /// <returns><c>true</c> si el estado cambió en esta llamada.</returns>
    private bool MarcarSinSenalSiProcede(Sensor sensor, DateTime ahora)
    {
        if (sensor.Estado == EstadoSensor.SinSenal)
            return false;

        var silencio = ahora - (sensor.UltimaLectura ?? sensor.FechaCreacion);
        if (silencio < _opciones.ToleranciaSinSenal)
            return false;

        sensor.Estado = EstadoSensor.SinSenal;
        sensor.FechaModificacion = ahora;

        logger.LogWarning("El sensor {Codigo} lleva {Segundos:0} s sin reportar: se marca sin señal.",
            sensor.Codigo, silencio.TotalSeconds);

        return true;
    }

    public async Task EvaluarComunidadAsync(int comunidadId, CancellationToken ct = default)
    {
        var comunidad = await db.Comunidades
            .Include(c => c.Sensores)
            .FirstOrDefaultAsync(c => c.Id == comunidadId, ct);

        if (comunidad is null)
            return;

        var alertasAbiertas = await db.Alertas
            .Include(a => a.Sensor)
            .Include(a => a.Comunidad)
            .Where(a => a.ComunidadId == comunidadId && a.FechaCierre == null)
            .ToListAsync(ct);

        if (!comunidad.Activa)
        {
            // Una comunidad desactivada deja de monitorearse: sus avisos abiertos ya no
            // describen nada vigente y se cierran sin esperar la histéresis.
            await CerrarSinMonitoreoAsync(comunidad, alertasAbiertas, ct);
            return;
        }

        var diagnosticos = await DiagnosticarAsync(comunidad, ct);
        await ConciliarAsync(comunidad, diagnosticos, alertasAbiertas, ct);
    }

    /// <summary>
    /// Combina las reglas integradas del motor con las reglas configurables del panel. Se
    /// conserva un diagnóstico por fenómeno —el más severo— porque la conciliación abre
    /// una sola alerta por fenómeno. Ante empate de nivel gana la regla configurada, que es
    /// la que el administrador definió de forma explícita, y entre varias configuradas la
    /// más específica: la de umbral más cercano al valor medido (con 450 µg/m³, "≥ 400"
    /// describe mejor la situación que "≥ 250").
    /// </summary>
    private async Task<IReadOnlyList<DiagnosticoRiesgo>> DiagnosticarAsync(Comunidad comunidad, CancellationToken ct)
    {
        var integrados = motor.Evaluar(new ContextoEvaluacion(comunidad, comunidad.Sensores));

        var reglas = await db.ReglasAlerta.AsNoTracking().Where(r => r.Activa).ToListAsync(ct);
        var configurados = new List<DiagnosticoRiesgo>();

        foreach (var sensor in comunidad.Sensores.Where(s => s.EstaOperativo))
        {
            sensor.Comunidad ??= comunidad;

            foreach (var regla in reglas.Where(r => r.TipoSensor == sensor.Tipo && r.SeCumple(sensor.ValorActual)))
            {
                configurados.Add(new DiagnosticoRiesgo(
                    regla.Fenomeno, regla.Nivel, regla.RedactarMensaje(sensor, sensor.ValorActual),
                    sensor.Id, sensor.ValorActual, regla.Id, regla.Nombre, regla.UmbralReferencia));
            }
        }

        return integrados
            .Concat(configurados)
            .GroupBy(d => d.Fenomeno)
            .Select(g => g
                .OrderByDescending(d => d.Nivel)
                .ThenByDescending(d => d.ReglaId.HasValue)
                .ThenBy(DistanciaAlUmbral)
                .ThenBy(d => d.ReglaId)
                .First())
            .OrderByDescending(d => d.Nivel)
            .ToList();
    }

    private static decimal DistanciaAlUmbral(DiagnosticoRiesgo d) =>
        d is { Umbral: { } umbral, ValorDisparo: { } valor } ? Math.Abs(valor - umbral) : decimal.MaxValue;

    private async Task CerrarSinMonitoreoAsync(Comunidad comunidad, List<Alerta> abiertas, CancellationToken ct)
    {
        if (abiertas.Count == 0)
            return;

        var ahora = DateTime.UtcNow;
        foreach (var alerta in abiertas)
            alerta.Cerrar(null, ahora);

        await db.SaveChangesAsync(ct);
        await CerrarEventosAsync(abiertas, ahora, ct);
        await db.SaveChangesAsync(ct);

        foreach (var alerta in abiertas)
        {
            alerta.Comunidad ??= comunidad;
            await notificador.AlertaCerradaAsync(AlertaDto.Desde(alerta), ct);
        }
    }

    /// <summary>
    /// Ajusta las alertas abiertas al diagnóstico vigente. Una alerta se levanta una sola
    /// vez por episodio y se mantiene mientras la condición persista: así el operador ve
    /// un aviso por fenómeno y no una avalancha de repeticiones cada pocos segundos.
    /// </summary>
    private async Task ConciliarAsync(
        Comunidad comunidad,
        IReadOnlyList<DiagnosticoRiesgo> diagnosticos,
        List<Alerta> alertasAbiertas,
        CancellationToken ct)
    {
        var ahora = DateTime.UtcNow;
        var nuevas = new List<Alerta>();
        var escaladas = new List<Alerta>();
        var cerradas = new List<Alerta>();

        foreach (var diagnostico in diagnosticos)
        {
            var abierta = alertasAbiertas.FirstOrDefault(a => a.Fenomeno == diagnostico.Fenomeno);

            if (abierta is null)
            {
                var alerta = new Alerta
                {
                    ComunidadId = comunidad.Id,
                    SensorId = diagnostico.SensorId,
                    Nivel = diagnostico.Nivel,
                    Fenomeno = diagnostico.Fenomeno,
                    Mensaje = diagnostico.Mensaje,
                    ValorDisparo = diagnostico.ValorDisparo,
                    ReglaAlertaId = diagnostico.ReglaId,
                    ReglaNombre = diagnostico.ReglaNombre ?? "Regla integrada",
                    Umbral = UmbralDe(comunidad, diagnostico),
                    Estado = EstadoAlerta.Activa,
                    FechaHora = ahora
                };

                db.Alertas.Add(alerta);
                nuevas.Add(alerta);
                continue;
            }

            if (diagnostico.Nivel > abierta.Nivel)
            {
                // El episodio empeoró: se vuelve a avisar y se retira el acuse anterior,
                // porque lo que el operador reconoció ya no describe la situación.
                abierta.Nivel = diagnostico.Nivel;
                abierta.Reconocida = false;
                abierta.ReconocidaPorUsuarioId = null;
                abierta.FechaReconocimiento = null;
                abierta.Estado = EstadoAlerta.Activa;
                escaladas.Add(abierta);
            }

            // Aunque no cambie el color, el texto, el valor y la regla reflejan la medición más reciente.
            abierta.Mensaje = diagnostico.Mensaje;
            abierta.ValorDisparo = diagnostico.ValorDisparo;
            abierta.SensorId = diagnostico.SensorId ?? abierta.SensorId;
            abierta.ReglaAlertaId = diagnostico.ReglaId;
            abierta.ReglaNombre = diagnostico.ReglaNombre ?? abierta.ReglaNombre;
            abierta.Umbral = UmbralDe(comunidad, diagnostico) ?? abierta.Umbral;
            abierta.FechaModificacion = ahora;

            // La condición sigue presente: se reinicia la cuenta atrás para el cierre.
            abierta.CiclosSinRiesgo = 0;
        }

        // Cierre con histéresis. Una alerta no se retira en cuanto la lectura baja del
        // umbral: debe mantenerse en normalidad varias evaluaciones seguidas. Sin esto,
        // un valor oscilando en torno al umbral produciría una ráfaga de aperturas y
        // cierres —y otros tantos asientos en el historial— por puro ruido de medición.
        var fenomenosVigentes = diagnosticos.Select(d => d.Fenomeno).ToHashSet();
        foreach (var alerta in alertasAbiertas.Where(a => !fenomenosVigentes.Contains(a.Fenomeno)))
        {
            alerta.CiclosSinRiesgo++;
            alerta.FechaModificacion = ahora;

            if (alerta.CiclosSinRiesgo < CiclosParaCierre)
                continue;

            alerta.Cerrar(null, ahora);
            cerradas.Add(alerta);
        }

        if (nuevas.Count == 0 && escaladas.Count == 0 && cerradas.Count == 0)
        {
            // Solo hubo refresco de textos: se persiste sin notificar para no saturar el canal.
            await db.SaveChangesAsync(ct);
            return;
        }

        await db.SaveChangesAsync(ct);

        AbrirEventos(comunidad, nuevas);
        await ActualizarEventosAsync(escaladas, ct);
        await CerrarEventosAsync(cerradas, ahora, ct);
        await db.SaveChangesAsync(ct);

        foreach (var alerta in nuevas.Concat(escaladas))
        {
            alerta.Comunidad ??= comunidad;
            alerta.Sensor ??= comunidad.Sensores.FirstOrDefault(s => s.Id == alerta.SensorId);
            await notificador.AlertaGeneradaAsync(AlertaDto.Desde(alerta), ct);

            logger.LogWarning("Alerta {Nivel} por {Fenomeno} en {Comunidad}: {Mensaje}",
                alerta.Nivel, alerta.Fenomeno, comunidad.Nombre, alerta.Mensaje);
        }

        foreach (var alerta in cerradas)
        {
            alerta.Comunidad ??= comunidad;
            await notificador.AlertaCerradaAsync(AlertaDto.Desde(alerta), ct);
        }
    }

    /// <summary>Abre el asiento de historial que acompaña a cada alerta nueva.</summary>
    private void AbrirEventos(Comunidad comunidad, List<Alerta> nuevas)
    {
        if (nuevas.Count == 0) return;

        foreach (var alerta in nuevas)
        {
            var sensor = comunidad.Sensores.FirstOrDefault(s => s.Id == alerta.SensorId);

            db.Eventos.Add(new EventoHistorial
            {
                ComunidadId = comunidad.Id,
                AlertaId = alerta.Id,
                Fenomeno = alerta.Fenomeno,
                NivelMaximo = alerta.Nivel,
                Descripcion = alerta.Mensaje,
                // Se copia el nombre del sensor: el historial debe seguir siendo legible
                // aunque más adelante el sensor se dé de baja.
                OrigenSensor = sensor is null ? "Evaluación combinada" : $"{sensor.Codigo} — {sensor.Nombre}",
                SensorId = sensor?.Id,
                ValorRegistrado = alerta.ValorDisparo,
                Estado = EstadoAlerta.Activa,
                FechaInicio = alerta.FechaHora
            });
        }
    }

    /// <summary>Eleva el nivel máximo registrado en el historial cuando un episodio se agrava.</summary>
    private async Task ActualizarEventosAsync(List<Alerta> escaladas, CancellationToken ct)
    {
        if (escaladas.Count == 0) return;

        var ids = escaladas.Select(a => a.Id).ToList();
        var eventos = await db.Eventos
            .Where(e => e.AlertaId != null && ids.Contains(e.AlertaId!.Value) && e.FechaFin == null)
            .ToListAsync(ct);

        foreach (var evento in eventos)
        {
            var alerta = escaladas.First(a => a.Id == evento.AlertaId);
            if (alerta.Nivel > evento.NivelMaximo)
                evento.NivelMaximo = alerta.Nivel;

            evento.Descripcion = alerta.Mensaje;
            evento.ValorRegistrado = alerta.ValorDisparo;
            evento.Estado = EstadoAlerta.Activa;
            evento.FechaModificacion = DateTime.UtcNow;
        }
    }

    /// <summary>Sella los asientos de historial de los episodios que volvieron a la normalidad.</summary>
    private async Task CerrarEventosAsync(List<Alerta> cerradas, DateTime ahora, CancellationToken ct)
    {
        if (cerradas.Count == 0) return;

        var ids = cerradas.Select(a => a.Id).ToList();
        var eventos = await db.Eventos
            .Where(e => e.AlertaId != null && ids.Contains(e.AlertaId!.Value) && e.FechaFin == null)
            .ToListAsync(ct);

        foreach (var evento in eventos)
        {
            evento.FechaFin = ahora;
            evento.Estado = EstadoAlerta.Cerrada;
            evento.FechaModificacion = ahora;
        }
    }

    /// <summary>
    /// Umbral que se reporta en la alerta. Las reglas configurables lo traen consigo; para
    /// las integradas se toma el umbral del sensor que corresponde a la lectura.
    /// </summary>
    private static decimal? UmbralDe(Comunidad comunidad, DiagnosticoRiesgo diagnostico)
    {
        if (diagnostico.Umbral is { } umbral)
            return umbral;

        var sensor = comunidad.Sensores.FirstOrDefault(s => s.Id == diagnostico.SensorId);
        if (sensor is null || diagnostico.ValorDisparo is not { } valor)
            return null;

        return sensor.UmbralPara(sensor.Clasificar(valor), valor);
    }

    public async Task<EstadoComunidadDto> ObtenerEstadoAsync(int comunidadId, CancellationToken ct = default)
    {
        var comunidad = await db.Comunidades
            .AsNoTracking()
            .Include(c => c.Sensores)
            .FirstOrDefaultAsync(c => c.Id == comunidadId, ct)
            ?? throw new ExcepcionNoEncontrado("la comunidad", comunidadId);

        var alertas = await db.Alertas
            .AsNoTracking()
            .Include(a => a.Sensor)
            .Include(a => a.ReconocidaPorUsuario)
            .Include(a => a.CerradaPorUsuario)
            .Where(a => a.ComunidadId == comunidadId && a.FechaCierre == null)
            .OrderByDescending(a => a.Nivel)
            .ThenByDescending(a => a.FechaHora)
            .ToListAsync(ct);

        foreach (var sensor in comunidad.Sensores)
            sensor.Comunidad = comunidad;

        var nivelGlobal = alertas.Count == 0 ? NivelAlerta.Verde : alertas.Max(a => a.Nivel);

        return new EstadoComunidadDto
        {
            ComunidadId = comunidad.Id,
            ComunidadNombre = comunidad.Nombre,
            NivelGlobal = nivelGlobal,
            NivelGlobalNombre = nivelGlobal.ToString(),
            Sensores = comunidad.Sensores.OrderBy(s => s.Tipo).Select(SensorDto.Desde).ToList(),
            AlertasActivas = alertas.Select(a =>
            {
                a.Comunidad = comunidad;
                return AlertaDto.Desde(a);
            }).ToList(),
            Marca = DateTime.UtcNow
        };
    }

    public async Task<IReadOnlyList<ComunidadDto>> ListarComunidadesAsync(CancellationToken ct = default)
    {
        var comunidades = await db.Comunidades
            .AsNoTracking()
            .Include(c => c.Sensores)
            .OrderBy(c => c.Nombre)
            .ToListAsync(ct);

        return comunidades.Select(ComunidadDto.Desde).ToList();
    }

    public async Task<IReadOnlyList<SerieHistoricaDto>> ObtenerSeriesAsync(
        int comunidadId, int minutos, CancellationToken ct = default)
    {
        minutos = Math.Clamp(minutos, 1, 1440);
        var desde = DateTime.UtcNow.AddMinutes(-minutos);

        var sensores = await db.Sensores
            .AsNoTracking()
            .Where(s => s.ComunidadId == comunidadId)
            .OrderBy(s => s.Tipo)
            .ToListAsync(ct);

        var idsSensores = sensores.Select(s => s.Id).ToList();

        var lecturas = await db.Lecturas
            .AsNoTracking()
            .Where(l => idsSensores.Contains(l.SensorId) && l.FechaHora >= desde)
            .OrderBy(l => l.FechaHora)
            .ToListAsync(ct);

        var porSensor = lecturas.ToLookup(l => l.SensorId);

        return sensores.Select(s => new SerieHistoricaDto
        {
            SensorId = s.Id,
            SensorCodigo = s.Codigo,
            SensorNombre = s.Nombre,
            Tipo = s.Tipo,
            UnidadMedida = s.UnidadMedida,
            Puntos = porSensor[s.Id].Select(l => new PuntoSerieDto(l.FechaHora, l.Valor)).ToList()
        }).ToList();
    }

    public async Task<ResumenDashboardDto> ObtenerResumenAsync(int? comunidadId, CancellationToken ct = default)
    {
        var comunidades = db.Comunidades.AsNoTracking().AsQueryable();
        var sensores = db.Sensores.AsNoTracking().AsQueryable();
        var alertas = db.Alertas.AsNoTracking().AsQueryable();
        var eventos = db.Eventos.AsNoTracking().AsQueryable();

        if (comunidadId is { } id)
        {
            comunidades = comunidades.Where(c => c.Id == id);
            sensores = sensores.Where(s => s.ComunidadId == id);
            alertas = alertas.Where(a => a.ComunidadId == id);
            eventos = eventos.Where(e => e.ComunidadId == id);
        }

        var desde24h = DateTime.UtcNow.AddHours(-24);

        var activas = await alertas.Where(a => a.FechaCierre == null).ToListAsync(ct);
        var recientes = await eventos.Where(e => e.FechaInicio >= desde24h).ToListAsync(ct);

        return new ResumenDashboardDto
        {
            TotalComunidades = await comunidades.CountAsync(ct),
            TotalSensores = await sensores.CountAsync(ct),
            SensoresActivos = await sensores.CountAsync(s => s.Estado == EstadoSensor.Activo, ct),
            SensoresInactivos = await sensores.CountAsync(s => s.Estado == EstadoSensor.Inactivo, ct),
            SensoresSinSenal = await sensores.CountAsync(s => s.Estado == EstadoSensor.SinSenal, ct),
            AlertasActivas = activas.Count,
            EventosUltimas24h = recientes.Count,
            NivelGlobal = activas.Count == 0 ? NivelAlerta.Verde : activas.Max(a => a.Nivel),
            AlertasPorNivel = activas
                .GroupBy(a => a.Nivel.ToString())
                .ToDictionary(g => g.Key, g => g.Count()),
            EventosPorFenomeno = recientes
                .GroupBy(e => e.Fenomeno.ToString())
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }

    public async Task ReiniciarSistemaAsync(CancellationToken ct = default)
    {
        var ahora = DateTime.UtcNow;

        // Las alertas abiertas se cierran en lugar de borrarse: el historial de incidentes
        // es evidencia y debe sobrevivir a un reinicio operativo.
        var abiertas = await db.Alertas.Where(a => a.FechaCierre == null).ToListAsync(ct);
        foreach (var alerta in abiertas)
        {
            alerta.Cerrar(null, ahora);
            alerta.FechaModificacion = ahora;
        }

        var eventosAbiertos = await db.Eventos.Where(e => e.FechaFin == null).ToListAsync(ct);
        foreach (var evento in eventosAbiertos)
        {
            evento.FechaFin = ahora;
            evento.Estado = EstadoAlerta.Cerrada;
            evento.FechaModificacion = ahora;
        }

        // Las lecturas sí se descartan: son datos simulados y sin ellas los gráficos
        // arrancan limpios, que es justamente lo que se espera de un reinicio.
        await db.Lecturas.ExecuteDeleteAsync(ct);

        var sensores = await db.Sensores.ToListAsync(ct);
        foreach (var sensor in sensores)
        {
            sensor.ValorActual = PlantillaSensor.Para(sensor.Tipo).ValorReposo;
            sensor.Estado = EstadoSensor.Activo;
            sensor.UltimaLectura = ahora;
            sensor.FechaModificacion = ahora;
        }

        await db.SaveChangesAsync(ct);
        simulador.Reiniciar();

        await bitacora.RegistrarAsync("SistemaReiniciado", "Sistema", null,
            new { AlertasCerradas = abiertas.Count, SensoresRestablecidos = sensores.Count }, ct);

        await notificador.SistemaReiniciadoAsync(ct);
        logger.LogInformation("Sistema de monitoreo reiniciado: {Alertas} alertas cerradas, {Sensores} sensores restablecidos",
            abiertas.Count, sensores.Count);
    }
}
