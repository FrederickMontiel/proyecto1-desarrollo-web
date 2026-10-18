package gt.umg.swmatrc.alertas.aplicacion;

import gt.umg.swmatrc.alertas.aplicacion.Dtos.AlertaDto;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.CambioAlerta;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.ComunidadEvaluacion;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.NivelSensor;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.RespuestaEvaluacion;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.SensorEvaluacion;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.SolicitudEvaluacion;
import gt.umg.swmatrc.alertas.dominio.Enums.EstadoAlerta;
import gt.umg.swmatrc.alertas.dominio.Enums.NivelAlerta;
import gt.umg.swmatrc.alertas.dominio.Reglas;
import gt.umg.swmatrc.alertas.dominio.Riesgo.ContextoEvaluacion;
import gt.umg.swmatrc.alertas.dominio.Riesgo.Diagnostico;
import gt.umg.swmatrc.alertas.dominio.Riesgo.MotorRiesgo;
import gt.umg.swmatrc.alertas.dominio.Riesgo.SensorEvaluado;
import gt.umg.swmatrc.alertas.dominio.Umbrales;
import gt.umg.swmatrc.alertas.persistencia.Entidades.Alerta;
import gt.umg.swmatrc.alertas.persistencia.Entidades.EventoHistorial;
import gt.umg.swmatrc.alertas.persistencia.Entidades.ReglaAlerta;
import gt.umg.swmatrc.alertas.persistencia.Entidades.UmbralSensor;
import gt.umg.swmatrc.alertas.persistencia.Repositorios.RepositorioAlertas;
import gt.umg.swmatrc.alertas.persistencia.Repositorios.RepositorioEventos;
import gt.umg.swmatrc.alertas.persistencia.Repositorios.RepositorioReglas;
import gt.umg.swmatrc.alertas.persistencia.Repositorios.RepositorioUmbrales;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.function.Function;
import java.util.stream.Collectors;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/**
 * Evaluación de riesgo que Sensor Service pide en cada ciclo de adquisición. Aplica umbrales
 * y reglas, abre, escala o cierra alertas, lleva el historial de eventos y responde con el
 * nivel de cada sensor y los cambios que hay que difundir.
 */
@Service
public class ServicioEvaluacion {

    private static final Logger log = LoggerFactory.getLogger(ServicioEvaluacion.class);

    private final MotorRiesgo motor = new MotorRiesgo(Reglas.todas());
    private final RepositorioAlertas alertas;
    private final RepositorioEventos eventos;
    private final RepositorioReglas reglas;
    private final RepositorioUmbrales umbrales;

    public ServicioEvaluacion(RepositorioAlertas alertas, RepositorioEventos eventos,
                              RepositorioReglas reglas, RepositorioUmbrales umbrales) {
        this.alertas = alertas;
        this.eventos = eventos;
        this.reglas = reglas;
        this.umbrales = umbrales;
    }

    @Transactional
    public RespuestaEvaluacion evaluar(SolicitudEvaluacion solicitud) {
        Instant ahora = Instant.now();
        List<ComunidadEvaluacion> comunidades = solicitud.comunidades() == null ? List.of() : solicitud.comunidades();

        // Una consulta por tabla para todo el lote, no una por comunidad o por sensor.
        List<Integer> sensorIds = comunidades.stream()
                .flatMap(c -> c.sensores() == null ? java.util.stream.Stream.empty() : c.sensores().stream())
                .map(SensorEvaluacion::sensorId).toList();
        Map<Integer, Umbrales> calibrados = umbrales.findAllById(sensorIds).stream()
                .collect(Collectors.toMap(u -> u.sensorId, UmbralSensor::aUmbrales));
        List<ReglaAlerta> activas = reglas.findByActivaTrue();
        Map<Integer, List<Alerta>> abiertasPorComunidad = alertas
                .findByComunidadIdInAndFechaCierreIsNull(comunidades.stream().map(ComunidadEvaluacion::comunidadId).toList())
                .stream().collect(Collectors.groupingBy(a -> a.comunidadId));

        List<NivelSensor> niveles = new ArrayList<>();
        List<CambioAlerta> cambios = new ArrayList<>();

        for (ComunidadEvaluacion comunidad : comunidades) {
            List<Alerta> abiertas = abiertasPorComunidad.getOrDefault(comunidad.comunidadId(), List.of());

            if (!comunidad.activa()) {
                // Una comunidad desactivada deja de monitorearse: sus avisos ya no describen
                // nada vigente y se cierran sin esperar la histéresis.
                cerrarSinMonitoreo(abiertas, ahora).forEach(a -> cambios.add(cambio("AlertaCerrada", a)));
                continue;
            }

            List<SensorEvaluado> sensores = (comunidad.sensores() == null ? List.<SensorEvaluacion>of() : comunidad.sensores())
                    .stream()
                    .map(s -> new SensorEvaluado(s.sensorId(), s.codigo(), s.nombre(), s.tipo(), s.unidadMedida(), s.estado(),
                            s.valor(), s.valorMinimo(), s.valorMaximo(), s.ultimaLectura(),
                            calibrados.getOrDefault(s.sensorId(), Umbrales.plantilla(s.tipo()))))
                    .toList();

            // El color de cada sensor: lo que el tablero pinta en la tarjeta y en la lectura.
            for (SensorEvaluado s : sensores)
                niveles.add(new NivelSensor(s.id(), s.operativo() ? s.clasificar() : NivelAlerta.Verde));

            var contexto = new ContextoEvaluacion(comunidad.comunidadId(), comunidad.comunidadNombre(), sensores);
            List<Diagnostico> diagnosticos = Evaluacion.diagnosticar(motor, contexto, activas);
            var resultado = Evaluacion.conciliar(contexto, diagnosticos, new ArrayList<>(abiertas),
                    sensores.stream().collect(Collectors.toMap(SensorEvaluado::id, Function.identity())), ahora);

            // El nombre de la comunidad puede haber cambiado: se mantiene al día en lo abierto.
            abiertas.forEach(a -> a.comunidadNombre = comunidad.comunidadNombre());
            alertas.saveAll(abiertas);
            if (!resultado.hayCambios()) continue;

            alertas.saveAll(resultado.nuevas());
            abrirEventos(resultado.nuevas(), sensores);
            actualizarEventos(resultado.escaladas());
            cerrarEventos(resultado.cerradas(), ahora);

            for (Alerta a : resultado.nuevas()) {
                log.warn("Alerta {} por {} en {}: {}", a.nivel, a.fenomeno, a.comunidadNombre, a.mensaje);
                cambios.add(cambio("AlertaGenerada", a));
            }
            resultado.escaladas().forEach(a -> cambios.add(cambio("AlertaGenerada", a)));
            resultado.cerradas().forEach(a -> cambios.add(cambio("AlertaCerrada", a)));
        }

        return new RespuestaEvaluacion(niveles, cambios);
    }

    /** Cierra todo lo abierto (reinicio del sistema). El historial se conserva, sellado. */
    @Transactional
    public int reiniciar() {
        Instant ahora = Instant.now();
        List<Alerta> abiertas = alertas.findByFechaCierreIsNull();
        abiertas.forEach(a -> a.cerrar(null, null, ahora));
        alertas.saveAll(abiertas);

        List<EventoHistorial> abiertos = eventos.findByFechaFinIsNull();
        abiertos.forEach(e -> {
            e.fechaFin = ahora;
            e.estado = EstadoAlerta.Cerrada;
            e.fechaModificacion = ahora;
        });
        eventos.saveAll(abiertos);
        return abiertas.size();
    }

    private List<Alerta> cerrarSinMonitoreo(List<Alerta> abiertas, Instant ahora) {
        if (abiertas.isEmpty()) return List.of();
        abiertas.forEach(a -> a.cerrar(null, null, ahora));
        alertas.saveAll(abiertas);
        cerrarEventos(abiertas, ahora);
        return abiertas;
    }

    /** Abre el asiento de historial que acompaña a cada alerta nueva. */
    private void abrirEventos(List<Alerta> nuevas, List<SensorEvaluado> sensores) {
        for (Alerta alerta : nuevas) {
            var sensor = sensores.stream().filter(s -> alerta.sensorId != null && s.id() == alerta.sensorId).findFirst();
            var evento = new EventoHistorial();
            evento.comunidadId = alerta.comunidadId;
            evento.comunidadNombre = alerta.comunidadNombre;
            evento.alertaId = alerta.id;
            evento.fenomeno = alerta.fenomeno;
            evento.nivelMaximo = alerta.nivel;
            evento.descripcion = alerta.mensaje;
            evento.origenSensor = sensor.map(s -> s.codigo() + " — " + s.nombre()).orElse("Evaluación combinada");
            evento.sensorId = sensor.map(SensorEvaluado::id).orElse(null);
            evento.valorRegistrado = alerta.valorDisparo;
            evento.estado = EstadoAlerta.Activa;
            evento.fechaInicio = alerta.fechaHora;
            eventos.save(evento);
        }
    }

    /** Eleva el nivel máximo registrado en el historial cuando un episodio se agrava. */
    private void actualizarEventos(List<Alerta> escaladas) {
        if (escaladas.isEmpty()) return;
        Map<Long, Alerta> porId = escaladas.stream().collect(Collectors.toMap(a -> a.id, Function.identity()));
        for (EventoHistorial evento : eventos.findByAlertaIdInAndFechaFinIsNull(porId.keySet())) {
            Alerta alerta = porId.get(evento.alertaId);
            evento.nivelMaximo = NivelAlerta.maximo(evento.nivelMaximo, alerta.nivel);
            evento.descripcion = alerta.mensaje;
            evento.valorRegistrado = alerta.valorDisparo;
            evento.estado = EstadoAlerta.Activa;
            evento.fechaModificacion = Instant.now();
        }
    }

    /** Sella los asientos de los episodios que volvieron a la normalidad. */
    private void cerrarEventos(List<Alerta> cerradas, Instant ahora) {
        if (cerradas.isEmpty()) return;
        for (EventoHistorial evento : eventos.findByAlertaIdInAndFechaFinIsNull(cerradas.stream().map(a -> a.id).toList())) {
            evento.fechaFin = ahora;
            evento.estado = EstadoAlerta.Cerrada;
            evento.fechaModificacion = ahora;
        }
    }

    private static CambioAlerta cambio(String evento, Alerta alerta) {
        return new CambioAlerta(evento, alerta.comunidadId, AlertaDto.desde(alerta));
    }
}
