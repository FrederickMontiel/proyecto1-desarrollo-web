package gt.umg.swmatrc.alertas.dominio;

import gt.umg.swmatrc.alertas.dominio.Enums.EstadoSensor;
import gt.umg.swmatrc.alertas.dominio.Enums.NivelAlerta;
import gt.umg.swmatrc.alertas.dominio.Enums.TipoFenomeno;
import gt.umg.swmatrc.alertas.dominio.Enums.TipoSensor;
import java.math.BigDecimal;
import java.math.RoundingMode;
import java.time.Instant;
import java.util.Comparator;
import java.util.EnumMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Optional;
import java.util.stream.Collectors;

/** Piezas del motor de riesgo, portadas de la Fase 2 (SWMatrc.Domain.Riesgo). */
public final class Riesgo {

    private Riesgo() {
    }

    /**
     * Un sensor tal como lo fotografió Sensor Service, con los umbrales que le corresponden
     * en este servicio.
     */
    public record SensorEvaluado(
            int id,
            String codigo,
            String nombre,
            TipoSensor tipo,
            String unidadMedida,
            EstadoSensor estado,
            BigDecimal valor,
            BigDecimal valorMinimo,
            BigDecimal valorMaximo,
            Instant ultimaLectura,
            Umbrales umbrales) {

        /** Solo un sensor en servicio alimenta la evaluación. */
        public boolean operativo() {
            return estado == EstadoSensor.Activo;
        }

        public NivelAlerta clasificar() {
            return umbrales.clasificar(valor);
        }
    }

    /**
     * Fotografía de una comunidad en un instante. Da acceso por tipo de magnitud para que
     * las reglas se lean como su enunciado meteorológico y no como recorridos de listas.
     */
    public static final class ContextoEvaluacion {

        private final int comunidadId;
        private final String comunidadNombre;
        private final List<SensorEvaluado> sensores;
        private final Map<TipoSensor, SensorEvaluado> porTipo;

        public ContextoEvaluacion(int comunidadId, String comunidadNombre, List<SensorEvaluado> todos) {
            this.comunidadId = comunidadId;
            this.comunidadNombre = comunidadNombre;
            this.sensores = todos.stream().filter(SensorEvaluado::operativo).toList();
            // Con varios sensores de la misma magnitud se toma el más reciente: es la
            // lectura que mejor representa la situación actual.
            // EnumMap se arma vacío y luego se llena: su constructor de copia falla si la
            // comunidad no tiene ningún sensor operativo.
            this.porTipo = new EnumMap<>(TipoSensor.class);
            this.porTipo.putAll(sensores.stream().collect(Collectors.toMap(
                    SensorEvaluado::tipo,
                    s -> s,
                    (a, b) -> instante(a).isAfter(instante(b)) ? a : b)));
        }

        private static Instant instante(SensorEvaluado s) {
            return s.ultimaLectura() == null ? Instant.MIN : s.ultimaLectura();
        }

        public int comunidadId() {
            return comunidadId;
        }

        public String comunidadNombre() {
            return comunidadNombre;
        }

        /** Sensores operativos. */
        public List<SensorEvaluado> sensores() {
            return sensores;
        }

        public Optional<SensorEvaluado> obtener(TipoSensor tipo) {
            return Optional.ofNullable(porTipo.get(tipo));
        }

        /** Valor actual de una magnitud, o vacío si ningún sensor operativo la mide. */
        public Optional<BigDecimal> valor(TipoSensor tipo) {
            return obtener(tipo).map(SensorEvaluado::valor);
        }
    }

    /**
     * Veredicto de una regla sobre una comunidad.
     *
     * @param sensorId    sensor disparador; nulo si la regla combina varias magnitudes
     * @param reglaId     regla configurable que lo produjo; nulo para las integradas
     * @param umbral      umbral cruzado, si se conoce
     */
    public record Diagnostico(
            TipoFenomeno fenomeno,
            NivelAlerta nivel,
            String mensaje,
            Integer sensorId,
            BigDecimal valorDisparo,
            Long reglaId,
            String reglaNombre,
            BigDecimal umbral) {

        public static Diagnostico normal(TipoFenomeno fenomeno) {
            return new Diagnostico(fenomeno, NivelAlerta.Verde, "Condiciones dentro de parámetros normales.",
                    null, null, null, null, null);
        }

        public static Diagnostico de(TipoFenomeno fenomeno, NivelAlerta nivel, String mensaje, Integer sensorId, BigDecimal valor) {
            return new Diagnostico(fenomeno, nivel, mensaje, sensorId, valor, null, null, null);
        }

        public boolean esRiesgo() {
            return nivel.esMasGraveQue(NivelAlerta.Verde);
        }

        public Diagnostico conReglaNombre(String nombre) {
            return new Diagnostico(fenomeno, nivel, mensaje, sensorId, valorDisparo, reglaId, nombre, umbral);
        }
    }

    /**
     * Regla de decisión para un fenómeno. Incorporar una amenaza nueva (deslizamiento,
     * granizada) es agregar una clase y registrarla, sin tocar el motor ni las demás.
     */
    public interface ReglaRiesgo {

        TipoFenomeno fenomeno();

        /** Orden de presentación cuando varias reglas disparan al mismo nivel. */
        int prioridad();

        Diagnostico evaluar(ContextoEvaluacion contexto);
    }

    /** Ejecuta las reglas integradas y devuelve los riesgos, del más grave al menos grave. */
    public static final class MotorRiesgo {

        private final List<ReglaRiesgo> reglas;

        public MotorRiesgo(List<? extends ReglaRiesgo> reglas) {
            this.reglas = reglas.stream().sorted(Comparator.comparingInt(ReglaRiesgo::prioridad)).map(r -> (ReglaRiesgo) r).toList();
        }

        public List<Diagnostico> evaluar(ContextoEvaluacion contexto) {
            return reglas.stream()
                    .map(regla -> {
                        Diagnostico d = regla.evaluar(contexto);
                        // Toda alerta debe poder rastrearse hasta la regla que la originó.
                        return d.reglaNombre() == null ? d.conReglaNombre("Regla integrada: " + regla.fenomeno()) : d;
                    })
                    .filter(Diagnostico::esRiesgo)
                    .sorted(Comparator.comparing(Diagnostico::nivel).reversed()
                            .thenComparingInt(d -> prioridadDe(d.fenomeno())))
                    .toList();
        }

        private int prioridadDe(TipoFenomeno fenomeno) {
            return reglas.stream().filter(r -> r.fenomeno() == fenomeno).mapToInt(ReglaRiesgo::prioridad).findFirst().orElse(99);
        }

        public static NivelAlerta nivelGlobal(List<Diagnostico> diagnosticos) {
            return diagnosticos.stream().map(Diagnostico::nivel).max(Comparator.naturalOrder()).orElse(NivelAlerta.Verde);
        }
    }

    // -------------------------------------------------------------------------------
    // Formato de números en los mensajes. Siempre con punto decimal, igual que la Fase 2
    // en el contenedor (cultura invariante).
    // -------------------------------------------------------------------------------

    public static String f(BigDecimal valor, int decimales) {
        return String.format(Locale.ROOT, "%." + decimales + "f", valor);
    }

    /** Equivale al formato "0.##" de .NET: hasta dos decimales, sin ceros a la derecha. */
    public static String compacto(BigDecimal valor) {
        BigDecimal redondeado = valor.setScale(2, RoundingMode.HALF_UP).stripTrailingZeros();
        return redondeado.scale() < 0 ? redondeado.setScale(0).toPlainString() : redondeado.toPlainString();
    }
}
