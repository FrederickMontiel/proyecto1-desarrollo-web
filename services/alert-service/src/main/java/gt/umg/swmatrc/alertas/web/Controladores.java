package gt.umg.swmatrc.alertas.web;

import gt.umg.swmatrc.alertas.aplicacion.Dtos.AlertaDto;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.AlertasActivas;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.CambiarEstadoReglaRequest;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.EstadisticasEventosDto;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.EventoDto;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.FiltroAlertas;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.FiltroEventos;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.FiltroReglas;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.GestionAlertaRequest;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.GuardarReglaRequest;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.GuardarUmbralesRequest;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.Pagina;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.ReglaDto;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.RespuestaEvaluacion;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.ResumenAlertasDto;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.SolicitudEvaluacion;
import gt.umg.swmatrc.alertas.aplicacion.Dtos.UmbralesDto;
import gt.umg.swmatrc.alertas.aplicacion.ServicioAlertas;
import gt.umg.swmatrc.alertas.aplicacion.ServicioConfiguracion;
import gt.umg.swmatrc.alertas.aplicacion.ServicioEvaluacion;
import gt.umg.swmatrc.alertas.dominio.Enums.TipoSensor;
import gt.umg.swmatrc.alertas.dominio.Umbrales;
import gt.umg.swmatrc.alertas.integracion.Integracion.ClienteAuditoria;
import jakarta.validation.Valid;
import java.net.URI;
import java.time.Instant;
import java.util.List;
import java.util.Map;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.orm.ObjectOptimisticLockingFailureException;
import org.springframework.security.access.prepost.PreAuthorize;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PatchMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.PutMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.ResponseStatus;
import org.springframework.web.bind.annotation.RestController;

/** Controladores HTTP. Las rutas van en inglés; el contenido, en español. */
public final class Controladores {

    private Controladores() {
    }

    private static final String LEER = "hasAuthority('PERM_alertas:leer')";
    private static final String ATENDER = "hasAuthority('PERM_alertas:atender')";
    private static final String CONFIGURAR = "hasAuthority('PERM_reglas:editar')";

    // ===============================================================================

    /** Alertas e historial de eventos. */
    @RestController
    @RequestMapping("/api/alerts")
    public static class AlertasController {

        private final ServicioAlertas servicio;

        public AlertasController(ServicioAlertas servicio) {
            this.servicio = servicio;
        }

        /** Listado paginado con filtros por fecha, comunidad, sensor, fenómeno, nivel y estado. */
        @GetMapping
        @PreAuthorize(LEER)
        public Pagina<AlertaDto> listar(FiltroAlertas filtro) {
            return servicio.listar(filtro, ServicioAlertas.TAMANO_MAXIMO_PUBLICO);
        }

        @GetMapping("/active")
        @PreAuthorize(LEER)
        public AlertasActivas activas(@RequestParam(required = false) Integer comunidadId) {
            return servicio.activas(comunidadId);
        }

        /** Indicadores para el dashboard: activas, por nivel, por fenómeno, eventos recientes. */
        @GetMapping("/summary")
        @PreAuthorize(LEER)
        public ResumenAlertasDto resumen(@RequestParam(required = false) Integer comunidadId,
                                         @RequestParam(required = false) Instant desde,
                                         @RequestParam(required = false) Instant hasta) {
            return servicio.resumen(comunidadId, desde, hasta);
        }

        @GetMapping("/{id:\\d+}")
        @PreAuthorize(LEER)
        public AlertaDto obtener(@PathVariable long id) {
            return servicio.obtener(id);
        }

        /** El responsable vio la alerta y se hace cargo. */
        @PostMapping("/{id:\\d+}/attend")
        @PreAuthorize(ATENDER)
        public AlertaDto atender(@PathVariable long id, @Valid @RequestBody(required = false) GestionAlertaRequest r) {
            return servicio.atender(id, r == null ? null : r.comentario());
        }

        /** Alias de {@code attend}, como en la Fase 2 ("reconocer"). */
        @PostMapping("/{id:\\d+}/acknowledge")
        @PreAuthorize(ATENDER)
        public AlertaDto reconocer(@PathVariable long id) {
            return servicio.atender(id, null);
        }

        @PostMapping("/{id:\\d+}/close")
        @PreAuthorize(ATENDER)
        public AlertaDto cerrar(@PathVariable long id, @Valid @RequestBody(required = false) GestionAlertaRequest r) {
            return servicio.cerrar(id, r == null ? null : r.comentario());
        }

        /** Historial de fenómenos, del más reciente al más antiguo. */
        @GetMapping("/events")
        @PreAuthorize(LEER)
        public Pagina<EventoDto> eventos(FiltroEventos filtro) {
            return servicio.eventos(filtro, ServicioAlertas.TAMANO_MAXIMO_PUBLICO);
        }

        @GetMapping("/events/stats")
        @PreAuthorize(LEER)
        public EstadisticasEventosDto estadisticas(FiltroEventos filtro) {
            return servicio.estadisticas(filtro);
        }
    }

    // ===============================================================================

    /** Reglas configurables. Cualquiera puede consultarlas para entender por qué se levantó una alerta. */
    @RestController
    @RequestMapping("/api/alerts/rules")
    public static class ReglasController {

        private final ServicioConfiguracion servicio;

        public ReglasController(ServicioConfiguracion servicio) {
            this.servicio = servicio;
        }

        @GetMapping
        @PreAuthorize(LEER)
        public List<ReglaDto> listar(FiltroReglas filtro) {
            return servicio.listarReglas(filtro);
        }

        @GetMapping("/{id:\\d+}")
        @PreAuthorize(LEER)
        public ReglaDto obtener(@PathVariable long id) {
            return servicio.obtenerRegla(id);
        }

        @PostMapping
        @PreAuthorize(CONFIGURAR)
        public ResponseEntity<ReglaDto> crear(@Valid @RequestBody GuardarReglaRequest r) {
            ReglaDto creada = servicio.crearRegla(r);
            return ResponseEntity.created(URI.create("/api/alerts/rules/" + creada.id())).body(creada);
        }

        @PutMapping("/{id:\\d+}")
        @PreAuthorize(CONFIGURAR)
        public ReglaDto actualizar(@PathVariable long id, @Valid @RequestBody GuardarReglaRequest r) {
            return servicio.actualizarRegla(id, r);
        }

        @PatchMapping("/{id:\\d+}/status")
        @PreAuthorize(CONFIGURAR)
        public ReglaDto cambiarEstado(@PathVariable long id, @Valid @RequestBody CambiarEstadoReglaRequest r) {
            return servicio.cambiarEstadoRegla(id, r.activa());
        }

        @DeleteMapping("/{id:\\d+}")
        @PreAuthorize(CONFIGURAR)
        @ResponseStatus(HttpStatus.NO_CONTENT)
        public void eliminar(@PathVariable long id) {
            servicio.eliminarRegla(id);
        }
    }

    // ===============================================================================

    /** Umbrales Amarillo/Naranja/Rojo por sensor. */
    @RestController
    @RequestMapping("/api/alerts/thresholds")
    public static class UmbralesController {

        private final ServicioConfiguracion servicio;

        public UmbralesController(ServicioConfiguracion servicio) {
            this.servicio = servicio;
        }

        /** Solo los sensores recalibrados; los demás usan la plantilla de su tipo. */
        @GetMapping
        @PreAuthorize(LEER)
        public List<UmbralesDto> personalizados() {
            return servicio.umbralesPersonalizados();
        }

        /** Calibración recomendada por tipo de sensor (la proponen los formularios de alta). */
        @GetMapping("/templates")
        @PreAuthorize(LEER)
        public Map<TipoSensor, Umbrales> plantillas() {
            return servicio.plantillas();
        }

        @GetMapping("/{sensorId:\\d+}")
        @PreAuthorize(LEER)
        public UmbralesDto obtener(@PathVariable int sensorId, @RequestParam(required = false) TipoSensor tipo) {
            return servicio.umbralesDe(sensorId, tipo);
        }

        @PutMapping("/{sensorId:\\d+}")
        @PreAuthorize(CONFIGURAR)
        public UmbralesDto guardar(@PathVariable int sensorId, @Valid @RequestBody GuardarUmbralesRequest r) {
            return servicio.guardarUmbrales(sensorId, r);
        }

        /** Vuelve a la plantilla del tipo. */
        @DeleteMapping("/{sensorId:\\d+}")
        @PreAuthorize(CONFIGURAR)
        @ResponseStatus(HttpStatus.NO_CONTENT)
        public void restablecer(@PathVariable int sensorId) {
            servicio.restablecerUmbrales(sensorId);
        }
    }

    // ===============================================================================

    /**
     * Rutas para Sensor y Report. Viven fuera de {@code /api}, así que el gateway no las
     * expone, y exigen token de servicio.
     */
    @RestController
    @RequestMapping("/internal")
    public static class InternoController {

        private static final Logger log = LoggerFactory.getLogger(InternoController.class);

        private final ServicioEvaluacion evaluacion;
        private final ServicioAlertas alertas;
        private final ClienteAuditoria auditoria;

        public InternoController(ServicioEvaluacion evaluacion, ServicioAlertas alertas, ClienteAuditoria auditoria) {
            this.evaluacion = evaluacion;
            this.alertas = alertas;
            this.auditoria = auditoria;
        }

        /**
         * Evaluación del ciclo de Sensor Service. Si en el mismo instante un operador atendió
         * o cerró una alerta de la comunidad, la transacción choca (bloqueo optimista) y se
         * reintenta una vez con los datos frescos.
         */
        @PostMapping("/evaluate")
        public RespuestaEvaluacion evaluar(@Valid @RequestBody SolicitudEvaluacion solicitud) {
            try {
                return evaluacion.evaluar(solicitud);
            } catch (ObjectOptimisticLockingFailureException | DataIntegrityViolationException e) {
                log.info("Evaluación concurrente con una acción de usuario; se reintenta. {}", e.getMessage());
                return evaluacion.evaluar(solicitud);
            }
        }

        @GetMapping("/alerts/active")
        public AlertasActivas activas(@RequestParam(required = false) Integer comunidadId) {
            return alertas.activas(comunidadId);
        }

        /** Reinicio del sistema pedido por Sensor Service: cierra todas las alertas abiertas. */
        @PostMapping("/reset")
        public Map<String, Integer> reiniciar() {
            int cerradas = evaluacion.reiniciar();
            auditoria.registrar("AlertasCerradasPorReinicio", "Sistema", null, Map.of("alertasCerradas", cerradas));
            return Map.of("alertasCerradas", cerradas);
        }

        /** Alertas paginadas con páginas de hasta 5000 filas, para procesar por lotes (RF-REP-04). */
        @GetMapping("/alerts")
        public Pagina<AlertaDto> listar(FiltroAlertas filtro) {
            return alertas.listar(filtro, ServicioAlertas.TAMANO_MAXIMO_INTERNO);
        }

        @GetMapping("/alerts/summary")
        public ResumenAlertasDto resumen(@RequestParam(required = false) Integer comunidadId,
                                         @RequestParam(required = false) Instant desde,
                                         @RequestParam(required = false) Instant hasta) {
            return alertas.resumen(comunidadId, desde, hasta);
        }

        /** Historial de eventos por lotes (RF-REP-05). */
        @GetMapping("/events")
        public Pagina<EventoDto> eventos(FiltroEventos filtro) {
            return alertas.eventos(filtro, ServicioAlertas.TAMANO_MAXIMO_INTERNO);
        }

        @GetMapping("/events/stats")
        public EstadisticasEventosDto estadisticas(FiltroEventos filtro) {
            return alertas.estadisticas(filtro);
        }
    }
}
