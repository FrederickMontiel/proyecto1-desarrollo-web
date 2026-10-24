package gt.umg.swmatrc.alertas.persistencia;

import gt.umg.swmatrc.alertas.dominio.Enums.EstadoAlerta;
import gt.umg.swmatrc.alertas.dominio.Enums.NivelAlerta;
import gt.umg.swmatrc.alertas.dominio.Enums.TipoFenomeno;
import gt.umg.swmatrc.alertas.dominio.Enums.TipoSensor;
import gt.umg.swmatrc.alertas.dominio.Riesgo;
import gt.umg.swmatrc.alertas.dominio.Umbrales;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EnumType;
import jakarta.persistence.Enumerated;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.PreUpdate;
import jakarta.persistence.Table;
import jakarta.persistence.Version;
import java.math.BigDecimal;
import java.time.Instant;

/**
 * Entidades JPA. Se declaran como clases anidadas públicas para tener el modelo completo a
 * la vista; Hibernate las trata igual que si estuvieran en archivos separados.
 *
 * <p>Las alertas y eventos guardan una <b>copia</b> del nombre de la comunidad y del código
 * del sensor: viven en otros servicios y el historial debe seguir siendo legible aunque el
 * sensor se dé de baja.
 */
public final class Entidades {

    private Entidades() {
    }

    // ===============================================================================

    /** Regla configurable: si un sensor del tipo indicado cae en el rango, se diagnostica el fenómeno. */
    @Entity
    @Table(name = "reglas_alerta")
    public static class ReglaAlerta {

        @Id
        @GeneratedValue(strategy = GenerationType.IDENTITY)
        public Long id;

        public String nombre;

        @Enumerated(EnumType.STRING)
        @Column(name = "tipo_sensor")
        public TipoSensor tipoSensor;

        @Column(name = "valor_minimo")
        public BigDecimal valorMinimo;

        @Column(name = "valor_maximo")
        public BigDecimal valorMaximo;

        @Enumerated(EnumType.ORDINAL)
        public NivelAlerta nivel;

        @Enumerated(EnumType.ORDINAL)
        public TipoFenomeno fenomeno;

        /** Admite los marcadores {valor}, {unidad}, {sensor} y {comunidad}. */
        public String mensaje;

        public boolean activa = true;

        @Column(name = "fecha_creacion")
        public Instant fechaCreacion = Instant.now();

        @Column(name = "fecha_modificacion")
        public Instant fechaModificacion;

        @PreUpdate
        void alModificar() {
            fechaModificacion = Instant.now();
        }

        /** La lectura cae dentro del rango de disparo. */
        public boolean seCumple(BigDecimal valor) {
            return (valorMinimo == null || valor.compareTo(valorMinimo) >= 0)
                    && (valorMaximo == null || valor.compareTo(valorMaximo) <= 0);
        }

        /** Umbral que se reporta en la alerta: el extremo del rango que la lectura cruzó. */
        public BigDecimal umbralReferencia() {
            return valorMinimo != null ? valorMinimo : valorMaximo;
        }

        public String redactar(Riesgo.SensorEvaluado sensor, String comunidad) {
            return mensaje
                    .replace("{valor}", Riesgo.compacto(sensor.valor()))
                    .replace("{unidad}", sensor.unidadMedida())
                    .replace("{sensor}", sensor.codigo())
                    .replace("{comunidad}", comunidad == null ? "" : comunidad);
        }
    }

    // ===============================================================================

    /**
     * Umbrales calibrados para un sensor concreto. Si un sensor no tiene fila, se usa la
     * plantilla de su tipo; la fila aparece cuando un administrador lo recalibra.
     */
    @Entity
    @Table(name = "umbrales_sensor")
    public static class UmbralSensor {

        @Id
        @Column(name = "sensor_id")
        public Integer sensorId;

        @Enumerated(EnumType.STRING)
        @Column(name = "tipo_sensor")
        public TipoSensor tipoSensor;

        @Column(name = "amarillo_alto")
        public BigDecimal amarilloAlto;
        @Column(name = "naranja_alto")
        public BigDecimal naranjaAlto;
        @Column(name = "rojo_alto")
        public BigDecimal rojoAlto;
        @Column(name = "amarillo_bajo")
        public BigDecimal amarilloBajo;
        @Column(name = "naranja_bajo")
        public BigDecimal naranjaBajo;
        @Column(name = "rojo_bajo")
        public BigDecimal rojoBajo;

        @Column(name = "fecha_modificacion")
        public Instant fechaModificacion = Instant.now();

        public Umbrales aUmbrales() {
            return new Umbrales(amarilloAlto, naranjaAlto, rojoAlto, amarilloBajo, naranjaBajo, rojoBajo);
        }

        public void aplicar(Umbrales u) {
            amarilloAlto = u.amarilloAlto();
            naranjaAlto = u.naranjaAlto();
            rojoAlto = u.rojoAlto();
            amarilloBajo = u.amarilloBajo();
            naranjaBajo = u.naranjaBajo();
            rojoBajo = u.rojoBajo();
            fechaModificacion = Instant.now();
        }
    }

    // ===============================================================================

    /** Aviso emitido para una comunidad. Una sola alerta abierta por fenómeno y comunidad. */
    @Entity
    @Table(name = "alertas")
    public static class Alerta {

        @Id
        @GeneratedValue(strategy = GenerationType.IDENTITY)
        public Long id;

        /**
         * Bloqueo optimista: el ciclo de evaluación (cada 3 s) y un operador que atiende o
         * cierra la alerta pueden tocar la misma fila a la vez; sin esto, el ciclo podría
         * pisar un cierre manual con una copia vieja.
         */
        @Version
        public long version;

        @Column(name = "comunidad_id")
        public Integer comunidadId;
        @Column(name = "comunidad_nombre")
        public String comunidadNombre;

        @Column(name = "sensor_id")
        public Integer sensorId;
        @Column(name = "sensor_codigo")
        public String sensorCodigo;
        @Column(name = "sensor_nombre")
        public String sensorNombre;
        @Column(name = "unidad_medida")
        public String unidadMedida;

        @Enumerated(EnumType.ORDINAL)
        public NivelAlerta nivel;

        @Enumerated(EnumType.ORDINAL)
        public TipoFenomeno fenomeno;

        public String mensaje;

        @Column(name = "valor_disparo")
        public BigDecimal valorDisparo;

        public BigDecimal umbral;

        @Column(name = "regla_id")
        public Long reglaId;
        @Column(name = "regla_nombre")
        public String reglaNombre;

        @Enumerated(EnumType.ORDINAL)
        public EstadoAlerta estado = EstadoAlerta.Activa;

        @Column(name = "fecha_hora")
        public Instant fechaHora = Instant.now();
        @Column(name = "fecha_cierre")
        public Instant fechaCierre;
        @Column(name = "fecha_modificacion")
        public Instant fechaModificacion;

        /** Evaluaciones seguidas en normalidad; al llegar al límite la alerta se cierra (histéresis). */
        @Column(name = "ciclos_sin_riesgo")
        public int ciclosSinRiesgo;

        public boolean reconocida;
        @Column(name = "reconocida_por_usuario_id")
        public Integer reconocidaPorUsuarioId;
        @Column(name = "reconocida_por")
        public String reconocidaPor;
        @Column(name = "fecha_reconocimiento")
        public Instant fechaReconocimiento;

        @Column(name = "cerrada_por_usuario_id")
        public Integer cerradaPorUsuarioId;
        @Column(name = "cerrada_por")
        public String cerradaPor;

        public boolean activa() {
            return fechaCierre == null;
        }

        public void atender(Integer usuarioId, String usuario, Instant ahora) {
            reconocida = true;
            reconocidaPorUsuarioId = usuarioId;
            reconocidaPor = usuario;
            fechaReconocimiento = ahora;
            estado = EstadoAlerta.Atendida;
            fechaModificacion = ahora;
        }

        /** Sin usuario significa que la cerró el sistema al normalizarse. */
        public void cerrar(Integer usuarioId, String usuario, Instant ahora) {
            fechaCierre = ahora;
            cerradaPorUsuarioId = usuarioId;
            cerradaPor = usuario;
            estado = EstadoAlerta.Cerrada;
            fechaModificacion = ahora;
        }
    }

    // ===============================================================================

    /** Asiento del historial de fenómenos (RF-REP-05). Acompaña a cada alerta desde que nace hasta que se cierra. */
    @Entity
    @Table(name = "eventos_historial")
    public static class EventoHistorial {

        @Id
        @GeneratedValue(strategy = GenerationType.IDENTITY)
        public Long id;

        @Column(name = "comunidad_id")
        public Integer comunidadId;
        @Column(name = "comunidad_nombre")
        public String comunidadNombre;

        @Column(name = "alerta_id")
        public Long alertaId;

        @Enumerated(EnumType.ORDINAL)
        public TipoFenomeno fenomeno;

        @Enumerated(EnumType.ORDINAL)
        @Column(name = "nivel_maximo")
        public NivelAlerta nivelMaximo;

        public String descripcion;

        /** Código y nombre del sensor, copiados: el historial debe leerse aunque el sensor ya no exista. */
        @Column(name = "origen_sensor")
        public String origenSensor;

        @Column(name = "sensor_id")
        public Integer sensorId;

        @Column(name = "valor_registrado")
        public BigDecimal valorRegistrado;

        @Enumerated(EnumType.ORDINAL)
        public EstadoAlerta estado = EstadoAlerta.Activa;

        @Column(name = "usuario_responsable_id")
        public Integer usuarioResponsableId;
        @Column(name = "usuario_responsable")
        public String usuarioResponsable;

        @Column(name = "fecha_inicio")
        public Instant fechaInicio = Instant.now();
        @Column(name = "fecha_fin")
        public Instant fechaFin;
        @Column(name = "fecha_modificacion")
        public Instant fechaModificacion;
    }
}
