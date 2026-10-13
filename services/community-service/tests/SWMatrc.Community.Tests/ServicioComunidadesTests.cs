using Microsoft.EntityFrameworkCore;
using SWMatrc.Comun;
using SWMatrc.Community.Aplicacion;
using SWMatrc.Community.Datos;
using SWMatrc.Community.Integracion;

namespace SWMatrc.Community.Tests;

public sealed class ServicioComunidadesTests
{
    private readonly ComunidadesDb _db = new(new DbContextOptionsBuilder<ComunidadesDb>()
        .UseInMemoryDatabase($"comunidades-{Guid.NewGuid()}")
        .Options);

    private readonly AuditoriaEnMemoria _auditoria = new();
    private readonly FuenteConteoFija _sensores = new();

    private ServicioComunidades Servicio() => new(_db, _sensores, _auditoria);

    private static GuardarComunidadRequest Solicitud(string nombre = "San Marcos", string departamento = "San Marcos") => new()
    {
        Nombre = $"  {nombre}  ",
        Municipio = "Tacaná",
        Departamento = departamento,
        Pais = "Guatemala",
        Latitud = 15.24m,
        Longitud = -92.07m,
        Poblacion = 1200
    };

    [Fact]
    public async Task Crear_guarda_recortado_y_lo_registra_en_auditoria()
    {
        var creada = await Servicio().CrearAsync(Solicitud());

        Assert.Equal("San Marcos", creada.Nombre);
        Assert.True(creada.Activa);
        var evento = Assert.Single(_auditoria.Eventos);
        Assert.Equal(("ComunidadCreada", "Comunidad", creada.Id.ToString()), (evento.Accion, evento.Entidad, evento.EntidadId));
    }

    [Fact]
    public async Task No_admite_dos_comunidades_con_el_mismo_nombre_sin_importar_mayusculas()
    {
        await Servicio().CrearAsync(Solicitud("San Marcos"));

        var error = await Assert.ThrowsAsync<ExcepcionConflicto>(() => Servicio().CrearAsync(Solicitud("SAN MARCOS")));
        Assert.Contains("Ya existe", error.Message);
    }

    [Fact]
    public async Task Actualizar_puede_conservar_su_propio_nombre()
    {
        var creada = await Servicio().CrearAsync(Solicitud());

        var actualizada = await Servicio().ActualizarAsync(creada.Id, Solicitud() with { Poblacion = 5000 });

        Assert.Equal(5000, actualizada.Poblacion);
        Assert.NotNull(actualizada.FechaModificacion);
    }

    [Fact]
    public async Task Los_conteos_vienen_del_sensor_service()
    {
        var creada = await Servicio().CrearAsync(Solicitud());
        _sensores.Conteos = new Dictionary<int, ConteoSensores> { [creada.Id] = new(creada.Id, 4, 3) };

        var dto = await Servicio().ObtenerAsync(creada.Id);

        Assert.Equal((4, 3, true), (dto.TotalSensores, dto.SensoresActivos, dto.ConteoSensoresDisponible));
    }

    [Fact]
    public async Task Si_sensor_service_no_responde_el_listado_sale_igual_marcado_sin_conteo()
    {
        await Servicio().CrearAsync(Solicitud());
        _sensores.Conteos = null;

        var dto = Assert.Single(await Servicio().ListarAsync(new FiltroComunidades()));

        Assert.False(dto.ConteoSensoresDisponible);
        Assert.Equal(0, dto.TotalSensores);
    }

    [Fact]
    public async Task Filtra_por_texto_sin_distinguir_mayusculas_y_por_departamento()
    {
        await Servicio().CrearAsync(Solicitud("Aldea El Rosario", "Escuintla"));
        await Servicio().CrearAsync(Solicitud("Caserío Las Flores", "Petén"));

        var porTexto = await Servicio().ListarAsync(new FiltroComunidades { Busqueda = "rosario" });
        var porDepartamento = await Servicio().ListarAsync(new FiltroComunidades { Departamento = "petén" });

        Assert.Equal("Aldea El Rosario", Assert.Single(porTexto).Nombre);
        Assert.Equal("Caserío Las Flores", Assert.Single(porDepartamento).Nombre);
    }

    [Fact]
    public async Task Cambiar_estado_solo_audita_si_el_estado_cambia()
    {
        var creada = await Servicio().CrearAsync(Solicitud());
        _auditoria.Eventos.Clear();

        await Servicio().CambiarEstadoAsync(creada.Id, activa: true);
        Assert.Empty(_auditoria.Eventos);

        var desactivada = await Servicio().CambiarEstadoAsync(creada.Id, activa: false);
        Assert.False(desactivada.Activa);
        Assert.Equal("ComunidadDesactivada", Assert.Single(_auditoria.Eventos).Accion);
    }

    [Fact]
    public async Task El_listado_interno_filtra_por_estado_y_no_consulta_sensores()
    {
        var activa = await Servicio().CrearAsync(Solicitud("Activa"));
        var inactiva = await Servicio().CrearAsync(Solicitud("Inactiva"));
        await Servicio().CambiarEstadoAsync(inactiva.Id, activa: false);
        _sensores.Consultas = 0;

        var internas = await Servicio().ListarInternoAsync(activa: true);

        Assert.Equal(activa.Id, Assert.Single(internas).Id);
        Assert.Equal(0, _sensores.Consultas);
    }

    [Fact]
    public async Task Una_comunidad_inexistente_responde_no_encontrado()
    {
        await Assert.ThrowsAsync<ExcepcionNoEncontrado>(() => Servicio().ObtenerAsync(999));
    }

    [Fact]
    public async Task El_geojson_ordena_las_coordenadas_como_longitud_latitud()
    {
        await Servicio().CrearAsync(Solicitud());

        var geo = System.Text.Json.JsonSerializer.SerializeToElement(
            await Servicio().GeoJsonAsync(new FiltroComunidades()), Json.Opciones);

        var coordenadas = geo.GetProperty("features")[0].GetProperty("geometry").GetProperty("coordinates");
        Assert.Equal(-92.07m, coordenadas[0].GetDecimal());
        Assert.Equal(15.24m, coordenadas[1].GetDecimal());
    }
}

internal sealed class AuditoriaEnMemoria : IClienteAuditoria
{
    public List<(string Accion, string Entidad, string? EntidadId)> Eventos { get; } = [];

    public void Registrar(string accion, string entidad, object? entidadId = null, object? detalle = null) =>
        Eventos.Add((accion, entidad, entidadId?.ToString()));
}

internal sealed class FuenteConteoFija : IFuenteConteoSensores
{
    public IReadOnlyDictionary<int, ConteoSensores>? Conteos { get; set; } = new Dictionary<int, ConteoSensores>();

    public int Consultas { get; set; }

    public Task<IReadOnlyDictionary<int, ConteoSensores>?> ObtenerAsync(CancellationToken ct)
    {
        Consultas++;
        return Task.FromResult(Conteos);
    }
}
