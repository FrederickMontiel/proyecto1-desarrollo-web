using Microsoft.EntityFrameworkCore;
using SWMatrc.Comun;
using SWMatrc.Sensores.Datos;
using SWMatrc.Sensores.Dominio;
using SWMatrc.Sensores.Integracion;
using SWMatrc.Sensores.TiempoReal;

namespace SWMatrc.Sensores.Aplicacion;

/// <summary>Administración de la red de sensores.</summary>
public sealed class ServicioSensores(
    SensoresDb db,
    IFuenteComunidades comunidades,
    ServicioMonitoreo monitoreo,
    INotificador notificador,
    IClienteAuditoria auditoria)
{
    public async Task<IReadOnlyList<SensorDto>> ListarAsync(FiltroSensores filtro, CancellationToken ct = default)
    {
        var consulta = db.Sensores.AsNoTracking();

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
        var catalogo = await comunidades.ListarAsync(ct);
        return sensores.Select(s => SensorDto.Desde(s, catalogo?.GetValueOrDefault(s.ComunidadId)?.Nombre)).ToList();
    }

    public async Task<SensorDto> ObtenerAsync(int id, CancellationToken ct = default) =>
        await ADtoAsync(await CargarAsync(id, ct), ct);

    public async Task<SensorDto> CrearAsync(CrearSensorRequest request, CancellationToken ct = default)
    {
        var comunidad = await ComunidadExistenteAsync(request.ComunidadId, ct);

        var codigo = request.Codigo.Trim().ToUpperInvariant();
        if (await db.Sensores.AnyAsync(s => s.Codigo == codigo, ct))
            throw new ExcepcionConflicto($"Ya existe un sensor con el código {codigo}.");

        // La plantilla del tipo aporta lo que el usuario no especificó: dar de alta un sensor
        // puede reducirse a elegir su tipo y su ubicación. Los umbrales de alerta los propone
        // Alert Service con su propia plantilla por tipo.
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
            UltimaLectura = DateTime.UtcNow
        };
        Validar(sensor);

        db.Sensores.Add(sensor);
        await db.SaveChangesAsync(ct);

        auditoria.Registrar("SensorCreado", nameof(Sensor), sensor.Id,
            new { sensor.Codigo, Tipo = sensor.Tipo.ToString(), sensor.ComunidadId });

        var dto = SensorDto.Desde(sensor, comunidad.Nombre);
        await notificador.EstadoSensorAsync(dto, ct);
        return dto;
    }

    public async Task<SensorDto> ActualizarAsync(int id, ActualizarSensorRequest request, CancellationToken ct = default)
    {
        var sensor = await CargarAsync(id, ct);
        var comunidadAnterior = sensor.ComunidadId;

        if (request.ComunidadId is { } nueva && nueva != sensor.ComunidadId)
            sensor.ComunidadId = (await ComunidadExistenteAsync(nueva, ct)).Id;

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
        // Si el rango se estrecha, la última lectura se acota a él en lugar de rechazar la edición.
        if (sensor.ValorMinimo < sensor.ValorMaximo)
            sensor.ValorActual = Math.Clamp(sensor.ValorActual, sensor.ValorMinimo, sensor.ValorMaximo);
        Validar(sensor);

        await db.SaveChangesAsync(ct);
        auditoria.Registrar("SensorActualizado", nameof(Sensor), sensor.Id, new { sensor.Codigo });

        // Mover un sensor de comunidad cambia la fotografía de las dos.
        await monitoreo.ReevaluarComunidadAsync(sensor.ComunidadId, ct: ct);
        if (comunidadAnterior != sensor.ComunidadId)
            await monitoreo.ReevaluarComunidadAsync(comunidadAnterior, ct: ct);

        var dto = await ADtoAsync(sensor, ct);
        await notificador.EstadoSensorAsync(dto, ct);
        return dto;
    }

    /// <summary>Activa o desactiva un sensor sin borrarlo de la red. "Sin señal" no se fija a mano: lo detecta el ciclo.</summary>
    public async Task<SensorDto> CambiarEstadoAsync(int id, EstadoSensor estado, CancellationToken ct = default)
    {
        if (estado == EstadoSensor.SinSenal)
            throw new ExcepcionValidacion("El estado 'SinSenal' lo asigna el sistema; elija Activo o Inactivo.");

        var sensor = await CargarAsync(id, ct);
        sensor.Estado = estado;
        if (estado == EstadoSensor.Activo)
            // Se le da un margen para reportar antes de que el ciclo lo declare sin señal.
            sensor.UltimaLectura = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        auditoria.Registrar("SensorCambioEstado", nameof(Sensor), sensor.Id, new { sensor.Codigo, Estado = estado.ToString() });

        // Un sensor apagado deja de sostener sus alertas; uno encendido puede levantarlas.
        await monitoreo.ReevaluarComunidadAsync(sensor.ComunidadId, ct: ct);

        var dto = await ADtoAsync(sensor, ct);
        await notificador.EstadoSensorAsync(dto, ct);
        return dto;
    }

    /// <summary>Fuerza una lectura concreta. Sirve para ensayar escenarios de alerta.</summary>
    public async Task<LecturaHistoricaDto> EstablecerValorAsync(int id, decimal valor, CancellationToken ct = default)
    {
        var sensor = await CargarAsync(id, ct);

        if (sensor.Estado == EstadoSensor.Inactivo)
            throw new ExcepcionValidacion($"El sensor {sensor.Codigo} está desactivado y no admite lecturas.");

        var comunidad = (await comunidades.ListarAsync(ct))?.GetValueOrDefault(sensor.ComunidadId);
        if (comunidad is { Activa: false })
            throw new ExcepcionValidacion($"La comunidad {comunidad.Nombre} está desactivada.");

        if (valor < sensor.ValorMinimo || valor > sensor.ValorMaximo)
            throw new ExcepcionValidacion(
                $"El valor debe estar entre {sensor.ValorMinimo:0.###} y {sensor.ValorMaximo:0.###} {sensor.UnidadMedida}.");

        var ahora = DateTime.UtcNow;
        sensor.ValorActual = valor;
        sensor.UltimaLectura = ahora;
        if (sensor.Estado == EstadoSensor.SinSenal)
            sensor.Estado = EstadoSensor.Activo;

        // La lectura manual queda en el histórico igual que una simulada: los gráficos y la
        // evaluación de riesgo no distinguen su origen.
        var lectura = new Lectura
        {
            SensorId = sensor.Id,
            Valor = valor,
            UnidadMedida = sensor.UnidadMedida,
            EstadoSensor = sensor.Estado,
            FechaHora = ahora
        };
        db.Lecturas.Add(lectura);

        await monitoreo.ReevaluarComunidadAsync(sensor.ComunidadId, lectura, ct);
        auditoria.Registrar("SensorValorEstablecido", nameof(Sensor), sensor.Id, new { sensor.Codigo, Valor = valor });

        return ServicioLecturas.AHistorica(lectura, sensor, comunidad?.Nombre);
    }

    public async Task EliminarAsync(int id, CancellationToken ct = default)
    {
        var sensor = await CargarAsync(id, ct);

        // Sus lecturas se borran en cascada. Las alertas y eventos que generó se conservan
        // en Alert Service, que guarda una copia del código y nombre del sensor.
        db.Sensores.Remove(sensor);
        await db.SaveChangesAsync(ct);

        auditoria.Registrar("SensorEliminado", nameof(Sensor), id, new { sensor.Codigo });
        await monitoreo.ReevaluarComunidadAsync(sensor.ComunidadId, ct: ct);
    }

    public async Task<IReadOnlyList<ConteoSensoresDto>> ContarPorComunidadAsync(CancellationToken ct = default) =>
        await db.Sensores.AsNoTracking()
            .GroupBy(s => s.ComunidadId)
            .Select(g => new ConteoSensoresDto(g.Key, g.Count(), g.Count(s => s.Estado == EstadoSensor.Activo)))
            .ToListAsync(ct);

    private async Task<ComunidadInfo> ComunidadExistenteAsync(int comunidadId, CancellationToken ct)
    {
        var catalogo = await comunidades.ListarAsync(ct);
        // Si no está en la caché puede ser una comunidad recién creada: se pregunta de nuevo.
        if (catalogo is not null && !catalogo.ContainsKey(comunidadId))
            catalogo = await comunidades.ListarAsync(ct, forzar: true);

        if (catalogo is null)
            throw new ExcepcionServicioNoDisponible("No se pudo verificar la comunidad: Community Service no responde.");

        return catalogo.GetValueOrDefault(comunidadId) ?? throw new ExcepcionNoEncontrado("la comunidad", comunidadId);
    }

    private async Task<SensorDto> ADtoAsync(Sensor sensor, CancellationToken ct) =>
        SensorDto.Desde(sensor, (await comunidades.ListarAsync(ct))?.GetValueOrDefault(sensor.ComunidadId)?.Nombre);

    private async Task<Sensor> CargarAsync(int id, CancellationToken ct) =>
        await db.Sensores.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new ExcepcionNoEncontrado("el sensor", id);

    private static string? Limpiar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private static void Validar(Sensor sensor)
    {
        if (sensor.ValorMinimo >= sensor.ValorMaximo)
            throw new ExcepcionValidacion("El valor mínimo debe ser menor que el valor máximo.");
        if (sensor.VariacionMaxima <= 0)
            throw new ExcepcionValidacion("La variación máxima debe ser mayor que cero.");
        if (sensor.ValorActual < sensor.ValorMinimo || sensor.ValorActual > sensor.ValorMaximo)
            throw new ExcepcionValidacion("El valor inicial debe estar dentro del rango del sensor.");
    }
}
