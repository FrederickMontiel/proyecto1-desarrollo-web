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

    public async Task<PaginaDto<BitacoraDto>> ListarAsync(
        int pagina, int tamano, string? filtroAccion, CancellationToken ct = default)
    {
        pagina = Math.Max(1, pagina);
        tamano = Math.Clamp(tamano, 1, 200);

        var consulta = db.Bitacoras.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filtroAccion))
            consulta = consulta.Where(b => b.Accion.Contains(filtroAccion));

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
}
