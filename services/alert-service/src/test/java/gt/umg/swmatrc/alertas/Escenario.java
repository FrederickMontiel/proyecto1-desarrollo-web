package gt.umg.swmatrc.alertas;

import gt.umg.swmatrc.alertas.dominio.Enums.EstadoSensor;
import gt.umg.swmatrc.alertas.dominio.Enums.TipoSensor;
import gt.umg.swmatrc.alertas.dominio.Riesgo.ContextoEvaluacion;
import gt.umg.swmatrc.alertas.dominio.Riesgo.SensorEvaluado;
import gt.umg.swmatrc.alertas.dominio.Umbrales;
import java.math.BigDecimal;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.function.Function;
import java.util.stream.Collectors;

/**
 * Arma comunidades de prueba con la calibración real del sistema (las plantillas), igual que
 * el ConstructorEscenario de la Fase 2: así las pruebas validan los valores de producción.
 */
public final class Escenario {

    /** Rango físico y reposo de cada tipo, como los define Sensor Service. */
    private static final Map<TipoSensor, String[]> FISICO = Map.of(
            TipoSensor.Temperatura, new String[]{"-10", "50", "24", "°C"},
            TipoSensor.Humedad, new String[]{"0", "100", "62", "%"},
            TipoSensor.Viento, new String[]{"0", "150", "12", "km/h"},
            TipoSensor.Lluvia, new String[]{"0", "120", "1.5", "mm"},
            TipoSensor.NivelRio, new String[]{"0", "8", "1.8", "m"},
            TipoSensor.NivelReservorio, new String[]{"0", "100", "60", "%"},
            TipoSensor.Humo, new String[]{"0", "500", "12", "µg/m³"});

    private final List<SensorEvaluado> sensores = new ArrayList<>();
    private int siguienteId = 1;

    public static Escenario enCalma() {
        var e = new Escenario();
        for (TipoSensor t : List.of(TipoSensor.Temperatura, TipoSensor.Humedad, TipoSensor.Viento, TipoSensor.Lluvia, TipoSensor.NivelRio))
            e.con(t, new BigDecimal(FISICO.get(t)[2]));
        return e;
    }

    public Escenario con(TipoSensor tipo, BigDecimal valor) {
        return con(tipo, valor, EstadoSensor.Activo);
    }

    public Escenario con(TipoSensor tipo, BigDecimal valor, EstadoSensor estado) {
        String[] f = FISICO.get(tipo);
        sensores.add(new SensorEvaluado(siguienteId, tipo.name().substring(0, 3).toUpperCase() + "-" + siguienteId, tipo.name(),
                tipo, f[3], estado, valor, new BigDecimal(f[0]), new BigDecimal(f[1]), Instant.now(), Umbrales.plantilla(tipo)));
        siguienteId++;
        return this;
    }

    /** Reemplaza la lectura de una magnitud ya presente. */
    public Escenario ajustar(TipoSensor tipo, String valor) {
        for (int i = 0; i < sensores.size(); i++) {
            SensorEvaluado s = sensores.get(i);
            if (s.tipo() == tipo) {
                sensores.set(i, new SensorEvaluado(s.id(), s.codigo(), s.nombre(), s.tipo(), s.unidadMedida(), s.estado(),
                        new BigDecimal(valor), s.valorMinimo(), s.valorMaximo(), s.ultimaLectura(), s.umbrales()));
            }
        }
        return this;
    }

    public ContextoEvaluacion construir() {
        return new ContextoEvaluacion(1, "Comunidad de prueba", sensores);
    }

    public Map<Integer, SensorEvaluado> porId() {
        return sensores.stream().collect(Collectors.toMap(SensorEvaluado::id, Function.identity()));
    }
}
