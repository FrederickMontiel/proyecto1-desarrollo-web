package gt.umg.swmatrc.alertas;

import static org.assertj.core.api.Assertions.assertThat;

import gt.umg.swmatrc.alertas.dominio.Enums.EstadoSensor;
import gt.umg.swmatrc.alertas.dominio.Enums.NivelAlerta;
import gt.umg.swmatrc.alertas.dominio.Enums.TipoFenomeno;
import gt.umg.swmatrc.alertas.dominio.Enums.TipoSensor;
import gt.umg.swmatrc.alertas.dominio.Reglas;
import gt.umg.swmatrc.alertas.dominio.Riesgo.Diagnostico;
import gt.umg.swmatrc.alertas.dominio.Riesgo.MotorRiesgo;
import gt.umg.swmatrc.alertas.dominio.Umbrales;
import java.math.BigDecimal;
import java.util.List;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

/** Casos de la Fase 2 (MotorRiesgoTests y SensorTests), portados a Java. */
class MotorRiesgoTest {

    private final MotorRiesgo motor = new MotorRiesgo(Reglas.todas());

    private Diagnostico unico(Escenario e, TipoFenomeno fenomeno) {
        return motor.evaluar(e.construir()).stream().filter(d -> d.fenomeno() == fenomeno).findFirst().orElseThrow();
    }

    @Test
    void enCalma_noEmiteNingunaAlerta() {
        // Es el caso que más fácilmente se rompe al recalibrar: un sistema que avisa en un
        // día tranquilo deja de ser creíble y se acaba ignorando.
        assertThat(motor.evaluar(Escenario.enCalma().construir())).isEmpty();
    }

    @Test
    void cauceSobreLaCotaDeDesbordamiento_declaraEmergenciaPorInundacion() {
        var d = unico(Escenario.enCalma().ajustar(TipoSensor.NivelRio, "4.8"), TipoFenomeno.Inundacion);

        assertThat(d.nivel()).isEqualTo(NivelAlerta.Rojo);
        assertThat(d.mensaje()).contains("EMERGENCIA").contains("4.80 m");
    }

    @Test
    void lluviaIntensaSimultanea_escalaLaInundacionUnPeldano() {
        var sinAgravante = unico(Escenario.enCalma().ajustar(TipoSensor.NivelRio, "3.2"), TipoFenomeno.Inundacion);
        var agravada = unico(Escenario.enCalma().ajustar(TipoSensor.NivelRio, "3.2").ajustar(TipoSensor.Lluvia, "35"),
                TipoFenomeno.Inundacion);

        assertThat(sinAgravante.nivel()).isEqualTo(NivelAlerta.Amarillo);
        assertThat(agravada.nivel()).isEqualTo(NivelAlerta.Naranja);
        assertThat(agravada.mensaje()).contains("escalado por lluvia");
    }

    @Test
    void temperaturaBajoCero_declaraEmergenciaPorHelada() {
        var d = unico(Escenario.enCalma().ajustar(TipoSensor.Temperatura, "-1").ajustar(TipoSensor.Humedad, "50"),
                TipoFenomeno.Helada);

        assertThat(d.nivel()).isEqualTo(NivelAlerta.Rojo);
    }

    @Test
    void humedadAltaConTemperaturaBaja_agravaLaHeladaPorEscarcha() {
        var d = unico(Escenario.enCalma().ajustar(TipoSensor.Temperatura, "4").ajustar(TipoSensor.Humedad, "90"),
                TipoFenomeno.Helada);

        assertThat(d.nivel()).isEqualTo(NivelAlerta.Naranja);
        assertThat(d.mensaje()).contains("escarcha");
    }

    @Test
    void calorSequedadYViento_declaranPeligroDeIncendio() {
        var d = unico(Escenario.enCalma().ajustar(TipoSensor.Temperatura, "42").ajustar(TipoSensor.Humedad, "10")
                .ajustar(TipoSensor.Viento, "45").ajustar(TipoSensor.Lluvia, "0"), TipoFenomeno.IncendioForestal);

        assertThat(d.nivel()).isEqualTo(NivelAlerta.Rojo);
        assertThat(d.mensaje()).contains("índice de peligro");
    }

    @Test
    void lluviaReciente_suprimeElPeligroDeIncendio() {
        var diagnosticos = motor.evaluar(Escenario.enCalma().ajustar(TipoSensor.Temperatura, "42")
                .ajustar(TipoSensor.Humedad, "10").ajustar(TipoSensor.Viento, "45").ajustar(TipoSensor.Lluvia, "8").construir());

        assertThat(diagnosticos).noneMatch(d -> d.fenomeno() == TipoFenomeno.IncendioForestal);
    }

    @Test
    void sequedadSostenidaConCauceEnMinimos_declaraEmergenciaPorSequia() {
        var d = unico(Escenario.enCalma().ajustar(TipoSensor.Lluvia, "0").ajustar(TipoSensor.Humedad, "20")
                .ajustar(TipoSensor.Temperatura, "36").ajustar(TipoSensor.NivelRio, "0.6"), TipoFenomeno.Sequia);

        assertThat(d.nivel()).isEqualTo(NivelAlerta.Rojo);
    }

    @Test
    void vientoHuracanado_declaraEmergenciaPorTormenta() {
        var d = unico(Escenario.enCalma().ajustar(TipoSensor.Viento, "95"), TipoFenomeno.Tormenta);

        assertThat(d.nivel()).isEqualTo(NivelAlerta.Rojo);
    }

    @Test
    void vientoYLluviaSimultaneos_escalanLaTormenta() {
        var d = unico(Escenario.enCalma().ajustar(TipoSensor.Viento, "45").ajustar(TipoSensor.Lluvia, "20"), TipoFenomeno.Tormenta);

        assertThat(d.nivel()).isEqualTo(NivelAlerta.Naranja);
    }

    @Test
    void sensorDesactivado_quedaFueraDeLaEvaluacion() {
        var e = new Escenario().con(TipoSensor.NivelRio, new BigDecimal("6"), EstadoSensor.Inactivo);

        assertThat(motor.evaluar(e.construir())).isEmpty();
    }

    @Test
    void sensorSinSenal_quedaFueraDeLaEvaluacion() {
        var e = new Escenario().con(TipoSensor.NivelRio, new BigDecimal("6"), EstadoSensor.SinSenal);

        assertThat(motor.evaluar(e.construir())).isEmpty();
    }

    @Test
    void sinSensorDeLaMagnitud_laReglaGuardaSilencio() {
        var e = new Escenario().con(TipoSensor.Temperatura, new BigDecimal("-5"));

        assertThat(motor.evaluar(e.construir())).extracting(Diagnostico::fenomeno).containsExactly(TipoFenomeno.Helada);
    }

    @Test
    void conVariosFenomenos_losDevuelveDelMasGraveAlMenosGrave() {
        var diagnosticos = motor.evaluar(Escenario.enCalma()
                .ajustar(TipoSensor.NivelRio, "3.2")   // inundación amarilla
                .ajustar(TipoSensor.Viento, "95")      // tormenta roja
                .construir());

        assertThat(diagnosticos).extracting(Diagnostico::nivel).isSortedAccordingTo((a, b) -> b.compareTo(a));
        assertThat(diagnosticos.getFirst().fenomeno()).isEqualTo(TipoFenomeno.Tormenta);
        assertThat(diagnosticos).allMatch(d -> d.reglaNombre().startsWith("Regla integrada"));
    }

    @Test
    void sinDiagnosticos_elNivelGlobalEsVerde() {
        assertThat(MotorRiesgo.nivelGlobal(List.of())).isEqualTo(NivelAlerta.Verde);
    }

    // --- Clasificación por umbrales (SensorTests de la Fase 2) ---

    @ParameterizedTest
    @CsvSource({"10,Verde", "38.9,Verde", "39,Amarillo", "61,Amarillo", "62,Naranja", "88,Naranja", "89,Rojo", "140,Rojo"})
    void clasificaPorExceso(String valor, NivelAlerta esperado) {
        assertThat(Umbrales.plantilla(TipoSensor.Viento).clasificar(new BigDecimal(valor))).isEqualTo(esperado);
    }

    @ParameterizedTest
    @CsvSource({"80,Verde", "35.1,Verde", "35,Amarillo", "26,Amarillo", "25,Naranja", "16,Naranja", "15,Rojo", "3,Rojo"})
    void clasificaPorDefecto(String valor, NivelAlerta esperado) {
        assertThat(Umbrales.plantilla(TipoSensor.Humedad).clasificar(new BigDecimal(valor))).isEqualTo(esperado);
    }

    @Test
    void conUmbralesEnAmbosSentidos_devuelveElNivelMasSevero() {
        var termometro = Umbrales.plantilla(TipoSensor.Temperatura);

        assertThat(termometro.clasificar(new BigDecimal("22"))).isEqualTo(NivelAlerta.Verde);
        assertThat(termometro.clasificar(new BigDecimal("45"))).isEqualTo(NivelAlerta.Rojo);
        assertThat(termometro.clasificar(new BigDecimal("-3"))).isEqualTo(NivelAlerta.Rojo);
        assertThat(termometro.clasificar(new BigDecimal("34"))).isEqualTo(NivelAlerta.Amarillo);
        assertThat(termometro.clasificar(new BigDecimal("4"))).isEqualTo(NivelAlerta.Amarillo);
    }

    @Test
    void sinUmbralesConfigurados_siempreClasificaEnVerde() {
        assertThat(Umbrales.NINGUNO.clasificar(new BigDecimal("99"))).isEqualTo(NivelAlerta.Verde);
    }

    @Test
    void validaQueLaEscaleraSeaCoherente() {
        assertThat(Umbrales.plantilla(TipoSensor.Temperatura).validar()).isNull();
        assertThat(new Umbrales(new BigDecimal("50"), new BigDecimal("40"), new BigDecimal("60"), null, null, null).validar())
                .contains("amarillo < naranja < rojo");
        assertThat(new Umbrales(null, null, null, new BigDecimal("5"), new BigDecimal("8"), new BigDecimal("1")).validar())
                .contains("amarillo > naranja > rojo");
    }
}
