using Microsoft.EntityFrameworkCore;
using SWMatrc.Comun;
using SWMatrc.Sensores.Dominio;
using SWMatrc.Sensores.Iot;

namespace SWMatrc.Sensores.Tests;

public sealed class SensoresFisicosTests
{
    private readonly Escenario _e = new();

    private DispositivoIot Raspberry => _e.OpcionesIot.Dispositivos[0];

    private static LoteIotRequest Lote(params (string Codigo, decimal Valor)[] lecturas) =>
        new(lecturas.Select(l => new LecturaIotRequest(l.Codigo, l.Valor)).ToList());

    [Fact]
    public void Solo_la_llave_exacta_identifica_al_dispositivo()
    {
        var registro = _e.RegistroIot();
        Assert.Equal("raspberry-01", registro.Autenticar("llave-de-prueba-muy-larga")?.Id);
        Assert.Null(registro.Autenticar("llave-de-prueba-muy-larg"));
        Assert.Null(registro.Autenticar(null));
        Assert.True(registro.EsFisico("rpi-temp-01"));
        Assert.False(registro.EsFisico("TEMP-01"));
    }

    [Fact]
    public void Una_llave_corta_no_se_acepta_como_configuracion()
    {
        _e.OpcionesIot.Dispositivos[0].Llave = "corta";
        Assert.Equal(0, _e.RegistroIot().CantidadDispositivos);
    }

    [Fact]
    public async Task La_lectura_real_llega_al_ciclo_se_evalua_se_guarda_y_se_difunde()
    {
        var sensor = await _e.AgregarSensorAsync(codigo: "RPI-TEMP-01", ultimaLectura: DateTime.UtcNow.AddSeconds(-5));
        _e.Alertas.Niveles[sensor.Id] = NivelAlerta.Amarillo;

        var resultado = await _e.Iot().RecibirAsync(Raspberry, Lote(("RPI-TEMP-01", 31.5m)));
        Assert.Equal(1, resultado.Aceptadas);

        await _e.Monitoreo().EjecutarCicloAsync();

        var lectura = await _e.Db.Lecturas.SingleAsync();
        Assert.Equal((31.5m, NivelAlerta.Amarillo), (lectura.Valor, lectura.Nivel));
        Assert.Equal(31.5m, Assert.Single(Assert.Single(_e.Alertas.Solicitudes).Comunidades).Sensores.Single().Valor);
        Assert.Single(_e.Notificador.Lecturas);
    }

    [Fact]
    public async Task El_simulador_no_inventa_valores_para_un_sensor_fisico()
    {
        var sensor = await _e.AgregarSensorAsync(codigo: "RPI-TEMP-01");
        _e.Simulador.Valores[sensor.Id] = 99m;

        await _e.Monitoreo().EjecutarCicloAsync();

        Assert.Empty(await _e.Db.Lecturas.ToListAsync());
    }

    [Fact]
    public async Task Un_mismo_valor_recibido_se_registra_una_sola_vez()
    {
        await _e.AgregarSensorAsync(codigo: "RPI-TEMP-01", ultimaLectura: DateTime.UtcNow.AddSeconds(-5));
        await _e.Iot().RecibirAsync(Raspberry, Lote(("RPI-TEMP-01", 25m)));

        await _e.Monitoreo().EjecutarCicloAsync();
        await _e.Monitoreo().EjecutarCicloAsync();

        Assert.Single(await _e.Db.Lecturas.ToListAsync());
    }

    [Fact]
    public async Task Un_sensor_fisico_que_deja_de_reportar_queda_sin_senal_con_su_propia_tolerancia()
    {
        // 15 s de silencio: un simulado (tolerancia 9 s) ya estaría sin señal; uno físico (30 s) no.
        var fisico = await _e.AgregarSensorAsync(codigo: "RPI-TEMP-01", ultimaLectura: DateTime.UtcNow.AddSeconds(-15));
        await _e.Monitoreo().EjecutarCicloAsync();
        Assert.Equal(EstadoSensor.Activo, (await _e.Db.Sensores.FindAsync(fisico.Id))!.Estado);

        fisico.UltimaLectura = DateTime.UtcNow.AddSeconds(-45);
        await _e.Db.SaveChangesAsync();
        await _e.Monitoreo().EjecutarCicloAsync();
        Assert.Equal(EstadoSensor.SinSenal, (await _e.Db.Sensores.FindAsync(fisico.Id))!.Estado);

        // Vuelve a reportar → recupera la señal.
        await _e.Iot().RecibirAsync(Raspberry, Lote(("RPI-TEMP-01", 22m)));
        await _e.Monitoreo().EjecutarCicloAsync();
        Assert.Equal(EstadoSensor.Activo, (await _e.Db.Sensores.FindAsync(fisico.Id))!.Estado);
    }

    [Fact]
    public async Task Rechaza_sensores_ajenos_inexistentes_inactivos_y_fuera_de_rango()
    {
        await _e.AgregarSensorAsync(codigo: "RPI-TEMP-01");
        await _e.AgregarSensorAsync(codigo: "RPI-HUM-01", tipo: TipoSensor.Humedad, estado: EstadoSensor.Inactivo);
        await _e.AgregarSensorAsync(codigo: "TEMP-02");

        var resultado = await _e.Iot().RecibirAsync(Raspberry, Lote(
            ("RPI-TEMP-01", 80m),     // fuera de -10…50 °C
            ("RPI-HUM-01", 50m),      // inactivo
            ("TEMP-02", 20m),         // no es de este dispositivo
            ("RPI-TEMP-01", 21m)));   // válida

        Assert.Equal(1, resultado.Aceptadas);
        Assert.Equal(["RPI-TEMP-01", "RPI-HUM-01", "TEMP-02"], resultado.Rechazadas.Select(r => r.Codigo));
        Assert.Null((await _e.Db.Sensores.SingleAsync(s => s.Codigo == "TEMP-02")).ValorRecibido);
    }

    [Fact]
    public async Task Un_lote_vacio_es_un_error_de_validacion()
    {
        await Assert.ThrowsAsync<ExcepcionValidacion>(() => _e.Iot().RecibirAsync(Raspberry, new LoteIotRequest([])));
    }
}
