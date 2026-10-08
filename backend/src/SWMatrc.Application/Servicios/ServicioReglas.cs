using Microsoft.EntityFrameworkCore;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Comun;
using SWMatrc.Application.Dtos;
using SWMatrc.Domain.Entities;
using SWMatrc.Domain.Enums;

namespace SWMatrc.Application.Servicios;

/// <inheritdoc cref="IServicioReglas"/>
public sealed class ServicioReglas(
    ISwmatrcDbContext db,
    IServicioBitacora bitacora,
    IServicioMonitoreo monitoreo) : IServicioReglas
{
    public async Task<IReadOnlyList<ReglaAlertaDto>> ListarAsync(FiltroReglas filtro, CancellationToken ct = default)
    {
        var consulta = db.ReglasAlerta.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filtro.Busqueda))
        {
            var texto = filtro.Busqueda.Trim();
            consulta = consulta.Where(r => r.Nombre.Contains(texto) || r.Mensaje.Contains(texto));
        }

        if (filtro.TipoSensor is { } tipo)
            consulta = consulta.Where(r => r.TipoSensor == tipo);

        if (filtro.Fenomeno is { } fenomeno)
            consulta = consulta.Where(r => r.Fenomeno == fenomeno);

        if (filtro.Nivel is { } nivel)
            consulta = consulta.Where(r => r.Nivel == nivel);

        if (filtro.Activa is { } activa)
            consulta = consulta.Where(r => r.Activa == activa);

        var reglas = await consulta
            .OrderBy(r => r.TipoSensor)
            .ThenByDescending(r => r.Nivel)
            .ThenBy(r => r.Nombre)
            .ToListAsync(ct);

        return reglas.Select(ReglaAlertaDto.Desde).ToList();
    }

    public async Task<ReglaAlertaDto> ObtenerAsync(int id, CancellationToken ct = default) =>
        ReglaAlertaDto.Desde(await CargarAsync(id, ct));

    public async Task<ReglaAlertaDto> CrearAsync(GuardarReglaRequest request, CancellationToken ct = default)
    {
        Validar(request);
        await ValidarNombreUnicoAsync(request.Nombre.Trim(), null, ct);

        var regla = new ReglaAlerta();
        Aplicar(regla, request);

        db.ReglasAlerta.Add(regla);
        await db.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("ReglaCreada", nameof(ReglaAlerta), regla.Id, Resumen(regla), ct);
        await ReevaluarAsync(ct);

        return ReglaAlertaDto.Desde(regla);
    }

    public async Task<ReglaAlertaDto> ActualizarAsync(int id, GuardarReglaRequest request, CancellationToken ct = default)
    {
        Validar(request);
        var regla = await CargarAsync(id, ct);
        await ValidarNombreUnicoAsync(request.Nombre.Trim(), id, ct);

        var anterior = Resumen(regla);
        Aplicar(regla, request);
        await db.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("ReglaModificada", nameof(ReglaAlerta), regla.Id,
            new { Antes = anterior, Despues = Resumen(regla) }, ct);
        await ReevaluarAsync(ct);

        return ReglaAlertaDto.Desde(regla);
    }

    public async Task<ReglaAlertaDto> CambiarEstadoAsync(int id, bool activa, CancellationToken ct = default)
    {
        var regla = await CargarAsync(id, ct);
        if (regla.Activa == activa)
            return ReglaAlertaDto.Desde(regla);

        regla.Activa = activa;
        await db.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync(activa ? "ReglaActivada" : "ReglaDesactivada",
            nameof(ReglaAlerta), regla.Id, new { regla.Nombre }, ct);
        await ReevaluarAsync(ct);

        return ReglaAlertaDto.Desde(regla);
    }

    public async Task EliminarAsync(int id, CancellationToken ct = default)
    {
        var regla = await CargarAsync(id, ct);

        // Las alertas ya emitidas conservan el nombre de la regla en ReglaNombre; la
        // referencia se suelta aquí para no depender del comportamiento del proveedor.
        var alertas = await db.Alertas.Where(a => a.ReglaAlertaId == id).ToListAsync(ct);
        foreach (var alerta in alertas)
            alerta.ReglaAlertaId = null;

        db.ReglasAlerta.Remove(regla);
        await db.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("ReglaEliminada", nameof(ReglaAlerta), id, new { regla.Nombre }, ct);
        await ReevaluarAsync(ct);
    }

    /// <summary>
    /// Un cambio de reglas puede abrir o cerrar alertas: se reevalúan todas las comunidades
    /// activas en el acto, sin esperar al siguiente ciclo del simulador.
    /// </summary>
    private async Task ReevaluarAsync(CancellationToken ct)
    {
        var ids = await db.Comunidades.Where(c => c.Activa).Select(c => c.Id).ToListAsync(ct);
        foreach (var comunidadId in ids)
            await monitoreo.EvaluarComunidadAsync(comunidadId, ct);
    }

    private static void Validar(GuardarReglaRequest request)
    {
        if (request.ValorMinimo is null && request.ValorMaximo is null)
            throw new ExcepcionValidacion("Indique al menos un valor mínimo o un valor máximo.");

        if (request.ValorMinimo is { } min && request.ValorMaximo is { } max && min > max)
            throw new ExcepcionValidacion("El valor mínimo no puede ser mayor que el valor máximo.");

        if (request.Nivel == NivelAlerta.Verde)
            throw new ExcepcionValidacion("Una regla de alerta debe tener nivel Amarillo, Naranja o Rojo.");

        if (request.Fenomeno == TipoFenomeno.Ninguno)
            throw new ExcepcionValidacion("Seleccione el fenómeno al que corresponde la regla.");
    }

    private static void Aplicar(ReglaAlerta regla, GuardarReglaRequest request)
    {
        regla.Nombre = request.Nombre.Trim();
        regla.TipoSensor = request.TipoSensor;
        regla.ValorMinimo = request.ValorMinimo;
        regla.ValorMaximo = request.ValorMaximo;
        regla.Nivel = request.Nivel;
        regla.Fenomeno = request.Fenomeno;
        regla.Mensaje = request.Mensaje.Trim();
        regla.Activa = request.Activa;
    }

    private static object Resumen(ReglaAlerta r) => new
    {
        r.Nombre,
        TipoSensor = r.TipoSensor.ToString(),
        r.ValorMinimo,
        r.ValorMaximo,
        Nivel = r.Nivel.ToString(),
        Fenomeno = r.Fenomeno.ToString(),
        r.Activa
    };

    private async Task ValidarNombreUnicoAsync(string nombre, int? excluirId, CancellationToken ct)
    {
        if (await db.ReglasAlerta.AnyAsync(r => r.Nombre == nombre && r.Id != excluirId, ct))
            throw new ExcepcionValidacion($"Ya existe una regla llamada '{nombre}'.");
    }

    private async Task<ReglaAlerta> CargarAsync(int id, CancellationToken ct) =>
        await db.ReglasAlerta.FirstOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new ExcepcionNoEncontrado("la regla", id);
}
