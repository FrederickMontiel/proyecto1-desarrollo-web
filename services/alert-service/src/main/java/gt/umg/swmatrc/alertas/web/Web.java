package gt.umg.swmatrc.alertas.web;

import gt.umg.swmatrc.alertas.aplicacion.Excepciones;
import gt.umg.swmatrc.alertas.integracion.Integracion;
import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import java.io.IOException;
import java.net.URI;
import java.util.UUID;
import java.util.stream.Collectors;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.slf4j.MDC;
import org.springframework.core.Ordered;
import org.springframework.core.annotation.Order;
import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.http.HttpStatus;
import org.springframework.http.ProblemDetail;
import org.springframework.http.converter.HttpMessageNotReadableException;
import org.springframework.orm.ObjectOptimisticLockingFailureException;
import org.springframework.security.authorization.AuthorizationDeniedException;
import org.springframework.stereotype.Component;
import org.springframework.validation.BindingResult;
import org.springframework.validation.FieldError;
import org.springframework.web.HttpRequestMethodNotSupportedException;
import org.springframework.web.bind.MethodArgumentNotValidException;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;
import org.springframework.web.filter.OncePerRequestFilter;
import org.springframework.web.method.annotation.HandlerMethodValidationException;
import org.springframework.web.method.annotation.MethodArgumentTypeMismatchException;
import org.springframework.web.servlet.resource.NoResourceFoundException;

/** Manejo de errores y correlación, comunes a toda la API. */
public final class Web {

    private Web() {
    }

    /**
     * Toma el {@code X-Correlation-Id} que asigna el gateway y lo pone en el contexto de log:
     * cada línea de log de la operación lo incluye y viaja en las llamadas a otros servicios.
     */
    @Component
    @Order(Ordered.HIGHEST_PRECEDENCE)
    public static class FiltroCorrelacion extends OncePerRequestFilter {

        @Override
        protected void doFilterInternal(HttpServletRequest req, HttpServletResponse res, FilterChain cadena)
                throws ServletException, IOException {
            String id = req.getHeader(Integracion.CABECERA_CORRELACION);
            if (id == null || id.length() > 64 || !id.matches("[A-Za-z0-9-]+")) id = UUID.randomUUID().toString().replace("-", "");
            MDC.put("correlacion", id);
            try {
                cadena.doFilter(req, res);
            } finally {
                MDC.remove("correlacion");
            }
        }
    }

    /**
     * Errores como ProblemDetails (RFC 9457) en español, el mismo formato que el gateway y los
     * demás servicios. Lo inesperado se responde como 500 genérico; el detalle queda en el log.
     */
    @RestControllerAdvice
    public static class ManejadorErrores {

        private static final Logger log = LoggerFactory.getLogger(ManejadorErrores.class);

        @ExceptionHandler(Excepciones.NoEncontrado.class)
        ProblemDetail noEncontrado(Excepciones.NoEncontrado e, HttpServletRequest req) {
            return problema(HttpStatus.NOT_FOUND, "Recurso no encontrado", e.getMessage(), req);
        }

        @ExceptionHandler(Excepciones.Validacion.class)
        ProblemDetail validacion(Excepciones.Validacion e, HttpServletRequest req) {
            return problema(HttpStatus.BAD_REQUEST, "Solicitud inválida", e.getMessage(), req);
        }

        @ExceptionHandler(Excepciones.Conflicto.class)
        ProblemDetail conflicto(Excepciones.Conflicto e, HttpServletRequest req) {
            return problema(HttpStatus.CONFLICT, "Conflicto", e.getMessage(), req);
        }

        /** El ciclo de evaluación modificó la alerta mientras el operador la atendía. */
        @ExceptionHandler(ObjectOptimisticLockingFailureException.class)
        ProblemDetail concurrencia(ObjectOptimisticLockingFailureException e, HttpServletRequest req) {
            return problema(HttpStatus.CONFLICT, "Conflicto",
                    "La alerta cambió mientras se procesaba la solicitud. Vuelva a intentarlo.", req);
        }

        @ExceptionHandler(DataIntegrityViolationException.class)
        ProblemDetail integridad(DataIntegrityViolationException e, HttpServletRequest req) {
            log.warn("Violación de integridad en {}: {}", req.getRequestURI(), e.getMostSpecificCause().getMessage());
            return problema(HttpStatus.CONFLICT, "Conflicto", "La operación choca con datos existentes.", req);
        }

        /**
         * Reglas de validación del cuerpo (mensajes propios en español) y errores de
         * conversión de parámetros de consulta (una fecha o un nivel mal escritos), cuyo
         * mensaje por defecto es técnico y en inglés.
         */
        @ExceptionHandler(MethodArgumentNotValidException.class)
        ProblemDetail argumentos(MethodArgumentNotValidException e, HttpServletRequest req) {
            return problema(HttpStatus.BAD_REQUEST, "Solicitud inválida", mensajes(e.getBindingResult()), req);
        }

        static String mensajes(BindingResult resultado) {
            return resultado.getAllErrors().stream()
                    .map(error -> {
                        if (error instanceof FieldError campo && campo.isBindingFailure()) {
                            boolean fecha = campo.getField().equals("desde") || campo.getField().equals("hasta");
                            return "El parámetro '" + campo.getField() + "' no tiene un valor válido"
                                    + (fecha ? " (use ISO 8601, por ejemplo 2026-10-22T00:00:00Z)." : ".");
                        }
                        return error.getDefaultMessage() == null ? "Hay datos inválidos." : error.getDefaultMessage();
                    })
                    .distinct()
                    .collect(Collectors.joining(" "));
        }

        @ExceptionHandler(HandlerMethodValidationException.class)
        ProblemDetail parametros(HandlerMethodValidationException e, HttpServletRequest req) {
            return problema(HttpStatus.BAD_REQUEST, "Solicitud inválida", "Hay parámetros inválidos en la solicitud.", req);
        }

        @ExceptionHandler(HttpMessageNotReadableException.class)
        ProblemDetail cuerpo(HttpMessageNotReadableException e, HttpServletRequest req) {
            return problema(HttpStatus.BAD_REQUEST, "Solicitud inválida",
                    "El cuerpo de la solicitud no es un JSON válido o tiene un valor de tipo incorrecto.", req);
        }

        @ExceptionHandler(MethodArgumentTypeMismatchException.class)
        ProblemDetail tipo(MethodArgumentTypeMismatchException e, HttpServletRequest req) {
            return problema(HttpStatus.BAD_REQUEST, "Solicitud inválida",
                    "El parámetro '" + e.getName() + "' no tiene un valor válido.", req);
        }

        @ExceptionHandler(org.springframework.validation.BindException.class)
        ProblemDetail enlace(org.springframework.validation.BindException e, HttpServletRequest req) {
            return problema(HttpStatus.BAD_REQUEST, "Solicitud inválida", mensajes(e.getBindingResult()), req);
        }

        @ExceptionHandler(AuthorizationDeniedException.class)
        ProblemDetail denegado(AuthorizationDeniedException e, HttpServletRequest req) {
            return problema(HttpStatus.FORBIDDEN, "Acceso denegado", "No tiene permisos para realizar esta operación.", req);
        }

        @ExceptionHandler(NoResourceFoundException.class)
        ProblemDetail ruta(NoResourceFoundException e, HttpServletRequest req) {
            return problema(HttpStatus.NOT_FOUND, "Recurso no encontrado", "La ruta solicitada no existe.", req);
        }

        @ExceptionHandler(HttpRequestMethodNotSupportedException.class)
        ProblemDetail metodo(HttpRequestMethodNotSupportedException e, HttpServletRequest req) {
            return problema(HttpStatus.METHOD_NOT_ALLOWED, "Método no permitido", "La ruta no admite el método " + e.getMethod() + ".", req);
        }

        @ExceptionHandler(Exception.class)
        ProblemDetail inesperado(Exception e, HttpServletRequest req) {
            log.error("Error no controlado en {} {}", req.getMethod(), req.getRequestURI(), e);
            return problema(HttpStatus.INTERNAL_SERVER_ERROR, "Error interno",
                    "Ocurrió un error inesperado al procesar la solicitud.", req);
        }

        private static ProblemDetail problema(HttpStatus estado, String titulo, String detalle, HttpServletRequest req) {
            if (estado.is4xxClientError() && estado != HttpStatus.NOT_FOUND)
                log.warn("{} en {}: {}", titulo, req.getRequestURI(), detalle);
            ProblemDetail p = ProblemDetail.forStatusAndDetail(estado, detalle);
            p.setTitle(titulo);
            p.setType(URI.create("https://httpstatuses.io/" + estado.value()));
            p.setInstance(URI.create(req.getRequestURI()));
            return p;
        }
    }
}
