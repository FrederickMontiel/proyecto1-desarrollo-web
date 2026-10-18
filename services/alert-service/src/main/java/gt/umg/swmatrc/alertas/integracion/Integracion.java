package gt.umg.swmatrc.alertas.integracion;

import gt.umg.swmatrc.alertas.aplicacion.Dtos.AlertaDto;
import jakarta.annotation.PreDestroy;
import jakarta.servlet.http.HttpServletRequest;
import java.net.http.HttpClient;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.BlockingQueue;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.slf4j.MDC;
import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.http.MediaType;
import org.springframework.http.client.JdkClientHttpRequestFactory;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.security.oauth2.server.resource.authentication.JwtAuthenticationToken;
import org.springframework.stereotype.Component;
import org.springframework.web.client.RestClient;
import org.springframework.web.client.RestClientException;
import org.springframework.web.context.request.RequestContextHolder;
import org.springframework.web.context.request.ServletRequestAttributes;

/** Comunicación con Auth, Sensor y Audit. Siempre por sus API, nunca por sus bases. */
public final class Integracion {

    private Integracion() {
    }

    public static final String CABECERA_CORRELACION = "X-Correlation-Id";

    /** Sección {@code swmatrc} de application.yml. */
    @ConfigurationProperties("swmatrc")
    public record Propiedades(Servicio servicio, Jwt jwt) {

        public record Servicio(String nombre, String secreto, String urlAuth, String urlAuditoria, String urlSensor) {
        }

        public record Jwt(String emisor, String audiencia, String audienciaInterna, String jwks) {
        }
    }

    static RestClient cliente(Duration lectura) {
        // HTTP/1.1 explícito: sobre http:// el cliente de Java intenta pasar a HTTP/2 con
        // "Upgrade: h2c", y uvicorn (Audit, Report) rechaza esa negociación.
        var http = HttpClient.newBuilder()
                .version(HttpClient.Version.HTTP_1_1)
                .connectTimeout(Duration.ofSeconds(2))
                .build();
        var fabrica = new JdkClientHttpRequestFactory(http);
        fabrica.setReadTimeout(lectura);
        return RestClient.builder().requestFactory(fabrica).build();
    }

    // ===============================================================================

    /**
     * Token con el que este servicio llama a los demás. Se pide al Auth Service y se reutiliza
     * hasta un minuto antes de que venza.
     */
    @Component
    public static class ProveedorTokenServicio {

        private final Propiedades propiedades;
        private final RestClient http = cliente(Duration.ofSeconds(3));
        private String token;
        private Instant expira = Instant.EPOCH;

        public ProveedorTokenServicio(Propiedades propiedades) {
            this.propiedades = propiedades;
        }

        public synchronized String obtener() {
            if (token != null && Instant.now().isBefore(expira.minusSeconds(60))) return token;

            var servicio = propiedades.servicio();
            RespuestaToken respuesta = http.post()
                    .uri(servicio.urlAuth() + "/internal/token")
                    .contentType(MediaType.APPLICATION_JSON)
                    .body(Map.of("clienteId", servicio.nombre(), "secreto", servicio.secreto()))
                    .retrieve()
                    .body(RespuestaToken.class);
            if (respuesta == null) throw new IllegalStateException("Auth Service devolvió una respuesta vacía.");

            token = respuesta.token();
            expira = respuesta.expira();
            return token;
        }

        record RespuestaToken(String token, Instant expira) {
        }
    }

    // ===============================================================================

    /** Datos de quien hace la petición en curso: del token y de las cabeceras. */
    public record Autor(Integer id, String email, String nombre, String ip, String correlacion) {

        public static Autor actual() {
            Integer id = null;
            String email = "sistema";
            String nombre = null;
            if (SecurityContextHolder.getContext().getAuthentication() instanceof JwtAuthenticationToken jwt) {
                String sub = jwt.getToken().getSubject();
                id = sub != null && sub.chars().allMatch(Character::isDigit) ? Integer.valueOf(sub) : null;
                email = jwt.getToken().getClaimAsString("email");
                nombre = jwt.getToken().getClaimAsString("name");
            }

            String ip = null;
            String correlacion = MDC.get("correlacion");
            if (RequestContextHolder.getRequestAttributes() instanceof ServletRequestAttributes atributos) {
                HttpServletRequest peticion = atributos.getRequest();
                ip = peticion.getRemoteAddr();
            }
            return new Autor(id, email == null ? "sistema" : email, nombre, ip, correlacion);
        }

        /** Lo que se guarda como "atendida por" / "cerrada por". */
        public String etiqueta() {
            return nombre != null && !nombre.isBlank() ? nombre : email;
        }
    }

    // ===============================================================================

    /**
     * Avisa a Sensor Service los cambios hechos por usuarios (atender, cerrar) para que los
     * tableros conectados por SignalR se enteren al instante. Es asíncrono y no falla la
     * operación si Sensor no responde.
     */
    @Component
    public static class ClienteSensor {

        private static final Logger log = LoggerFactory.getLogger(ClienteSensor.class);

        private final Propiedades propiedades;
        private final ProveedorTokenServicio tokens;
        private final RestClient http = cliente(Duration.ofSeconds(3));
        private final ExecutorService hilos = Executors.newVirtualThreadPerTaskExecutor();

        public ClienteSensor(Propiedades propiedades, ProveedorTokenServicio tokens) {
            this.propiedades = propiedades;
            this.tokens = tokens;
        }

        public void notificar(String evento, AlertaDto alerta) {
            String correlacion = MDC.get("correlacion");
            hilos.submit(() -> {
                try {
                    var peticion = http.post()
                            .uri(propiedades.servicio().urlSensor() + "/internal/notify")
                            .header("Authorization", "Bearer " + tokens.obtener())
                            .contentType(MediaType.APPLICATION_JSON)
                            .body(Map.of("comunidadId", alerta.comunidadId(), "evento", evento, "datos", alerta));
                    if (correlacion != null) peticion.header(CABECERA_CORRELACION, correlacion);
                    peticion.retrieve().toBodilessEntity();
                } catch (RestClientException | IllegalStateException e) {
                    log.warn("Sensor Service no recibió el aviso {} de la alerta {}: {}", evento, alerta.id(), e.getMessage());
                }
            });
        }

        @PreDestroy
        void cerrar() {
            hilos.close();
        }
    }

    // ===============================================================================

    /**
     * Registra acciones en el Audit Service. Encola y vuelve de inmediato: la operación del
     * usuario nunca espera a la bitácora ni falla si Audit está caído. Un hilo programado
     * vacía la cola en lotes; al apagarse, envía lo pendiente.
     */
    @Component
    public static class ClienteAuditoria {

        private static final Logger log = LoggerFactory.getLogger(ClienteAuditoria.class);
        private static final int TAMANO_LOTE = 200;

        private final Propiedades propiedades;
        private final ProveedorTokenServicio tokens;
        private final RestClient http = cliente(Duration.ofSeconds(3));
        /** Acotada: si Audit lleva mucho caído, se descartan eventos en lugar de agotar la memoria. */
        private final BlockingQueue<Map<String, Object>> cola = new ArrayBlockingQueue<>(10_000);

        public ClienteAuditoria(Propiedades propiedades, ProveedorTokenServicio tokens) {
            this.propiedades = propiedades;
            this.tokens = tokens;
        }

        public void registrar(String accion, String entidad, Object entidadId, Map<String, ?> detalle) {
            Autor autor = Autor.actual();
            Map<String, Object> evento = new HashMap<>();
            evento.put("servicio", propiedades.servicio().nombre());
            evento.put("usuarioId", autor.id());
            evento.put("usuarioEmail", autor.email());
            evento.put("accion", accion);
            evento.put("entidad", entidad);
            evento.put("entidadId", entidadId == null ? null : entidadId.toString());
            evento.put("detalle", detalle);
            evento.put("direccionIp", autor.ip());
            evento.put("correlacion", autor.correlacion());
            evento.put("fechaHora", Instant.now().toString());
            if (!cola.offer(evento)) log.warn("Cola de auditoría llena: se descarta '{}'.", accion);
        }

        @Scheduled(fixedDelay = 1000)
        void enviarPendientes() {
            List<Map<String, Object>> lote = new ArrayList<>(TAMANO_LOTE);
            cola.drainTo(lote, TAMANO_LOTE);
            if (lote.isEmpty()) return;
            try {
                http.post()
                        .uri(propiedades.servicio().urlAuditoria() + "/internal/events/batch")
                        .header("Authorization", "Bearer " + tokens.obtener())
                        .contentType(MediaType.APPLICATION_JSON)
                        .body(Map.of("eventos", lote))
                        .retrieve()
                        .toBodilessEntity();
            } catch (RestClientException | IllegalStateException e) {
                log.warn("No se pudieron registrar {} eventos en auditoría: {}", lote.size(), e.getMessage());
            }
        }

        @PreDestroy
        void vaciarAlApagar() {
            while (!cola.isEmpty()) enviarPendientes();
        }
    }
}
