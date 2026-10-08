using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Dtos;
using SWMatrc.Domain.Entities;

namespace SWMatrc.Application.Servicios;

/// <inheritdoc cref="IServicioBitacora"/>
public sealed class ServicioBitacora(
    ISwmatrcDbContext db,
    IUsuarioActual usuarioActual,
    ILogger<ServicioBitacora> logger) : IServicioBitacora
{
    public Task RegistrarAsync(
        string accion, string entidad, object? entidadId = null, object? detalle = null, CancellationToken ct = default) =>
        EscribirAsync(usuarioActual.Id, usuarioActual.Email ?? "sistema", accion, entidad, entidadId, detalle, ct);

    public Task RegistrarComoAsync(
        int usuarioId, string usuarioEmail, string accion, string entidad,
        object? entidadId = null, object? detalle = null, CancellationToken ct = default) =>
        EscribirAsync(usuarioId, usuarioEmail, accion, entidad, entidadId, detalle, ct);

    private async Task EscribirAsync(
        int? usuarioId, string usuarioEmail, string accion, string entidad,
        object? entidadId, object? detalle, CancellationToken ct)
    {
        try
        {
            db.Bitacoras.Add(new Bitacora
            {
                UsuarioId = usuarioId,
                UsuarioEmail = usuarioEmail,
                Accion = accion,
                Entidad = entidad,
                EntidadId = entidadId?.ToString(),
                Detalle = detalle is null ? null : JsonSerializer.Serialize(detalle),
                DireccionIp = usuarioActual.DireccionIp,
                FechaHora = DateTime.UtcNow
            });

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // La auditoría es un efecto colateral: si falla se reporta, pero no se
            // aborta la acción de negocio que el usuario ya completó.
            logger.LogError(ex, "No se pudo registrar en bitácora la acción {Accion} sobre {Entidad}", accion, entidad);
        }
    }

    public async Task<PaginaDto<BitacoraDto>> ListarAsync(FiltroBitacora filtro, CancellationToken ct = default)
    {
        var pagina = Math.Max(1, filtro.Pagina);
        var tamano = Math.Clamp(filtro.Tamano, 1, 200);

        var consulta = db.Bitacoras.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filtro.Usuario))
            consulta = consulta.Where(b => b.UsuarioEmail.Contains(filtro.Usuario.Trim()));

        if (!string.IsNullOrWhiteSpace(filtro.Accion))
            consulta = consulta.Where(b => b.Accion.Contains(filtro.Accion.Trim()));

        if (!string.IsNullOrWhiteSpace(filtro.Entidad))
            consulta = consulta.Where(b => b.Entidad == filtro.Entidad.Trim());

        if (filtro.Desde is { } desde)
            consulta = consulta.Where(b => b.FechaHora >= desde);

        if (filtro.Hasta is { } hasta)
            consulta = consulta.Where(b => b.FechaHora <= hasta);

        var total = await consulta.CountAsync(ct);

        // El mapeo corre en memoria: EF Core no sabe traducir el factory del DTO a SQL.
        var registros = await consulta
            .OrderByDescending(b => b.FechaHora)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync(ct);

        var elementos = registros.Select(BitacoraDto.Desde).ToList();

        return new PaginaDto<BitacoraDto>
        {
            Elementos = elementos,
            Pagina = pagina,
            TamanoPagina = tamano,
            TotalElementos = total
        };
    }

    public async Task<(IReadOnlyList<string> Acciones, IReadOnlyList<string> Entidades)> CatalogosAsync(
        CancellationToken ct = default)
    {
        var acciones = await db.Bitacoras.AsNoTracking().Select(b => b.Accion).Distinct().OrderBy(a => a).ToListAsync(ct);
        var entidades = await db.Bitacoras.AsNoTracking().Select(b => b.Entidad).Distinct().OrderBy(e => e).ToListAsync(ct);
        return (acciones, entidades);
    }
}
