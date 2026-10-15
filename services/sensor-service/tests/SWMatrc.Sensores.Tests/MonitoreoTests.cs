using Microsoft.EntityFrameworkCore;
using SWMatrc.Comun;
using SWMatrc.Sensores.Aplicacion;
using SWMatrc.Sensores.Dominio;
using SWMatrc.Sensores.Integracion;

namespace SWMatrc.Sensores.Tests;

public sealed class CicloDeMonitoreoTests
{
    private readonly Escenario _e = new();

    [Fact]
    public async Task Guarda_las_lecturas_con_el_nivel_que_devuelve_alert_y_las_difunde()
    {
        var sensor = await _e.AgregarSensorAsync();
        _e.Simulador.Valores[sensor.Id] = 39.5m;
        _e.Alertas.Niveles[sensor.Id] = NivelAlerta.Naranja;

        await _e.Monitoreo().EjecutarCicloAsync();

        var lectura = await _e.Db.Lecturas.SingleAsync();
        Assert.Equal((39.5m, NivelAlerta.Naranja), (lectura.Valor, lectura.Nivel));
        Assert.Equal(NivelAlerta.Naranja, (await _e.Db.Sensores.SingleAsync()).NivelActual);

        var difundida = Assert.Single(_e.Notificador.Lecturas);
        Assert.Equal((sensor.Id, 1, NivelAlerta.Naranja), (difundida.SensorId, difundida.ComunidadId, difundida.Nivel));
    }

    [Fact]
    public async Task Alert_recibe_la_fotografia_de_la_comunidad_con_el_valor_nuevo()
    {
        var sensor = await _e.AgregarSensorAsync();
        _e.Simulador.Valores[sensor.Id] = 41m;

        await _e.Monitoreo().EjecutarCicloAsync();

        var comunidad = Assert.Single(Assert.Single(_e.Alertas.Solicitudes).Comunidades);
        Assert.Equal((1, "Aldea El Rosario", true), (comunidad.ComunidadId, comunidad.ComunidadNombre, comunidad.Activa));
        Assert.Equal(41m, Assert.Single(comunidad.Sensores).Valor);
    }

    [Fact]
    public async Task Si_alert_no_responde_las_lecturas_se_guardan_igual_sin_nivel()
    {
        await _e.AgregarSensorAsync();
        _e.Alertas.Disponible = false;

        await _e.Monitoreo().EjecutarCicloAsync();

        Assert.Null((await _e.Db.Lecturas.SingleAsync()).Nivel);
        Assert.Single(_e.Notificador.Lecturas);
    }

    [Fact]
    public async Task Los_cambios_de_alertas_se_difunden_por_comunidad()
    {
        await _e.AgregarSensorAsync();
        _e.Alertas.CambiosPorDevolver.Add(new CambioAlerta("AlertaGenerada", 1, Json2.Alerta("Calor extremo")));

        await _e.Monitoreo().EjecutarCicloAsync();

        Assert.Equal(("AlertaGenerada", 1), Assert.Single(_e.Notificador.Alertas));
    }

    [Fact]
    public async Task Una_comunidad_inactiva_no_se_simula_y_alert_la_recibe_vacia_para_cerrar_lo_abierto()
    {
        await _e.AgregarSensorAsync(comunidadId: 2, codigo: "HUM-02");
        _e.Comunidades.Catalogo![2] = _e.Comunidades.Catalogo[2] with { Activa = false };

        await _e.Monitoreo().EjecutarCicloAsync();

        Assert.Empty(await _e.Db.Lecturas.ToListAsync());
        var comunidad = Assert.Single(Assert.Single(_e.Alertas.Solicitudes).Comunidades);
        Assert.False(comunidad.Activa);
        Assert.Empty(comunidad.Sensores);
    }

    [Fact]
    public async Task Sin_conocer_las_comunidades_el_ciclo_no_genera_nada()
    {
        await _e.AgregarSensorAsync();
        _e.Comunidades.Catalogo = null;

        await _e.Monitoreo().EjecutarCicloAsync();

        Assert.Empty(await _e.Db.Lecturas.ToListAsync());
        Assert.Empty(_e.Alertas.Solicitudes);
    }

    [Fact]
    public async Task Un_sensor_mudo_pasa_a_sin_senal_tras_la_tolerancia_y_se_recupera_al_volver()
    {
        var sensor = await _e.AgregarSensorAsync(ultimaLectura: DateTime.UtcNow.AddSeconds(-30));
        _e.Simulador.Valores[sensor.Id] = null;

        await _e.Monitoreo().EjecutarCicloAsync();

        Assert.Equal(EstadoSensor.SinSenal, (await _e.Db.Sensores.SingleAsync()).Estado);
        Assert.Equal(EstadoSensor.SinSenal, Assert.Single(_e.Notificador.Sensores).Estado);

        _e.Simulador.Valores[sensor.Id] = 22m;
        await _e.Monitoreo().EjecutarCicloAsync();

        Assert.Equal(EstadoSensor.Activo, (await _e.Db.Sensores.SingleAsync()).Estado);
    }

    [Fact]
    public async Task Un_silencio_breve_no_marca_el_sensor_sin_senal()
    {
        var sensor = await _e.AgregarSensorAsync(ultimaLectura: DateTime.UtcNow.AddSeconds(-4));
        _e.Simulador.Valores[sensor.Id] = null;

        await _e.Monitoreo().EjecutarCicloAsync();

        Assert.Equal(EstadoSensor.Activo, (await _e.Db.Sensores.SingleAsync()).Estado);
    }

    [Fact]
    public async Task Los_sensores_inactivos_no_reportan_pero_si_viajan_a_alert()
    {
        await _e.AgregarSensorAsync(estado: EstadoSensor.Inactivo);

        await _e.Monitoreo().EjecutarCicloAsync();

        Assert.Empty(await _e.Db.Lecturas.ToListAsync());
        var enviado = Assert.Single(Assert.Single(Assert.Single(_e.Alertas.Solicitudes).Comunidades).Sensores);
        Assert.Equal(EstadoSensor.Inactivo, enviado.Estado);
    }
}

public sealed class SensoresTests
{
    private readonly Escenario _e = new();

    private static CrearSensorRequest Solicitud(int comunidadId = 1, string codigo = "llu-04") => new()
    {
        ComunidadId = comunidadId,
        Codigo = codigo,
        Nombre = "Pluviómetro de la escuela",
        Tipo = TipoSensor.Lluvia
    };

    [Fact]
    public async Task Crear_completa_con_la_plantilla_y_toma_la_ubicacion_de_la_comunidad()
    {
        var sensor = await _e.Sensores().CrearAsync(Solicitud());

        Assert.Equal(("LLU-04", "mm", 0m, 120m), (sensor.Codigo, sensor.UnidadMedida, sensor.ValorMinimo, sensor.ValorMaximo));
        Assert.Equal((14.3m, -90.78m), (sensor.Latitud, sensor.Longitud));
        Assert.Equal("Aldea El Rosario", sensor.ComunidadNombre);
        Assert.Contains("SensorCreado", _e.Auditoria.Acciones);
    }

    [Fact]
    public async Task No_se_crea_en_una_comunidad_que_no_existe()
    {
        await Assert.ThrowsAsync<ExcepcionNoEncontrado>(() => _e.Sensores().CrearAsync(Solicitud(comunidadId: 99)));
    }

    [Fact]
    public async Task Si_community_no_responde_no_se_puede_verificar_y_se_avisa()
    {
        _e.Comunidades.Catalogo = null;

        await Assert.ThrowsAsync<ExcepcionServicioNoDisponible>(() => _e.Sensores().CrearAsync(Solicitud()));
    }

    [Fact]
    public async Task El_codigo_es_unico()
    {
        await _e.Sensores().CrearAsync(Solicitud());

        await Assert.ThrowsAsync<ExcepcionConflicto>(() => _e.Sensores().CrearAsync(Solicitud(codigo: "LLU-04")));
    }

    [Fact]
    public async Task Forzar_un_valor_lo_evalua_lo_guarda_con_su_nivel_y_lo_difunde()
    {
        var sensor = await _e.AgregarSensorAsync();
        _e.Alertas.Niveles[sensor.Id] = NivelAlerta.Rojo;

        var lectura = await _e.Sensores().EstablecerValorAsync(sensor.Id, 43m);

        Assert.Equal((43m, NivelAlerta.Rojo), (lectura.Valor, lectura.Nivel));
        Assert.Equal(43m, Assert.Single(_e.Notificador.Lecturas).Valor);
    }

    [Fact]
    public async Task No_admite_valores_fuera_del_rango_fisico()
    {
        var sensor = await _e.AgregarSensorAsync();

        var error = await Assert.ThrowsAsync<ExcepcionValidacion>(() => _e.Sensores().EstablecerValorAsync(sensor.Id, 80m));
        Assert.Contains("entre -10 y 50", error.Message);
    }

    [Fact]
    public async Task Un_sensor_inactivo_no_admite_lecturas()
    {
        var sensor = await _e.AgregarSensorAsync(estado: EstadoSensor.Inactivo);

        await Assert.ThrowsAsync<ExcepcionValidacion>(() => _e.Sensores().EstablecerValorAsync(sensor.Id, 20m));
    }

    [Fact]
    public async Task Sin_senal_no_se_asigna_a_mano()
    {
        var sensor = await _e.AgregarSensorAsync();

        await Assert.ThrowsAsync<ExcepcionValidacion>(() => _e.Sensores().CambiarEstadoAsync(sensor.Id, EstadoSensor.SinSenal));
    }

    [Fact]
    public async Task Desactivar_reevalua_la_comunidad()
    {
        var sensor = await _e.AgregarSensorAsync();

        var dto = await _e.Sensores().CambiarEstadoAsync(sensor.Id, EstadoSensor.Inactivo);

        Assert.Equal(EstadoSensor.Inactivo, dto.Estado);
        Assert.Equal(EstadoSensor.Inactivo, Assert.Single(Assert.Single(Assert.Single(_e.Alertas.Solicitudes).Comunidades).Sensores).Estado);
    }

    [Fact]
    public async Task Cuenta_los_sensores_por_comunidad()
    {
        await _e.AgregarSensorAsync(comunidadId: 1, codigo: "A");
        await _e.AgregarSensorAsync(comunidadId: 1, codigo: "B", estado: EstadoSensor.Inactivo);
        await _e.AgregarSensorAsync(comunidadId: 2, codigo: "C");

        var conteo = (await _e.Sensores().ContarPorComunidadAsync()).OrderBy(c => c.ComunidadId).ToList();

        Assert.Equal([new ConteoSensoresDto(1, 2, 1), new ConteoSensoresDto(2, 1, 1)], conteo);
    }
}

public sealed class LecturasTests
{
    private readonly Escenario _e = new();

    [Fact]
    public async Task Pagina_de_la_mas_reciente_a_la_mas_antigua_con_nombre_de_comunidad()
    {
        var sensor = await _e.AgregarSensorAsync();
        for (var i = 0; i < 5; i++)
            _e.Db.Lecturas.Add(new Lectura { SensorId = sensor.Id, Valor = i, UnidadMedida = "°C", FechaHora = DateTime.UtcNow.AddMinutes(i) });
        await _e.Db.SaveChangesAsync();

        var pagina = await _e.Lecturas().ListarAsync(new FiltroLecturas { Pagina = 1, Tamano = 2 });

        Assert.Equal((5, 3), (pagina.TotalElementos, pagina.TotalPaginas));
        Assert.Equal([4m, 3m], pagina.Elementos.Select(l => l.Valor));
        Assert.Equal("Aldea El Rosario", pagina.Elementos[0].ComunidadNombre);
    }

    [Fact]
    public async Task El_tamano_publico_se_acota_y_el_interno_admite_lotes_grandes()
    {
        await _e.AgregarSensorAsync();

        var publica = await _e.Lecturas().ListarAsync(new FiltroLecturas { Tamano = 100_000 });
        var interna = await _e.Lecturas().ListarAsync(new FiltroLecturas { Tamano = 100_000 }, ServicioLecturas.TamanoMaximoInterno);

        Assert.Equal((500, 5000), (publica.TamanoPagina, interna.TamanoPagina));
    }

    [Fact]
    public async Task Rango_de_fechas_invertido()
    {
        await Assert.ThrowsAsync<ExcepcionValidacion>(() => _e.Lecturas().ListarAsync(
            new FiltroLecturas { Desde = DateTime.UtcNow, Hasta = DateTime.UtcNow.AddDays(-1) }));
    }

    [Fact]
    public async Task Estadisticas_por_tipo()
    {
        var temperatura = await _e.AgregarSensorAsync(codigo: "T");
        var lluvia = await _e.AgregarSensorAsync(tipo: TipoSensor.Lluvia, codigo: "L");
        foreach (var (sensor, valor) in new[] { (temperatura, 20m), (temperatura, 30m), (lluvia, 5m), (lluvia, 7m) })
            _e.Db.Lecturas.Add(new Lectura { SensorId = sensor.Id, Valor = valor, UnidadMedida = sensor.UnidadMedida });
        await _e.Db.SaveChangesAsync();

        var estadisticas = await _e.Lecturas().EstadisticasAsync(null, null, null);

        var t = estadisticas.Single(e => e.Tipo == TipoSensor.Temperatura);
        Assert.Equal((2, 25m, 20m, 30m), (t.Lecturas, t.Promedio, t.Minimo, t.Maximo));
        Assert.Equal(12m, estadisticas.Single(e => e.Tipo == TipoSensor.Lluvia).Suma);
    }
}
