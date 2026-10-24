package gt.umg.swmatrc.alertas.aplicacion;

import gt.umg.swmatrc.alertas.aplicacion.Dtos.AlertaDto;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.AlertasActivas;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.EstadisticasEventosDto;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.EventoDto;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.FiltroAlertas;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.FiltroEventos;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.Pagina;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.ResumenAlertasDto;
import gt.umg.swmatrc.alertas.dominio.Enums.EstadoAlerta;
import gt.umg.swmatrc.alertas.dominio.Enums.NivelAlerta;
import gt.umg.swmatrc.alertas.integracion.Integracion.Autor;
import gt.umg.swmatrc.alertas.integracion.Integracion.ClienteAuditoria;
import gt.umg.swmatrc.alertas.integracion.Integracion.ClienteSensor;
import gt.umg.swmatrc.alertas.persistencia.Entidades.Alerta;
import gt.umg.swmatrc.alertas.persistencia.Entidades.EventoHistorial;
import gt.umg.swmatrc.alertas.persistencia.Repositorios.RepositorioAlertas;
import gt.umg.swmatrc.alertas.persistencia.Repositorios.RepositorioEventos;
import jakarta.persistence.criteria.Predicate;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;
import java.util.function.Function;
import java.util.stream.Collectors;
import org.springframework.data.domain.PageRequest;
import org.springframework.data.domain.Sort;
import org.springframework.data.jpa.domain.Specification;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/** Consulta, atención y cierre de alertas; historial de eventos e indicadores. */
@Service
public class ServicioAlertas {

    /** Tope de página para el frontend. */
    public static final int TAMANO_MAXIMO_PUBLICO = 200;
    /** Tope para Report Service, que recorre grandes volúmenes por lotes (RNF-REP-05). */
    public static final int TAMANO_MAXIMO_INTERNO = 5000;

    private final RepositorioAlertas alertas;
    private final RepositorioEventos eventos;
    private final ClienteAuditoria auditoria;
    private final ClienteSensor sensor;

    public ServicioAlertas(RepositorioAlertas alertas, RepositorioEventos eventos,
                           ClienteAuditoria auditoria, ClienteSensor sensor) {
        this.alertas = alertas;
        this.eventos = eventos;
        this.auditoria = auditoria;
        this.sensor = sensor;
    }

    // -------------------------------------------------------------------------------
    // Alertas
    // -------------------------------------------------------------------------------

    @Transactional(readOnly = true)
    public AlertasActivas activas(Integer comunidadId) {
        Specification<Alerta> abiertas = (raiz, q, cb) -> {
            List<Predicate> condiciones = new ArrayList<>();
            condiciones.add(cb.isNull(raiz.get("fechaCierre")));
            if (comunidadId != null) condiciones.add(cb.equal(raiz.get("comunidadId"), comunidadId));
            return cb.and(condiciones.toArray(Predicate[]::new));
        };
        List<AlertaDto> lista = alertas.findAll(abiertas, Sort.by(Sort.Order.desc("nivel"), Sort.Order.desc("fechaHora")))
                .stream().map(AlertaDto::desde).toList();
        NivelAlerta global = lista.stream().map(AlertaDto::nivel).max(Comparator.naturalOrder()).orElse(NivelAlerta.Verde);
        return new AlertasActivas(global, lista);
    }

    @Transactional(readOnly = true)
    public Pagina<AlertaDto> listar(FiltroAlertas f, int tamanoMaximo) {
        validarRango(f.desde(), f.hasta());
        Specification<Alerta> filtro = (raiz, q, cb) -> {
            List<Predicate> c = new ArrayList<>();
            if (f.comunidadId() != null) c.add(cb.equal(raiz.get("comunidadId"), f.comunidadId()));
            if (f.sensorId() != null) c.add(cb.equal(raiz.get("sensorId"), f.sensorId()));
            if (f.fenomeno() != null) c.add(cb.equal(raiz.get("fenomeno"), f.fenomeno()));
            if (f.nivel() != null) c.add(cb.equal(raiz.get("nivel"), f.nivel()));
            if (f.estado() != null) c.add(cb.equal(raiz.get("estado"), f.estado()));
            if (f.desde() != null) c.add(cb.greaterThanOrEqualTo(raiz.get("fechaHora"), f.desde()));
            if (f.hasta() != null) c.add(cb.lessThanOrEqualTo(raiz.get("fechaHora"), f.hasta()));
            return cb.and(c.toArray(Predicate[]::new));
        };
        var pagina = PageRequest.of(pagina(f.pagina()), tamano(f.tamano(), 20, tamanoMaximo),
                Sort.by(Sort.Order.desc("fechaHora"), Sort.Order.desc("id")));
        return Pagina.de(alertas.findAll(filtro, pagina), AlertaDto::desde);
    }

    @Transactional(readOnly = true)
    public AlertaDto obtener(long id) {
        return AlertaDto.desde(cargar(id));
    }

    /** Deja constancia de que un responsable vio la alerta y se hace cargo. */
    @Transactional
    public AlertaDto atender(long id, String comentario) {
        Alerta alerta = cargar(id);
        if (alerta.estado == EstadoAlerta.Cerrada) throw new Excepciones.Conflicto("La alerta ya está cerrada.");
        if (alerta.estado == EstadoAlerta.Atendida) throw new Excepciones.Conflicto("La alerta ya fue atendida.");

        Autor autor = Autor.actual();
        Instant ahora = Instant.now();
        alerta.atender(autor.id(), autor.etiqueta(), ahora);
        sincronizarEvento(alerta, EstadoAlerta.Atendida, null, autor);
        alertas.saveAndFlush(alerta);

        var dto = AlertaDto.desde(alerta);
        auditoria.registrar("AlertaAtendida", "Alerta", id, detalle(alerta, comentario));
        sensor.notificar("AlertaAtendida", dto);
        return dto;
    }

    /**
     * Cierre manual. Da por terminado el episodio; si la condición de riesgo persiste, la
     * evaluación abrirá una alerta nueva en el siguiente ciclo: el cierre no silencia el riesgo.
     */
    @Transactional
    public AlertaDto cerrar(long id, String comentario) {
        Alerta alerta = cargar(id);
        if (alerta.estado == EstadoAlerta.Cerrada) throw new Excepciones.Conflicto("La alerta ya está cerrada.");

        Autor autor = Autor.actual();
        Instant ahora = Instant.now();
        alerta.cerrar(autor.id(), autor.etiqueta(), ahora);
        sincronizarEvento(alerta, EstadoAlerta.Cerrada, ahora, autor);
        alertas.saveAndFlush(alerta);

        var dto = AlertaDto.desde(alerta);
        auditoria.registrar("AlertaCerrada", "Alerta", id, detalle(alerta, comentario));
        sensor.notificar("AlertaCerrada", dto);
        return dto;
    }

    private void sincronizarEvento(Alerta alerta, EstadoAlerta estado, Instant fin, Autor autor) {
        eventos.findFirstByAlertaIdOrderByFechaInicioDesc(alerta.id).ifPresent(e -> {
            e.estado = estado;
            e.usuarioResponsableId = autor.id();
            e.usuarioResponsable = autor.etiqueta();
            if (fin != null && e.fechaFin == null) e.fechaFin = fin;
            e.fechaModificacion = Instant.now();
        });
    }

    private static Map<String, Object> detalle(Alerta a, String comentario) {
        Map<String, Object> d = new LinkedHashMap<>();
        d.put("fenomeno", a.fenomeno.name());
        d.put("nivel", a.nivel.name());
        d.put("comunidadId", a.comunidadId);
        if (comentario != null && !comentario.isBlank()) d.put("comentario", comentario.trim());
        return d;
    }

    private Alerta cargar(long id) {
        return alertas.findById(id).orElseThrow(() -> new Excepciones.NoEncontrado("la alerta", id));
    }

    // -------------------------------------------------------------------------------
    // Historial de eventos (RF-REP-05)
    // -------------------------------------------------------------------------------

    @Transactional(readOnly = true)
    public Pagina<EventoDto> eventos(FiltroEventos f, int tamanoMaximo) {
        validarRango(f.desde(), f.hasta());
        var pagina = PageRequest.of(pagina(f.pagina()), tamano(f.tamano(), 20, tamanoMaximo),
                Sort.by(Sort.Order.desc("fechaInicio"), Sort.Order.desc("id")));
        return Pagina.de(eventos.findAll(filtroEventos(f), pagina), EventoDto::desde);
    }

    @Transactional(readOnly = true)
    public EstadisticasEventosDto estadisticas(FiltroEventos f) {
        validarRango(f.desde(), f.hasta());
        List<EventoHistorial> filas = eventos.findAll(filtroEventos(f));
        List<EventoHistorial> cerrados = filas.stream().filter(e -> e.fechaFin != null).toList();

        Double promedio = cerrados.isEmpty() ? null : Math.round(cerrados.stream()
                .mapToLong(e -> Duration.between(e.fechaInicio, e.fechaFin).toSeconds())
                .average().orElse(0) / 6.0) / 10.0;

        return new EstadisticasEventosDto(
                filas.size(),
                filas.size() - cerrados.size(),
                promedio,
                contar(filas, e -> e.fenomeno.name()),
                contar(filas, e -> e.nivelMaximo.name()),
                contar(filas, e -> e.comunidadNombre),
                new TreeMap<>(contar(filas, e -> e.fechaInicio.atOffset(ZoneOffset.UTC).toLocalDate().toString())));
    }

    private static Specification<EventoHistorial> filtroEventos(FiltroEventos f) {
        return (raiz, q, cb) -> {
            List<Predicate> c = new ArrayList<>();
            if (f.comunidadId() != null) c.add(cb.equal(raiz.get("comunidadId"), f.comunidadId()));
            if (f.fenomeno() != null) c.add(cb.equal(raiz.get("fenomeno"), f.fenomeno()));
            if (f.nivel() != null) c.add(cb.equal(raiz.get("nivelMaximo"), f.nivel()));
            if (f.estado() != null) c.add(cb.equal(raiz.get("estado"), f.estado()));
            if (f.desde() != null) c.add(cb.greaterThanOrEqualTo(raiz.get("fechaInicio"), f.desde()));
            if (f.hasta() != null) c.add(cb.lessThanOrEqualTo(raiz.get("fechaInicio"), f.hasta()));
            return cb.and(c.toArray(Predicate[]::new));
        };
    }

    // -------------------------------------------------------------------------------
    // Indicadores del dashboard
    // -------------------------------------------------------------------------------

    /**
     * Alertas activas y eventos recientes, más la distribución de las alertas emitidas en un
     * periodo (por defecto, los últimos 30 días) por nivel y por fenómeno.
     */
    @Transactional(readOnly = true)
    public ResumenAlertasDto resumen(Integer comunidadId, Instant desde, Instant hasta) {
        Instant fin = hasta != null ? hasta : Instant.now();
        Instant inicio = desde != null ? desde : fin.minus(Duration.ofDays(30));
        validarRango(inicio, fin);

        List<AlertaDto> activas = activas(comunidadId).alertas();
        List<EventoHistorial> recientes = eventos.findAll(filtroEventos(
                new FiltroEventos(comunidadId, null, null, null, Instant.now().minus(Duration.ofHours(24)), null, null, null)));
        List<Alerta> periodo = alertas.findAll((raiz, q, cb) -> {
            List<Predicate> c = new ArrayList<>();
            if (comunidadId != null) c.add(cb.equal(raiz.get("comunidadId"), comunidadId));
            c.add(cb.between(raiz.get("fechaHora"), inicio, fin));
            return cb.and(c.toArray(Predicate[]::new));
        });

        return new ResumenAlertasDto(
                activas.size(),
                activas.stream().map(AlertaDto::nivel).max(Comparator.naturalOrder()).orElse(NivelAlerta.Verde),
                contar(activas, a -> a.nivel().name()),
                contar(activas, a -> a.fenomeno().name()),
                recientes.size(),
                contar(recientes, e -> e.fenomeno.name()),
                periodo.size(),
                contar(periodo, a -> a.nivel.name()),
                contar(periodo, a -> a.fenomeno.name()));
    }

    // -------------------------------------------------------------------------------

    private static <T> Map<String, Integer> contar(List<T> filas, Function<T, String> clave) {
        Map<String, Integer> conteo = new HashMap<>();
        for (T fila : filas) conteo.merge(clave.apply(fila) == null ? "" : clave.apply(fila), 1, Integer::sum);
        return conteo.entrySet().stream()
                .sorted(Map.Entry.<String, Integer>comparingByValue().reversed())
                .collect(Collectors.toMap(Map.Entry::getKey, Map.Entry::getValue, (a, b) -> a, LinkedHashMap::new));
    }

    static void validarRango(Instant desde, Instant hasta) {
        if (desde != null && hasta != null && desde.isAfter(hasta))
            throw new Excepciones.Validacion("La fecha 'desde' no puede ser posterior a 'hasta'.");
    }

    static int pagina(Integer pagina) {
        return Math.max(1, pagina == null ? 1 : pagina) - 1;
    }

    static int tamano(Integer tamano, int porDefecto, int maximo) {
        return Math.clamp(tamano == null ? porDefecto : tamano, 1, maximo);
    }
}
