package gt.umg.swmatrc.alertas.aplicacion;

import gt.umg.swmatrc.alertas.dominio.Enums.EstadoAlerta;
import gt.umg.swmatrc.alertas.dominio.Enums.TipoFenomeno;
import gt.umg.swmatrc.alertas.dominio.Riesgo.ContextoEvaluacion;
import gt.umg.swmatrc.alertas.dominio.Riesgo.Diagnostico;
import gt.umg.swmatrc.alertas.dominio.Riesgo.MotorRiesgo;
import gt.umg.swmatrc.alertas.dominio.Riesgo.SensorEvaluado;
import gt.umg.swmatrc.alertas.persistencia.Entidades.Alerta;
import gt.umg.swmatrc.alertas.persistencia.Entidades.ReglaAlerta;
import java.math.BigDecimal;
import java.time.Instant;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import java.util.stream.Collectors;

/**
 * Lógica pura de la evaluación: no toca la base ni Spring, para poder probarla sola. El
 * servicio carga los datos, llama aquí y persiste el resultado.
 */
public final class Evaluacion {

    private Evaluacion() {
    }

    /**
     * Evaluaciones seguidas en normalidad antes de cerrar una alerta. Con el ciclo de 3 s son
     * unos quince segundos: suficiente para distinguir una mejora real del ruido en torno al umbral.
     */
    public static final int CICLOS_PARA_CIERRE = 5;

    // ===============================================================================

    /**
     * Combina las reglas integradas con las configurables. Queda un diagnóstico por
     * fenómeno —el más severo— porque se abre una sola alerta por fenómeno. Ante empate de
     * nivel gana la configurada (la definió el administrador a propósito) y, entre varias,
     * la de umbral más cercano al valor medido: con 450 µg/m³, "≥ 400" describe mejor la
     * situación que "≥ 250".
     */
    public static List<Diagnostico> diagnosticar(MotorRiesgo motor, ContextoEvaluacion contexto, List<ReglaAlerta> reglas) {
        List<Diagnostico> todos = new ArrayList<>(motor.evaluar(contexto));

        for (SensorEvaluado sensor : contexto.sensores()) {
            for (ReglaAlerta regla : reglas) {
                if (regla.activa && regla.tipoSensor == sensor.tipo() && regla.seCumple(sensor.valor())) {
                    todos.add(new Diagnostico(regla.fenomeno, regla.nivel, regla.redactar(sensor, contexto.comunidadNombre()),
                            sensor.id(), sensor.valor(), regla.id, regla.nombre, regla.umbralReferencia()));
                }
            }
        }

        Comparator<Diagnostico> preferencia = Comparator
                .comparing(Diagnostico::nivel).reversed()
                .thenComparing(d -> d.reglaId() == null)                       // configurada primero
                .thenComparing(Evaluacion::distanciaAlUmbral)
                .thenComparing(d -> d.reglaId() == null ? Long.MAX_VALUE : d.reglaId());

        return todos.stream()
                .collect(Collectors.groupingBy(Diagnostico::fenomeno))
                .values().stream()
                .map(grupo -> grupo.stream().sorted(preferencia).findFirst().orElseThrow())
                .sorted(Comparator.comparing(Diagnostico::nivel).reversed())
                .toList();
    }

    private static BigDecimal distanciaAlUmbral(Diagnostico d) {
        return d.umbral() != null && d.valorDisparo() != null
                ? d.valorDisparo().subtract(d.umbral()).abs()
                : new BigDecimal("1e12");
    }

    // ===============================================================================

    /** Qué pasó con las alertas de una comunidad en una evaluación. */
    public record Resultado(List<Alerta> nuevas, List<Alerta> escaladas, List<Alerta> cerradas) {

        public boolean hayCambios() {
            return !nuevas.isEmpty() || !escaladas.isEmpty() || !cerradas.isEmpty();
        }
    }

    /**
     * Ajusta las alertas abiertas al diagnóstico vigente. Una alerta se levanta una sola vez
     * por episodio y se mantiene mientras la condición persista: el operador ve un aviso por
     * fenómeno y no una avalancha de repeticiones cada pocos segundos.
     *
     * <p>Modifica las alertas de {@code abiertas} en el sitio; las nuevas vuelven sin id.
     */
    public static Resultado conciliar(ContextoEvaluacion contexto, List<Diagnostico> diagnosticos,
                                      List<Alerta> abiertas, Map<Integer, SensorEvaluado> sensores, Instant ahora) {
        List<Alerta> nuevas = new ArrayList<>();
        List<Alerta> escaladas = new ArrayList<>();
        List<Alerta> cerradas = new ArrayList<>();

        for (Diagnostico diagnostico : diagnosticos) {
            Optional<Alerta> existente = abiertas.stream().filter(a -> a.fenomeno == diagnostico.fenomeno()).findFirst();

            if (existente.isEmpty()) {
                Alerta alerta = new Alerta();
                alerta.comunidadId = contexto.comunidadId();
                alerta.comunidadNombre = contexto.comunidadNombre();
                alerta.nivel = diagnostico.nivel();
                alerta.fenomeno = diagnostico.fenomeno();
                alerta.estado = EstadoAlerta.Activa;
                alerta.fechaHora = ahora;
                refrescar(alerta, diagnostico, sensores);
                nuevas.add(alerta);
                continue;
            }

            Alerta abierta = existente.get();
            if (diagnostico.nivel().esMasGraveQue(abierta.nivel)) {
                // El episodio empeoró: se vuelve a avisar y se retira el acuse anterior,
                // porque lo que el operador reconoció ya no describe la situación.
                abierta.nivel = diagnostico.nivel();
                abierta.reconocida = false;
                abierta.reconocidaPorUsuarioId = null;
                abierta.reconocidaPor = null;
                abierta.fechaReconocimiento = null;
                abierta.estado = EstadoAlerta.Activa;
                escaladas.add(abierta);
            }

            // Aunque no cambie el color, texto, valor y regla reflejan la medición más reciente.
            refrescar(abierta, diagnostico, sensores);
            abierta.fechaModificacion = ahora;
            // La condición sigue presente: se reinicia la cuenta atrás para el cierre.
            abierta.ciclosSinRiesgo = 0;
        }

        // Cierre con histéresis: una alerta no se retira en cuanto la lectura baja del umbral,
        // debe mantenerse en normalidad varias evaluaciones seguidas. Sin esto, un valor
        // oscilando en torno al umbral produciría una ráfaga de aperturas y cierres.
        Set<TipoFenomeno> vigentes = diagnosticos.stream().map(Diagnostico::fenomeno).collect(Collectors.toSet());
        for (Alerta alerta : abiertas) {
            if (vigentes.contains(alerta.fenomeno)) continue;
            alerta.ciclosSinRiesgo++;
            alerta.fechaModificacion = ahora;
            if (alerta.ciclosSinRiesgo >= CICLOS_PARA_CIERRE) {
                alerta.cerrar(null, null, ahora);
                cerradas.add(alerta);
            }
        }

        return new Resultado(nuevas, escaladas, cerradas);
    }

    private static void refrescar(Alerta alerta, Diagnostico d, Map<Integer, SensorEvaluado> sensores) {
        alerta.mensaje = d.mensaje();
        alerta.valorDisparo = d.valorDisparo();
        alerta.reglaId = d.reglaId();
        if (d.reglaNombre() != null) alerta.reglaNombre = d.reglaNombre();
        if (alerta.reglaNombre == null) alerta.reglaNombre = "Regla integrada";

        SensorEvaluado sensor = d.sensorId() == null ? null : sensores.get(d.sensorId());
        if (sensor != null) {
            alerta.sensorId = sensor.id();
            alerta.sensorCodigo = sensor.codigo();
            alerta.sensorNombre = sensor.nombre();
            alerta.unidadMedida = sensor.unidadMedida();
        }

        BigDecimal umbral = umbralDe(d, sensor);
        if (umbral != null) alerta.umbral = umbral;
    }

    /**
     * Umbral que se reporta en la alerta. Las reglas configurables lo traen consigo; para las
     * integradas se toma el umbral del sensor que corresponde a la lectura.
     */
    static BigDecimal umbralDe(Diagnostico d, SensorEvaluado sensor) {
        if (d.umbral() != null) return d.umbral();
        if (sensor == null || d.valorDisparo() == null) return null;
        return sensor.umbrales().umbralPara(sensor.umbrales().clasificar(sensor.valor()), sensor.valor());
    }
}
