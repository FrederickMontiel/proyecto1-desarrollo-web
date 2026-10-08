using Microsoft.EntityFrameworkCore;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Comun;
using SWMatrc.Application.Dtos;
using SWMatrc.Domain.Entities;
using SWMatrc.Domain.Enums;
using SWMatrc.Domain.Riesgo;

namespace SWMatrc.Application.Servicios;

/// <inheritdoc cref="IServicioSensores"/>
public sealed class ServicioSensores(
    ISwmatrcDbContext db,
    IServicioBitacora bitacora,
    INotificadorTiempoReal notificador,
    IServicioMonitoreo monitoreo) : IServicioSensores
{
    public async Task<IReadOnlyList<SensorDto>> ListarAsync(FiltroSensores filtro, CancellationToken ct = default)
    {
        var consulta = db.Sensores.AsNoTracking().Include(s => s.Comunidad).AsQueryable();

        if (filtro.ComunidadId is { } id)
            consulta = consulta.Where(s => s.ComunidadId == id);

        if (filtro.Tipo is { } tipo)
            consulta = consulta.Where(s => s.Tipo == tipo);

        if (filtro.Estado is { } estado)
            consulta = consulta.Where(s => s.Estado == estado);

        if (!string.IsNullOrWhiteSpace(filtro.Codigo))
        {
            var codigo = filtro.Codigo.Trim().ToUpperInvariant();
            consulta = consulta.Where(s => s.Codigo.Contains(codigo));
        }

        var sensores = await consulta.OrderBy(s => s.Tipo).ThenBy(s => s.Codigo).ToListAsync(ct);
        return sensores.Select(SensorDto.Desde).ToList();
    }

    public async Task<SensorDto> ObtenerAsync(int id, CancellationToken ct = default) =>
        SensorDto.Desde(await CargarAsync(id, ct));

    public async Task<SensorDto> CrearAsync(CrearSensorRequest request, CancellationToken ct = default)
    {
        var comunidad = await db.Comunidades.FirstOrDefaultAsync(c => c.Id == request.ComunidadId, ct)
            ?? throw new ExcepcionNoEncontrado("la comunidad", request.ComunidadId);

        var codigo = request.Codigo.Trim().ToUpperInvariant();
        if (await db.Sensores.AnyAsync(s => s.Codigo == codigo, ct))
            throw new ExcepcionValidacion($"Ya existe un sensor con el código {codigo}.");

        // La plantilla del tipo aporta los valores que el usuario no especificó, de modo
        // que dar de alta un sensor puede reducirse a elegir su tipo y su ubicación.
        var plantilla = PlantillaSensor.Para(request.Tipo);

        var sensor = new Sensor
        {
            ComunidadId = comunidad.Id,
            Codigo = codigo,
            Nombre = request.Nombre.Trim(),
            Tipo = request.Tipo,
            UnidadMedida = string.IsNullOrWhiteSpace(request.UnidadMedida) ? plantilla.Unidad : request.UnidadMedida.Trim(),
            Estado = request.Estado == EstadoSensor.Inactivo ? EstadoSensor.Inactivo : EstadoSensor.Activo,
            Ubicacion = Limpiar(request.Ubicacion),
            Descripcion = Limpiar(request.Descripcion),
            FechaInstalacion = request.FechaInstalacion ?? DateTime.UtcNow.Date,
            Latitud = request.Latitud == 0 ? comunidad.Latitud : request.Latitud,
            Longitud = request.Longitud == 0 ? comunidad.Longitud : request.Longitud,
            ValorMinimo = request.ValorMinimo ?? plantilla.ValorMinimo,
            ValorMaximo = request.ValorMaximo ?? plantilla.ValorMaximo,
            VariacionMaxima = request.VariacionMaxima ?? plantilla.VariacionMaxima,
            ValorActual = request.ValorInicial ?? plantilla.ValorReposo,
            UmbralAmarilloAlto = request.UmbralAmarilloAlto ?? plantilla.AmarilloAlto,
            UmbralNaranjaAlto = request.UmbralNaranjaAlto ?? plantilla.NaranjaAlto,
            UmbralRojoAlto = request.UmbralRojoAlto ?? plantilla.RojoAlto,
            UmbralAmarilloBajo = request.UmbralAmarilloBajo ?? plantilla.AmarilloBajo,
            UmbralNaranjaBajo = request.UmbralNaranjaBajo ?? plantilla.NaranjaBajo,
            UmbralRojoBajo = request.UmbralRojoBajo ?? plantilla.RojoBajo,
            UltimaLectura = DateTime.UtcNow
        };

        ValidarRangos(sensor);

        db.Sensores.Add(sensor);
        await db.SaveChangesAsync(ct);

        sensor.Comunidad = comunidad;
        var dto = SensorDto.Desde(sensor);

        await bitacora.RegistrarAsync("SensorCreado", nameof(Sensor), sensor.Id,
            new { sensor.Codigo, Tipo = sensor.Tipo.ToString(), sensor.ComunidadId }, ct);
        await notificador.EstadoSensorCambiadoAsync(dto, ct);

        return dto;
    }

    public async Task<SensorDto> ActualizarAsync(int id, ActualizarSensorRequest request, CancellationToken ct = default)
    {
        var sensor = await CargarAsync(id, ct);
        var comunidadAnterior = sensor.ComunidadId;

        if (request.ComunidadId is { } nuevaComunidad && nuevaComunidad != sensor.ComunidadId)
        {
            sensor.Comunidad = await db.Comunidades.FirstOrDefaultAsync(c => c.Id == nuevaComunidad, ct)
                ?? throw new ExcepcionNoEncontrado("la comunidad", nuevaComunidad);
            sensor.ComunidadId = nuevaComunidad;
        }

        sensor.Nombre = request.Nombre?.Trim() ?? sensor.Nombre;
        sensor.UnidadMedida = request.UnidadMedida?.Trim() ?? sensor.UnidadMedida;
        sensor.Ubicacion = request.Ubicacion is null ? sensor.Ubicacion : Limpiar(request.Ubicacion);
        sensor.Descripcion = request.Descripcion is null ? sensor.Descripcion : Limpiar(request.Descripcion);
        sensor.FechaInstalacion = request.FechaInstalacion ?? sensor.FechaInstalacion;
        sensor.Latitud = request.Latitud ?? sensor.Latitud;
        sensor.Longitud = request.Longitud ?? sensor.Longitud;
        sensor.ValorMinimo = request.ValorMinimo ?? sensor.ValorMinimo;
        sensor.ValorMaximo = request.ValorMaximo ?? sensor.ValorMaximo;
        sensor.VariacionMaxima = request.VariacionMaxima ?? sensor.VariacionMaxima;
        sensor.UmbralAmarilloAlto = request.UmbralAmarilloAlto ?? sensor.UmbralAmarilloAlto;
        sensor.UmbralNaranjaAlto = request.UmbralNaranjaAlto ?? sensor.UmbralNaranjaAlto;
        sensor.UmbralRojoAlto = request.UmbralRojoAlto ?? sensor.UmbralRojoAlto;
        sensor.UmbralAmarilloBajo = request.UmbralAmarilloBajo ?? sensor.UmbralAmarilloBajo;
        sensor.UmbralNaranjaBajo = request.UmbralNaranjaBajo ?? sensor.UmbralNaranjaBajo;
        sensor.UmbralRojoBajo = request.UmbralRojoBajo ?? sensor.UmbralRojoBajo;
        sensor.FechaModificacion = DateTime.UtcNow;

        ValidarRangos(sensor);
        await db.SaveChangesAsync(ct);

        var dto = SensorDto.Desde(sensor);
        await bitacora.RegistrarAsync("SensorActualizado", nameof(Sensor), sensor.Id, new { sensor.Codigo }, ct);
        await notificador.EstadoSensorCambiadoAsync(dto, ct);

        // Recalibrar umbrales puede abrir o cerrar alertas: se reevalúa de inmediato.
        await monitoreo.EvaluarComunidadAsync(sensor.ComunidadId, ct);
        if (comunidadAnterior != sensor.ComunidadId)
            await monitoreo.EvaluarComunidadAsync(comunidadAnterior, ct);

        return dto;
    }

    public async Task<SensorDto> CambiarEstadoAsync(int id, EstadoSensor estado, CancellationToken ct = default)
    {
        var sensor = await CargarAsync(id, ct);

        sensor.Estado = estado;
        sensor.FechaModificacion = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var dto = SensorDto.Desde(sensor);
        await bitacora.RegistrarAsync("SensorCambioEstado", nameof(Sensor), sensor.Id,
            new { sensor.Codigo, Estado = estado.ToString() }, ct);
        await notificador.EstadoSensorCambiadoAsync(dto, ct);

        // Un sensor apagado deja de sostener sus alertas; uno encendido puede levantarlas.
        await monitoreo.EvaluarComunidadAsync(sensor.ComunidadId, ct);

        return dto;
    }

    public async Task<SensorDto> EstablecerValorAsync(int id, decimal valor, CancellationToken ct = default)
    {
        var sensor = await CargarAsync(id, ct);

        // Un sensor desactivado no produce lecturas, ni simuladas ni manuales (RF-ADM-18).
        if (sensor.Estado == EstadoSensor.Inactivo)
            throw new ExcepcionValidacion($"El sensor {sensor.Codigo} está desactivado y no admite lecturas.");

        if (sensor.Comunidad is { Activa: false })
            throw new ExcepcionValidacion($"La comunidad {sensor.Comunidad.Nombre} está desactivada.");

        if (valor < sensor.ValorMinimo || valor > sensor.ValorMaximo)
            throw new ExcepcionValidacion(
                $"El valor debe estar entre {sensor.ValorMinimo} y {sensor.ValorMaximo} {sensor.UnidadMedida}.");

        var ahora = DateTime.UtcNow;
        sensor.ValorActual = valor;
        sensor.UltimaLectura = ahora;

        // La inyección manual queda en el histórico igual que una lectura simulada:
        // los gráficos y el motor de riesgo no distinguen su origen.
        var lectura = new Lectura
        {
            SensorId = sensor.Id,
            Valor = valor,
            UnidadMedida = sensor.UnidadMedida,
            EstadoSensor = sensor.Estado,
            FechaHora = ahora
        };
        db.Lecturas.Add(lectura);
        await db.SaveChangesAsync(ct);

        var dto = SensorDto.Desde(sensor);
        await bitacora.RegistrarAsync("SensorValorEstablecido", nameof(Sensor), sensor.Id,
            new { sensor.Codigo, Valor = valor }, ct);
        await notificador.LecturaRecibidaAsync(LecturaDto.Desde(lectura, sensor), ct);
        await monitoreo.EvaluarComunidadAsync(sensor.ComunidadId, ct);

        return dto;
    }

    public async Task EliminarAsync(int id, CancellationToken ct = default)
    {
        var sensor = await CargarAsync(id, ct);
        var codigo = sensor.Codigo;
        var comunidadId = sensor.ComunidadId;

        // Las alertas emitidas por este sensor se conservan como evidencia, así que su
        // referencia se desata a mano antes de la baja: la clave foránea está declarada
        // sin acción en cascada para no perder historial por un cambio de inventario.
        var alertasDelSensor = await db.Alertas.Where(a => a.SensorId == id).ToListAsync(ct);
        foreach (var alerta in alertasDelSensor)
            alerta.SensorId = null;

        // El historial conserva el nombre del sensor en OrigenSensor; solo se suelta la referencia.
        var eventosDelSensor = await db.Eventos.Where(e => e.SensorId == id).ToListAsync(ct);
        foreach (var evento in eventosDelSensor)
            evento.SensorId = null;

        await db.SaveChangesAsync(ct);

        db.Sensores.Remove(sensor);
        await db.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("SensorEliminado", nameof(Sensor), id, new { Codigo = codigo }, ct);
        await monitoreo.EvaluarComunidadAsync(comunidadId, ct);
    }

    private static string? Limpiar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private async Task<Sensor> CargarAsync(int id, CancellationToken ct) =>
        await db.Sensores.Include(s => s.Comunidad).FirstOrDefaultAsync(s => s.Id == id, ct)
        ?? throw new ExcepcionNoEncontrado("el sensor", id);

    /// <summary>Comprueba la coherencia del rango y de la escalera de umbrales.</summary>
    private static void ValidarRangos(Sensor sensor)
    {
        if (sensor.ValorMinimo >= sensor.ValorMaximo)
            throw new ExcepcionValidacion("El valor mínimo debe ser menor que el valor máximo.");

        if (sensor.VariacionMaxima <= 0)
            throw new ExcepcionValidacion("La variación máxima debe ser mayor que cero.");

        // Por exceso los umbrales crecen hacia el rojo; por defecto decrecen.
        decimal?[] altos = [sensor.UmbralAmarilloAlto, sensor.UmbralNaranjaAlto, sensor.UmbralRojoAlto];
        if (altos.All(u => u is not null) && !(altos[0] < altos[1] && altos[1] < altos[2]))
            throw new ExcepcionValidacion("Los umbrales por exceso deben cumplir amarillo < naranja < rojo.");

        decimal?[] bajos = [sensor.UmbralAmarilloBajo, sensor.UmbralNaranjaBajo, sensor.UmbralRojoBajo];
        if (bajos.All(u => u is not null) && !(bajos[0] > bajos[1] && bajos[1] > bajos[2]))
            throw new ExcepcionValidacion("Los umbrales por defecto deben cumplir amarillo > naranja > rojo.");
    }
}
