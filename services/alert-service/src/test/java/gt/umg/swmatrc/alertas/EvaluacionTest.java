package gt.umg.swmatrc.alertas;

import static org.assertj.core.api.Assertions.assertThat;

import gt.umg.swmatrc.alertas.aplicacion.Evaluacion;
import gt.umg.swmatrc.alertas.dominio.Enums.EstadoAlerta;
import gt.umg.swmatrc.alertas.dominio.Enums.NivelAlerta;
import gt.umg.swmatrc.alertas.dominio.Enums.TipoFenomeno;
import gt.umg.swmatrc.alertas.dominio.Enums.TipoSensor;
import gt.umg.swmatrc.alertas.dominio.Reglas;
import gt.umg.swmatrc.alertas.dominio.Riesgo.Diagnostico;
import gt.umg.swmatrc.alertas.dominio.Riesgo.MotorRiesgo;
import gt.umg.swmatrc.alertas.persistencia.Entidades.Alerta;
import gt.umg.swmatrc.alertas.persistencia.Entidades.ReglaAlerta;
import java.math.BigDecimal;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.Test;

/** Diagnóstico combinado (integradas + configurables) y conciliación con las alertas abiertas. */
class EvaluacionTest {

    private final MotorRiesgo motor = new MotorRiesgo(Reglas.todas());
    private final Instant ahora = Instant.parse("2026-10-22T12:00:00Z");

    private static ReglaAlerta regla(long id, TipoSensor tipo, String minimo, NivelAlerta nivel, TipoFenomeno fenomeno) {
        var r = new ReglaAlerta();
        r.id = id;
        r.nombre = "Regla " + id;
        r.tipoSensor = tipo;
        r.valorMinimo = minimo == null ? null : new BigDecimal(minimo);
        r.nivel = nivel;
        r.fenomeno = fenomeno;
        r.mensaje = "{sensor} marca {valor} {unidad} en {comunidad}";
        r.activa = true;
        return r;
    }

    // --- Diagnóstico ---------------------------------------------------------------

    @Test
    void unaReglaConfigurableRedactaSuMensajeConLosMarcadores() {
        var e = Escenario.enCalma().con(TipoSensor.Humo, new BigDecimal("180.5"));

        var d = Evaluacion.diagnosticar(motor, e.construir(),
                List.of(regla(4, TipoSensor.Humo, "150", NivelAlerta.Naranja, TipoFenomeno.IncendioForestal)));

        assertThat(d).singleElement().satisfies(x -> {
            assertThat(x.nivel()).isEqualTo(NivelAlerta.Naranja);
            assertThat(x.reglaId()).isEqualTo(4L);
            assertThat(x.mensaje()).isEqualTo("HUM-6 marca 180.5 µg/m³ en Comunidad de prueba");
            assertThat(x.umbral()).isEqualByComparingTo("150");
        });
    }

    @Test
    void queda_un_diagnostico_por_fenomeno_y_gana_el_mas_grave() {
        // Integrada: incendio rojo por calor y sequedad. Configurable: incendio amarillo por humo.
        var e = Escenario.enCalma().ajustar(TipoSensor.Temperatura, "42").ajustar(TipoSensor.Humedad, "10")
                .ajustar(TipoSensor.Viento, "45").ajustar(TipoSensor.Lluvia, "0").con(TipoSensor.Humo, new BigDecimal("60"));

        var d = Evaluacion.diagnosticar(motor, e.construir(),
                List.of(regla(4, TipoSensor.Humo, "55", NivelAlerta.Amarillo, TipoFenomeno.IncendioForestal)));

        assertThat(d).filteredOn(x -> x.fenomeno() == TipoFenomeno.IncendioForestal).singleElement()
                .satisfies(x -> assertThat(x.nivel()).isEqualTo(NivelAlerta.Rojo));
    }

    @Test
    void ante_empate_de_nivel_gana_la_configurable_mas_cercana_al_valor() {
        var e = Escenario.enCalma().con(TipoSensor.Humo, new BigDecimal("450"));

        var d = Evaluacion.diagnosticar(motor, e.construir(), List.of(
                regla(6, TipoSensor.Humo, "250", NivelAlerta.Rojo, TipoFenomeno.IncendioForestal),
                regla(9, TipoSensor.Humo, "400", NivelAlerta.Rojo, TipoFenomeno.IncendioForestal)));

        assertThat(d).singleElement().satisfies(x -> assertThat(x.reglaId()).isEqualTo(9L));
    }

    @Test
    void una_regla_inactiva_no_dispara() {
        var r = regla(4, TipoSensor.Humo, "55", NivelAlerta.Amarillo, TipoFenomeno.IncendioForestal);
        r.activa = false;

        assertThat(Evaluacion.diagnosticar(motor, Escenario.enCalma().con(TipoSensor.Humo, new BigDecimal("300")).construir(), List.of(r)))
                .isEmpty();
    }

    // --- Conciliación --------------------------------------------------------------

    private Evaluacion.Resultado conciliar(Escenario e, List<Alerta> abiertas) {
        var ctx = e.construir();
        List<Diagnostico> d = Evaluacion.diagnosticar(motor, ctx, List.of());
        return Evaluacion.conciliar(ctx, d, abiertas, e.porId(), ahora);
    }

    private static Alerta abierta(TipoFenomeno fenomeno, NivelAlerta nivel) {
        var a = new Alerta();
        a.id = 77L;
        a.comunidadId = 1;
        a.fenomeno = fenomeno;
        a.nivel = nivel;
        a.mensaje = "anterior";
        a.reglaNombre = "Regla integrada: " + fenomeno;
        return a;
    }

    @Test
    void un_riesgo_nuevo_abre_una_alerta_con_copia_del_sensor_y_su_umbral() {
        var r = conciliar(Escenario.enCalma().ajustar(TipoSensor.NivelRio, "4.8"), new ArrayList<>());

        assertThat(r.nuevas()).singleElement().satisfies(a -> {
            assertThat(a.fenomeno).isEqualTo(TipoFenomeno.Inundacion);
            assertThat(a.nivel).isEqualTo(NivelAlerta.Rojo);
            assertThat(a.sensorCodigo).isEqualTo("NIV-5");
            assertThat(a.unidadMedida).isEqualTo("m");
            assertThat(a.umbral).isEqualByComparingTo("4.5");
            assertThat(a.comunidadNombre).isEqualTo("Comunidad de prueba");
            assertThat(a.estado).isEqualTo(EstadoAlerta.Activa);
        });
    }

    @Test
    void si_el_riesgo_persiste_no_se_duplica_la_alerta_y_se_refresca_el_texto() {
        var existente = abierta(TipoFenomeno.Inundacion, NivelAlerta.Rojo);
        existente.ciclosSinRiesgo = 3;

        var r = conciliar(Escenario.enCalma().ajustar(TipoSensor.NivelRio, "4.9"), new ArrayList<>(List.of(existente)));

        assertThat(r.hayCambios()).isFalse();
        assertThat(existente.mensaje).contains("4.90 m");
        assertThat(existente.ciclosSinRiesgo).isZero();
    }

    @Test
    void si_empeora_se_escala_y_se_retira_el_acuse() {
        var existente = abierta(TipoFenomeno.Inundacion, NivelAlerta.Amarillo);
        existente.atender(5, "Ana", ahora.minusSeconds(60));

        var r = conciliar(Escenario.enCalma().ajustar(TipoSensor.NivelRio, "4.8"), new ArrayList<>(List.of(existente)));

        assertThat(r.escaladas()).containsExactly(existente);
        assertThat(existente.nivel).isEqualTo(NivelAlerta.Rojo);
        assertThat(existente.reconocida).isFalse();
        assertThat(existente.estado).isEqualTo(EstadoAlerta.Activa);
    }

    @Test
    void al_normalizarse_se_cierra_solo_despues_de_varios_ciclos_en_calma() {
        var existente = abierta(TipoFenomeno.Inundacion, NivelAlerta.Rojo);
        var lista = new ArrayList<>(List.of(existente));

        for (int ciclo = 1; ciclo < Evaluacion.CICLOS_PARA_CIERRE; ciclo++) {
            assertThat(conciliar(Escenario.enCalma(), lista).cerradas()).isEmpty();
        }
        var r = conciliar(Escenario.enCalma(), lista);

        assertThat(r.cerradas()).containsExactly(existente);
        assertThat(existente.fechaCierre).isEqualTo(ahora);
        assertThat(existente.cerradaPor).isNull();
        assertThat(existente.estado).isEqualTo(EstadoAlerta.Cerrada);
    }
}
