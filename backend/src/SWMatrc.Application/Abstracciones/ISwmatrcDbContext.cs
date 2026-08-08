using Microsoft.EntityFrameworkCore;
using SWMatrc.Domain.Entities;

namespace SWMatrc.Application.Abstracciones;

/// <summary>
/// Vista del contexto de datos que consume la capa de aplicación. Mantiene la
/// dependencia hacia EF Core como detalle de infraestructura y permite sustituir el
/// almacenamiento en pruebas sin tocar los servicios.
/// </summary>
public interface ISwmatrcDbContext
{
    DbSet<Usuario> Usuarios { get; }
    DbSet<Comunidad> Comunidades { get; }
    DbSet<Sensor> Sensores { get; }
    DbSet<Lectura> Lecturas { get; }
    DbSet<Alerta> Alertas { get; }
    DbSet<EventoHistorial> Eventos { get; }
    DbSet<Bitacora> Bitacoras { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
