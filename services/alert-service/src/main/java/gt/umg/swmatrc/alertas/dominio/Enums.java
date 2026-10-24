package gt.umg.swmatrc.alertas.dominio;

/**
 * Catálogos del dominio. Los nombres coinciden con los que usan Sensor Service y el frontend
 * (viajan como texto en JSON). En la base, nivel, fenómeno y estado se guardan por su
 * ordinal: es el mismo número que tenían en la Fase 2 y además permite ordenar por severidad.
 */
public final class Enums {

    private Enums() {
    }

    /** Magnitud física que mide un sensor. */
    public enum TipoSensor {
        Temperatura, Humedad, Viento, Lluvia, NivelRio, NivelReservorio, Humo, Otro
    }

    /** Estado operativo de un sensor, según lo reporta Sensor Service. */
    public enum EstadoSensor {
        Inactivo, Activo, SinSenal
    }

    /** Código de color del protocolo de alerta temprana. El orden es significativo: a mayor ordinal, mayor severidad. */
    public enum NivelAlerta {
        Verde, Amarillo, Naranja, Rojo;

        public boolean esMasGraveQue(NivelAlerta otro) {
            return compareTo(otro) > 0;
        }

        /** Un peldaño más grave, sin pasar de rojo. */
        public NivelAlerta escalar() {
            return this == Rojo ? Rojo : values()[ordinal() + 1];
        }

        public static NivelAlerta maximo(NivelAlerta a, NivelAlerta b) {
            return a.compareTo(b) >= 0 ? a : b;
        }
    }

    /** Fenómeno hidrometeorológico. {@code Ninguno} existe por compatibilidad con la Fase 2. */
    public enum TipoFenomeno {
        Ninguno, Inundacion, Sequia, Tormenta, Helada, IncendioForestal
    }

    /** Ciclo de vida de una alerta: nace activa, un operador la atiende y se cierra al normalizarse o a mano. */
    public enum EstadoAlerta {
        Activa, Atendida, Cerrada
    }
}
