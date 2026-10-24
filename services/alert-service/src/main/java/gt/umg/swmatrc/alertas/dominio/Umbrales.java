package gt.umg.swmatrc.alertas.dominio;

import gt.umg.swmatrc.alertas.dominio.Enums.NivelAlerta;
import gt.umg.swmatrc.alertas.dominio.Enums.TipoSensor;
import java.math.BigDecimal;
import java.util.EnumMap;
import java.util.Map;

/**
 * Escalera de umbrales de un sensor. Por exceso (el riesgo crece cuando el valor sube:
 * crecida, viento, calor) y por defecto (cuando baja: helada, sequía). Cualquier peldaño
 * puede faltar.
 */
public record Umbrales(
        BigDecimal amarilloAlto,
        BigDecimal naranjaAlto,
        BigDecimal rojoAlto,
        BigDecimal amarilloBajo,
        BigDecimal naranjaBajo,
        BigDecimal rojoBajo) {

    public static final Umbrales NINGUNO = new Umbrales(null, null, null, null, null, null);

    /**
     * Nivel más severo que cumple el valor. Evalúa de rojo a amarillo para quedarse con el
     * peor caso. Los umbrales son inclusivos.
     */
    public NivelAlerta clasificar(BigDecimal valor) {
        if (alcanza(valor, rojoAlto) || cae(valor, rojoBajo)) return NivelAlerta.Rojo;
        if (alcanza(valor, naranjaAlto) || cae(valor, naranjaBajo)) return NivelAlerta.Naranja;
        if (alcanza(valor, amarilloAlto) || cae(valor, amarilloBajo)) return NivelAlerta.Amarillo;
        return NivelAlerta.Verde;
    }

    /** Nivel considerando solo los umbrales por defecto (la helada ignora el calor). */
    public NivelAlerta clasificarPorDefecto(BigDecimal valor) {
        if (cae(valor, rojoBajo)) return NivelAlerta.Rojo;
        if (cae(valor, naranjaBajo)) return NivelAlerta.Naranja;
        if (cae(valor, amarilloBajo)) return NivelAlerta.Amarillo;
        return NivelAlerta.Verde;
    }

    /** Umbral que corresponde a un nivel, en el sentido que cruzó el valor. */
    public BigDecimal umbralPara(NivelAlerta nivel, BigDecimal valor) {
        BigDecimal alto = switch (nivel) {
            case Rojo -> rojoAlto;
            case Naranja -> naranjaAlto;
            case Amarillo -> amarilloAlto;
            case Verde -> null;
        };
        BigDecimal bajo = switch (nivel) {
            case Rojo -> rojoBajo;
            case Naranja -> naranjaBajo;
            case Amarillo -> amarilloBajo;
            case Verde -> null;
        };
        if (alcanza(valor, alto)) return alto;
        if (cae(valor, bajo)) return bajo;
        return alto != null ? alto : bajo;
    }

    /** Comprueba que la escalera sea coherente. Devuelve el error o {@code null}. */
    public String validar() {
        if (amarilloAlto != null && naranjaAlto != null && rojoAlto != null
                && !(amarilloAlto.compareTo(naranjaAlto) < 0 && naranjaAlto.compareTo(rojoAlto) < 0)) {
            return "Los umbrales por exceso deben cumplir amarillo < naranja < rojo.";
        }
        if (amarilloBajo != null && naranjaBajo != null && rojoBajo != null
                && !(amarilloBajo.compareTo(naranjaBajo) > 0 && naranjaBajo.compareTo(rojoBajo) > 0)) {
            return "Los umbrales por defecto deben cumplir amarillo > naranja > rojo.";
        }
        return null;
    }

    private static boolean alcanza(BigDecimal valor, BigDecimal umbral) {
        return umbral != null && valor.compareTo(umbral) >= 0;
    }

    private static boolean cae(BigDecimal valor, BigDecimal umbral) {
        return umbral != null && valor.compareTo(umbral) <= 0;
    }

    // -------------------------------------------------------------------------------
    // Calibración recomendada por tipo, la misma de la Fase 2. Sigue las escalas de aviso
    // de los servicios meteorológicos: viento en escala Beaufort, precipitación por
    // intensidad horaria y nivel de cauce respecto a la cota de desbordamiento.
    // -------------------------------------------------------------------------------

    private static final Map<TipoSensor, Umbrales> PLANTILLAS = new EnumMap<>(TipoSensor.class);

    static {
        // Calor extremo por arriba; riesgo de helada por abajo.
        PLANTILLAS.put(TipoSensor.Temperatura, de("33", "38", "42", "5", "2", "0"));
        // Solo la humedad baja es relevante para el riesgo (sequía e incendio).
        PLANTILLAS.put(TipoSensor.Humedad, de(null, null, null, "35", "25", "15"));
        // Beaufort: 39 km/h temporal, 62 km/h vendaval, 89 km/h temporal duro.
        PLANTILLAS.put(TipoSensor.Viento, de("39", "62", "89", null, null, null));
        // Precipitación acumulada en la última hora.
        PLANTILLAS.put(TipoSensor.Lluvia, de("15", "30", "60", null, null, null));
        // 4.5 m es la cota de desbordamiento de referencia.
        PLANTILLAS.put(TipoSensor.NivelRio, de("3.0", "3.8", "4.5", null, null, null));
        // Exceso anuncia desbordamiento; defecto, desabastecimiento.
        PLANTILLAS.put(TipoSensor.NivelReservorio, de("85", "92", "97", "30", "20", "10"));
        // PM2.5 según el índice de calidad del aire.
        PLANTILLAS.put(TipoSensor.Humo, de("55", "150", "250", null, null, null));
    }

    public static Umbrales plantilla(TipoSensor tipo) {
        return PLANTILLAS.getOrDefault(tipo, NINGUNO);
    }

    public static Map<TipoSensor, Umbrales> plantillas() {
        Map<TipoSensor, Umbrales> todas = new EnumMap<>(TipoSensor.class);
        for (TipoSensor tipo : TipoSensor.values()) todas.put(tipo, plantilla(tipo));
        return todas;
    }

    private static Umbrales de(String aa, String na, String ra, String ab, String nb, String rb) {
        return new Umbrales(num(aa), num(na), num(ra), num(ab), num(nb), num(rb));
    }

    private static BigDecimal num(String s) {
        return s == null ? null : new BigDecimal(s);
    }
}
