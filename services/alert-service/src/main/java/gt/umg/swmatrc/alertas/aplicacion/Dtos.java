package gt.umg.swmatrc.alertas.aplicacion;

import gt.umg.swmatrc.alertas.dominio.Enums.EstadoAlerta;
import gt.umg.swmatrc.alertas.dominio.Enums.EstadoSensor;
import gt.umg.swmatrc.alertas.dominio.Enums.NivelAlerta;
import gt.umg.swmatrc.alertas.dominio.Enums.TipoFenomeno;
import gt.umg.swmatrc.alertas.dominio.Enums.TipoSensor;
import gt.umg.swmatrc.alertas.dominio.Umbrales;
import gt.umg.swmatrc.alertas.persistencia.Entidades.Alerta;
import gt.umg.swmatrc.alertas.persistencia.Entidades.EventoHistorial;
import gt.umg.swmatrc.alertas.persistencia.Entidades.ReglaAlerta;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Size;
import java.math.BigDecimal;
import java.time.Duration;
import java.time.Instant;
import java.util.List;
import java.util.Map;
import java.util.function.Function;
import org.springframework.data.domain.Page;

/** Contratos HTTP. Los nombres de campo coinciden con los de la Fase 2. */
public final class Dtos {

    private Dtos() {
    }

    // -------------------------------------------------------------------------------
    // Contrato con Sensor Service: POST /internal/evaluate
    // -------------------------------------------------------------------------------

    public record SolicitudEvaluacion(@NotNull List<ComunidadEvaluacion> comunidades) {
    }

    public record ComunidadEvaluacion(
            int comunidadId,
            String comunidadNombre,
            boolean activa,
            List<SensorEvaluacion> sensores) {
    }

    public record SensorEvaluacion(
            int sensorId,
            String codigo,
            String nombre,
            TipoSensor tipo,
            String unidadMedida,
            EstadoSensor estado,
            BigDecimal valor,
            BigDecimal valorMinimo,
            BigDecimal valorMaximo,
            Instant ultimaLectura) {
    }

    public record RespuestaEvaluacion(List<NivelSensor> niveles, List<CambioAlerta> cambios) {
    }

    public record NivelSensor(int sensorId, NivelAlerta nivel) {
    }

    /** {@code evento} es el nombre del mensaje SignalR que Sensor difunde tal cual. */
    public record CambioAlerta(String evento, int comunidadId, AlertaDto alerta) {
    }

    public record AlertasActivas(NivelAlerta nivelGlobal, List<AlertaDto> alertas) {
    }

    // -------------------------------------------------------------------------------
    // Alertas
    // -------------------------------------------------------------------------------

    public record AlertaDto(
            Long id,
            Integer comunidadId,
            String comunidadNombre,
            Integer sensorId,
            String sensorCodigo,
            String sensorNombre,
            NivelAlerta nivel,
            String nivelNombre,
            TipoFenomeno fenomeno,
            String fenomenoNombre,
            String mensaje,
            BigDecimal valorDisparo,
            BigDecimal umbral,
            String unidadMedida,
            Long reglaId,
            String reglaNombre,
            EstadoAlerta estado,
            Instant fechaHora,
            Instant fechaCierre,
            boolean activa,
            boolean reconocida,
            String reconocidaPor,
            Instant fechaReconocimiento,
            String cerradaPor) {

        public static AlertaDto desde(Alerta a) {
            return new AlertaDto(a.id, a.comunidadId, a.comunidadNombre, a.sensorId, a.sensorCodigo, a.sensorNombre,
                    a.nivel, a.nivel.name(), a.fenomeno, a.fenomeno.name(), a.mensaje, a.valorDisparo, a.umbral,
                    a.unidadMedida, a.reglaId, a.reglaNombre, a.estado, a.fechaHora, a.fechaCierre, a.activa(),
                    a.reconocida, a.reconocidaPor, a.fechaReconocimiento, a.cerradaPor);
        }
    }

    public record FiltroAlertas(
            Integer comunidadId,
            Integer sensorId,
            TipoFenomeno fenomeno,
            NivelAlerta nivel,
            EstadoAlerta estado,
            Instant desde,
            Instant hasta,
            Integer pagina,
            Integer tamano) {
    }

    public record GestionAlertaRequest(@Size(max = 300, message = "El comentario admite como máximo 300 caracteres.") String comentario) {
    }

    // -------------------------------------------------------------------------------
    // Historial de eventos
    // -------------------------------------------------------------------------------

    public record EventoDto(
            Long id,
            Integer comunidadId,
            String comunidadNombre,
            Long alertaId,
            TipoFenomeno fenomeno,
            String fenomenoNombre,
            NivelAlerta nivelMaximo,
            String nivelNombre,
            String descripcion,
            String origenSensor,
            Integer sensorId,
            BigDecimal valorRegistrado,
            EstadoAlerta estado,
            String usuarioResponsable,
            Instant fechaInicio,
            Instant fechaFin,
            Double duracionMinutos) {

        public static EventoDto desde(EventoHistorial e) {
            Double duracion = e.fechaFin == null ? null
                    : Math.round(Duration.between(e.fechaInicio, e.fechaFin).toSeconds() / 6.0) / 10.0;
            return new EventoDto(e.id, e.comunidadId, e.comunidadNombre, e.alertaId, e.fenomeno, e.fenomeno.name(),
                    e.nivelMaximo, e.nivelMaximo.name(), e.descripcion, e.origenSensor, e.sensorId, e.valorRegistrado,
                    e.estado, e.usuarioResponsable, e.fechaInicio, e.fechaFin, duracion);
        }
    }

    public record FiltroEventos(
            Integer comunidadId,
            TipoFenomeno fenomeno,
            NivelAlerta nivel,
            EstadoAlerta estado,
            Instant desde,
            Instant hasta,
            Integer pagina,
            Integer tamano) {
    }

    public record EstadisticasEventosDto(
            int totalEventos,
            int eventosAbiertos,
            Double duracionPromedioMinutos,
            Map<String, Integer> porFenomeno,
            Map<String, Integer> porNivel,
            Map<String, Integer> porComunidad,
            Map<String, Integer> porDia) {
    }

    /** Indicadores de alertas y eventos para el dashboard (sección 15). */
    public record ResumenAlertasDto(
            int alertasActivas,
            NivelAlerta nivelGlobal,
            Map<String, Integer> alertasPorNivel,
            Map<String, Integer> alertasPorFenomeno,
            int eventosUltimas24h,
            Map<String, Integer> eventosPorFenomeno,
            int alertasEnPeriodo,
            Map<String, Integer> alertasPeriodoPorNivel,
            Map<String, Integer> alertasPeriodoPorFenomeno) {
    }

    // -------------------------------------------------------------------------------
    // Reglas configurables
    // -------------------------------------------------------------------------------

    public record ReglaDto(
            Long id,
            String nombre,
            TipoSensor tipoSensor,
            BigDecimal valorMinimo,
            BigDecimal valorMaximo,
            NivelAlerta nivel,
            TipoFenomeno fenomeno,
            String mensaje,
            boolean activa,
            Instant fechaCreacion,
            Instant fechaModificacion) {

        public static ReglaDto desde(ReglaAlerta r) {
            return new ReglaDto(r.id, r.nombre, r.tipoSensor, r.valorMinimo, r.valorMaximo, r.nivel, r.fenomeno,
                    r.mensaje, r.activa, r.fechaCreacion, r.fechaModificacion);
        }
    }

    public record GuardarReglaRequest(
            @NotBlank(message = "El nombre es obligatorio.") @Size(max = 120, message = "El nombre admite como máximo 120 caracteres.") String nombre,
            @NotNull(message = "Indique el tipo de sensor.") TipoSensor tipoSensor,
            BigDecimal valorMinimo,
            BigDecimal valorMaximo,
            @NotNull(message = "Indique el nivel.") NivelAlerta nivel,
            @NotNull(message = "Indique el fenómeno.") TipoFenomeno fenomeno,
            @NotBlank(message = "El mensaje es obligatorio.") @Size(max = 500, message = "El mensaje admite como máximo 500 caracteres.") String mensaje,
            Boolean activa) {
    }

    public record CambiarEstadoReglaRequest(@NotNull(message = "Indique si la regla queda activa.") Boolean activa) {
    }

    public record FiltroReglas(String busqueda, TipoSensor tipoSensor, TipoFenomeno fenomeno, NivelAlerta nivel, Boolean activa) {
    }

    // -------------------------------------------------------------------------------
    // Umbrales por sensor
    // -------------------------------------------------------------------------------

    /**
     * Umbrales vigentes de un sensor. {@code personalizados} es falso cuando vienen de la
     * plantilla del tipo porque nadie los ha recalibrado.
     */
    public record UmbralesDto(
            Integer sensorId,
            TipoSensor tipoSensor,
            BigDecimal umbralAmarilloAlto,
            BigDecimal umbralNaranjaAlto,
            BigDecimal umbralRojoAlto,
            BigDecimal umbralAmarilloBajo,
            BigDecimal umbralNaranjaBajo,
            BigDecimal umbralRojoBajo,
            boolean personalizados,
            Instant fechaModificacion) {

        public static UmbralesDto de(Integer sensorId, TipoSensor tipo, Umbrales u, boolean personalizados, Instant fecha) {
            return new UmbralesDto(sensorId, tipo, u.amarilloAlto(), u.naranjaAlto(), u.rojoAlto(),
                    u.amarilloBajo(), u.naranjaBajo(), u.rojoBajo(), personalizados, fecha);
        }
    }

    public record GuardarUmbralesRequest(
            @NotNull(message = "Indique el tipo de sensor.") TipoSensor tipoSensor,
            BigDecimal umbralAmarilloAlto,
            BigDecimal umbralNaranjaAlto,
            BigDecimal umbralRojoAlto,
            BigDecimal umbralAmarilloBajo,
            BigDecimal umbralNaranjaBajo,
            BigDecimal umbralRojoBajo) {

        public Umbrales aUmbrales() {
            return new Umbrales(umbralAmarilloAlto, umbralNaranjaAlto, umbralRojoAlto,
                    umbralAmarilloBajo, umbralNaranjaBajo, umbralRojoBajo);
        }
    }

    // -------------------------------------------------------------------------------
    // Paginación con la misma forma que los demás servicios
    // -------------------------------------------------------------------------------

    public record Pagina<T>(List<T> elementos, int pagina, int tamanoPagina, long totalElementos, int totalPaginas) {

        public static <E, T> Pagina<T> de(Page<E> pagina, Function<E, T> mapeo) {
            return new Pagina<>(pagina.getContent().stream().map(mapeo).toList(), pagina.getNumber() + 1,
                    pagina.getSize(), pagina.getTotalElements(), pagina.getTotalPages());
        }
    }
}
