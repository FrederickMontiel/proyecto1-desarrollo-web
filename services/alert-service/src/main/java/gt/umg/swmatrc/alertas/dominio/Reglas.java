package gt.umg.swmatrc.alertas.dominio;

import static gt.umg.swmatrc.alertas.dominio.Riesgo.f;

import gt.umg.swmatrc.alertas.dominio.Enums.NivelAlerta;
import gt.umg.swmatrc.alertas.dominio.Enums.TipoFenomeno;
import gt.umg.swmatrc.alertas.dominio.Enums.TipoSensor;
import gt.umg.swmatrc.alertas.dominio.Riesgo.ContextoEvaluacion;
import gt.umg.swmatrc.alertas.dominio.Riesgo.Diagnostico;
import gt.umg.swmatrc.alertas.dominio.Riesgo.ReglaRiesgo;
import gt.umg.swmatrc.alertas.dominio.Riesgo.SensorEvaluado;
import java.math.BigDecimal;
import java.math.RoundingMode;
import java.util.List;
import java.util.Optional;

/**
 * Las cinco reglas integradas del motor de riesgo, una por fenómeno climático, portadas de
 * la Fase 2 (SWMatrc.Domain.Riesgo.Reglas) con los mismos criterios y mensajes.
 */
public final class Reglas {

    private Reglas() {
    }

    public static List<ReglaRiesgo> todas() {
        return List.of(new Inundacion(), new Tormenta(), new Helada(), new Sequia(), new IncendioForestal());
    }

    private static final BigDecimal CIEN = new BigDecimal("100");

    // ===============================================================================

    /**
     * Crecida de río. El nivel del cauce manda; la lluvia actúa como agravante porque el
     * agua que ya está cayendo aún no se refleja en el nivel medido aguas abajo.
     */
    public static final class Inundacion implements ReglaRiesgo {

        public TipoFenomeno fenomeno() {
            return TipoFenomeno.Inundacion;
        }

        public int prioridad() {
            return 1;
        }

        public Diagnostico evaluar(ContextoEvaluacion contexto) {
            Optional<SensorEvaluado> rioOpt = contexto.obtener(TipoSensor.NivelRio);
            if (rioOpt.isEmpty()) return Diagnostico.normal(fenomeno());
            SensorEvaluado rio = rioOpt.get();

            NivelAlerta nivel = rio.clasificar();
            if (nivel == NivelAlerta.Verde) return Diagnostico.normal(fenomeno());

            // Lluvia intensa simultánea escala un peldaño: el caudal seguirá subiendo.
            boolean agravado = false;
            Optional<SensorEvaluado> lluvia = contexto.obtener(TipoSensor.Lluvia);
            if (lluvia.isPresent() && lluvia.get().clasificar().compareTo(NivelAlerta.Naranja) >= 0) {
                agravado = nivel != NivelAlerta.Rojo;
                if (agravado) nivel = nivel.escalar();
            }

            String valor = f(rio.valor(), 2) + " " + rio.unidadMedida();
            String mensaje = switch (nivel) {
                case Rojo -> "EMERGENCIA por inundación: el cauce alcanzó " + valor
                        + ", por encima del nivel de desbordamiento. Evacuar de inmediato las viviendas ribereñas.";
                case Naranja -> "Alerta de inundación: nivel del cauce en " + valor
                        + ". Preparar la evacuación de zonas bajas y despejar las rutas de salida.";
                default -> "Precaución por crecida: el cauce subió a " + valor
                        + ". Vigilar la evolución y avisar a las familias cercanas al río.";
            };
            if (agravado) mensaje += " Nivel escalado por lluvia intensa en curso.";

            return Diagnostico.de(fenomeno(), nivel, mensaje, rio.id(), rio.valor());
        }
    }

    // ===============================================================================

    /**
     * Tormenta o vendaval. Se dispara por viento fuerte y se agrava cuando además llueve:
     * la combinación de racha y precipitación es la que derriba techos y tendido eléctrico.
     */
    public static final class Tormenta implements ReglaRiesgo {

        public TipoFenomeno fenomeno() {
            return TipoFenomeno.Tormenta;
        }

        public int prioridad() {
            return 2;
        }

        public Diagnostico evaluar(ContextoEvaluacion contexto) {
            Optional<SensorEvaluado> vientoOpt = contexto.obtener(TipoSensor.Viento);
            Optional<SensorEvaluado> lluvia = contexto.obtener(TipoSensor.Lluvia);
            if (vientoOpt.isEmpty()) return Diagnostico.normal(fenomeno());
            SensorEvaluado viento = vientoOpt.get();

            NivelAlerta nivelViento = viento.clasificar();
            NivelAlerta nivelLluvia = lluvia.map(SensorEvaluado::clasificar).orElse(NivelAlerta.Verde);

            // Ni el viento solo ni la lluvia sola bastan si ambos están por debajo de precaución.
            if (nivelViento == NivelAlerta.Verde && nivelLluvia == NivelAlerta.Verde) return Diagnostico.normal(fenomeno());

            NivelAlerta nivel = NivelAlerta.maximo(nivelViento, nivelLluvia);

            // Viento y lluvia simultáneos por encima de precaución escalan un peldaño.
            if (nivelViento != NivelAlerta.Verde && nivelLluvia != NivelAlerta.Verde) nivel = nivel.escalar();

            String detalleLluvia = lluvia.map(l -> " y precipitación de " + f(l.valor(), 1) + " " + l.unidadMedida()).orElse("");
            String racha = f(viento.valor(), 1) + " " + viento.unidadMedida() + detalleLluvia;

            String mensaje = switch (nivel) {
                case Rojo -> "EMERGENCIA por tormenta: rachas de " + racha
                        + ". Suspender toda actividad al aire libre y resguardarse en estructuras seguras.";
                case Naranja -> "Alerta de tormenta: viento de " + racha
                        + ". Asegurar techos, cortar energía en zonas expuestas y proteger al ganado.";
                default -> "Precaución por tormenta: viento de " + racha
                        + ". Evitar desplazamientos innecesarios y retirar objetos sueltos.";
            };

            return Diagnostico.de(fenomeno(), nivel, mensaje, viento.id(), viento.valor());
        }
    }

    // ===============================================================================

    /**
     * Helada. Depende del extremo inferior de la temperatura (umbrales por defecto). La
     * humedad alta adelanta la formación de escarcha sobre el cultivo y agrava el diagnóstico.
     */
    public static final class Helada implements ReglaRiesgo {

        /** Por encima de esta humedad relativa la condensación acelera el daño por congelación. */
        static final BigDecimal HUMEDAD_CRITICA_ESCARCHA = new BigDecimal("85");

        public TipoFenomeno fenomeno() {
            return TipoFenomeno.Helada;
        }

        public int prioridad() {
            return 3;
        }

        public Diagnostico evaluar(ContextoEvaluacion contexto) {
            Optional<SensorEvaluado> temperaturaOpt = contexto.obtener(TipoSensor.Temperatura);
            if (temperaturaOpt.isEmpty()) return Diagnostico.normal(fenomeno());
            SensorEvaluado temperatura = temperaturaOpt.get();

            // Solo interesa el descenso: se ignoran los umbrales por exceso del sensor.
            NivelAlerta nivel = temperatura.umbrales().clasificarPorDefecto(temperatura.valor());
            if (nivel == NivelAlerta.Verde) return Diagnostico.normal(fenomeno());

            boolean escarcha = contexto.valor(TipoSensor.Humedad)
                    .map(h -> h.compareTo(HUMEDAD_CRITICA_ESCARCHA) >= 0).orElse(false);
            if (escarcha) nivel = nivel.escalar();

            String valor = f(temperatura.valor(), 1) + " " + temperatura.unidadMedida();
            String mensaje = switch (nivel) {
                case Rojo -> "EMERGENCIA por helada: " + valor + ". Pérdida total de cultivos sensibles "
                        + "en pocas horas. Proteger animales y activar los refugios comunitarios.";
                case Naranja -> "Alerta de helada: " + valor + ". Cubrir cultivos, encender fuentes de calor "
                        + "y resguardar el ganado joven.";
                default -> "Precaución por descenso térmico: " + valor + ". "
                        + "Preparar coberturas para los cultivos más sensibles.";
            };
            if (escarcha) mensaje += " La humedad elevada favorece la formación de escarcha.";

            return Diagnostico.de(fenomeno(), nivel, mensaje, temperatura.id(), temperatura.valor());
        }
    }

    // ===============================================================================

    /**
     * Déficit hídrico. Ninguna magnitud aislada define una sequía: se puntúa la
     * concurrencia de señales —lluvia escasa, aire seco, calor y cauce en mínimos— y el
     * total decide la severidad, como los índices de sequía multivariables.
     */
    public static final class Sequia implements ReglaRiesgo {

        // Por debajo de los valores de reposo de la red (1,5 mm de lluvia y 1,8 m de
        // cauce): si no, una jornada tranquila ya sumaría dos puntos.
        static final BigDecimal LLUVIA_ESCASA = new BigDecimal("0.5");
        static final BigDecimal HUMEDAD_BAJA = new BigDecimal("35");
        static final BigDecimal TEMPERATURA_ALTA = new BigDecimal("32");
        static final BigDecimal FACTOR_CAUCE_BAJO = new BigDecimal("0.15");

        public TipoFenomeno fenomeno() {
            return TipoFenomeno.Sequia;
        }

        public int prioridad() {
            return 4;
        }

        public Diagnostico evaluar(ContextoEvaluacion contexto) {
            Optional<BigDecimal> lluviaOpt = contexto.valor(TipoSensor.Lluvia);
            Optional<BigDecimal> humedadOpt = contexto.valor(TipoSensor.Humedad);

            // Sin lluvia ni humedad no hay evidencia suficiente para hablar de sequía.
            if (lluviaOpt.isEmpty() || humedadOpt.isEmpty()) return Diagnostico.normal(fenomeno());
            BigDecimal lluvia = lluviaOpt.get();
            BigDecimal humedad = humedadOpt.get();

            int puntos = 0;
            if (lluvia.compareTo(LLUVIA_ESCASA) <= 0) puntos++;
            if (humedad.compareTo(HUMEDAD_BAJA) <= 0) puntos++;
            if (contexto.valor(TipoSensor.Temperatura).map(t -> t.compareTo(TEMPERATURA_ALTA) >= 0).orElse(false)) puntos++;

            // Un cauce en mínimos confirma que el déficit ya afecta la disponibilidad de agua.
            Optional<SensorEvaluado> rio = contexto.obtener(TipoSensor.NivelRio);
            if (rio.isPresent()) {
                SensorEvaluado r = rio.get();
                BigDecimal cauceBajo = r.valorMinimo().add(r.valorMaximo().subtract(r.valorMinimo()).multiply(FACTOR_CAUCE_BAJO));
                if (r.valor().compareTo(cauceBajo) <= 0) puntos++;
            }

            NivelAlerta nivel = puntos >= 4 ? NivelAlerta.Rojo
                    : puntos == 3 ? NivelAlerta.Naranja
                    : puntos == 2 ? NivelAlerta.Amarillo
                    : NivelAlerta.Verde;
            if (nivel == NivelAlerta.Verde) return Diagnostico.normal(fenomeno());

            String mensaje = switch (nivel) {
                case Rojo -> "EMERGENCIA por sequía: precipitación de " + f(lluvia, 1) + " mm, humedad " + f(humedad, 0)
                        + " % y cauce en mínimos. Racionar el agua potable y habilitar el abastecimiento por cisterna.";
                case Naranja -> "Alerta por sequía: precipitación de " + f(lluvia, 1) + " mm con humedad de " + f(humedad, 0)
                        + " %. Restringir el riego y priorizar el consumo humano y del ganado.";
                default -> "Precaución por déficit hídrico: precipitación de " + f(lluvia, 1) + " mm y humedad de "
                        + f(humedad, 0) + " %. Iniciar el ahorro de agua y vigilar las reservas.";
            };

            Integer sensorId = contexto.obtener(TipoSensor.Lluvia).map(SensorEvaluado::id).orElse(null);
            return Diagnostico.de(fenomeno(), nivel, mensaje, sensorId, lluvia);
        }
    }

    // ===============================================================================

    /**
     * Peligro de incendio forestal. Índice 0–100 inspirado en los sistemas de peligro
     * meteorológico (tipo FWI): el calor y el aire seco preparan el combustible, el viento
     * propaga el fuego y la lluvia reciente lo suprime.
     */
    public static final class IncendioForestal implements ReglaRiesgo {

        static final BigDecimal TEMP_BASE = new BigDecimal("20");
        static final BigDecimal TEMP_TOPE = new BigDecimal("45");
        static final BigDecimal VIENTO_TOPE = new BigDecimal("60");
        static final BigDecimal PESO_TEMPERATURA = new BigDecimal("0.35");
        static final BigDecimal PESO_SEQUEDAD = new BigDecimal("0.40");
        static final BigDecimal PESO_VIENTO = new BigDecimal("0.25");
        /** Precipitación a partir de la cual el combustible fino deja de estar disponible. */
        static final BigDecimal LLUVIA_SUPRESORA = new BigDecimal("5");

        public TipoFenomeno fenomeno() {
            return TipoFenomeno.IncendioForestal;
        }

        public int prioridad() {
            return 5;
        }

        public Diagnostico evaluar(ContextoEvaluacion contexto) {
            Optional<BigDecimal> temperaturaOpt = contexto.valor(TipoSensor.Temperatura);
            Optional<BigDecimal> humedadOpt = contexto.valor(TipoSensor.Humedad);
            BigDecimal viento = contexto.valor(TipoSensor.Viento).orElse(BigDecimal.ZERO);
            BigDecimal lluvia = contexto.valor(TipoSensor.Lluvia).orElse(BigDecimal.ZERO);

            if (temperaturaOpt.isEmpty() || humedadOpt.isEmpty()) return Diagnostico.normal(fenomeno());
            // Lluvia significativa: el combustible está mojado, no hay peligro que reportar.
            if (lluvia.compareTo(LLUVIA_SUPRESORA) >= 0) return Diagnostico.normal(fenomeno());

            BigDecimal temperatura = temperaturaOpt.get();
            BigDecimal humedad = humedadOpt.get();

            BigDecimal fTemp = normalizar(temperatura, TEMP_BASE, TEMP_TOPE);
            BigDecimal fSequedad = normalizar(CIEN.subtract(humedad), BigDecimal.ZERO, CIEN);
            BigDecimal fViento = normalizar(viento, BigDecimal.ZERO, VIENTO_TOPE);

            BigDecimal indice = fTemp.multiply(PESO_TEMPERATURA)
                    .add(fSequedad.multiply(PESO_SEQUEDAD))
                    .add(fViento.multiply(PESO_VIENTO))
                    .multiply(CIEN);

            NivelAlerta nivel = indice.compareTo(new BigDecimal("80")) >= 0 ? NivelAlerta.Rojo
                    : indice.compareTo(new BigDecimal("65")) >= 0 ? NivelAlerta.Naranja
                    : indice.compareTo(new BigDecimal("50")) >= 0 ? NivelAlerta.Amarillo
                    : NivelAlerta.Verde;
            if (nivel == NivelAlerta.Verde) return Diagnostico.normal(fenomeno());

            String condiciones = f(temperatura, 1) + " °C, humedad " + f(humedad, 0) + " % y viento " + f(viento, 1) + " km/h";
            String i = f(indice, 0);
            String mensaje = switch (nivel) {
                case Rojo -> "EMERGENCIA por incendio forestal: índice de peligro " + i + " sobre 100 (" + condiciones + "). "
                        + "Prohibir toda quema, activar brigadas y preparar la evacuación de caseríos cercanos al bosque.";
                case Naranja -> "Alerta de incendio forestal: índice de peligro " + i + " sobre 100 (" + condiciones + "). "
                        + "Suspender quemas agrícolas y mantener las brigadas en disponibilidad.";
                default -> "Precaución por riesgo de incendio: índice de peligro " + i + " sobre 100 (" + condiciones + "). "
                        + "Evitar fuegos abiertos y vigilar los linderos con vegetación seca.";
            };

            Integer sensorId = contexto.obtener(TipoSensor.Temperatura).map(SensorEvaluado::id).orElse(null);
            return Diagnostico.de(fenomeno(), nivel, mensaje, sensorId, indice.setScale(1, RoundingMode.HALF_UP));
        }

        /** Lleva un valor a la escala 0–1 recortándolo contra sus extremos. */
        static BigDecimal normalizar(BigDecimal valor, BigDecimal minimo, BigDecimal maximo) {
            if (maximo.compareTo(minimo) <= 0) return BigDecimal.ZERO;
            BigDecimal escalado = valor.subtract(minimo).divide(maximo.subtract(minimo), 10, RoundingMode.HALF_UP);
            return escalado.max(BigDecimal.ZERO).min(BigDecimal.ONE);
        }
    }
}
