using Microsoft.EntityFrameworkCore;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Domain.Common;
using SWMatrc.Domain.Entities;

namespace SWMatrc.Infrastructure.Persistencia;

/// <summary>Contexto de EF Core sobre SQL Server. Concentra el mapeo relacional del sistema.</summary>
public class SwmatrcDbContext(DbContextOptions<SwmatrcDbContext> options)
    : DbContext(options), ISwmatrcDbContext
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Comunidad> Comunidades => Set<Comunidad>();
    public DbSet<Sensor> Sensores => Set<Sensor>();
    public DbSet<Lectura> Lecturas => Set<Lectura>();
    public DbSet<Alerta> Alertas => Set<Alerta>();
    public DbSet<EventoHistorial> Eventos => Set<EventoHistorial>();
    public DbSet<Bitacora> Bitacoras => Set<Bitacora>();
    public DbSet<ReglaAlerta> ReglasAlerta => Set<ReglaAlerta>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Las configuraciones viven en clases IEntityTypeConfiguration, una por entidad.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SwmatrcDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Sella FechaModificacion sin obligar a cada servicio a recordarlo.
        foreach (var entrada in ChangeTracker.Entries<EntidadBase>().Where(e => e.State == EntityState.Modified))
            entrada.Entity.FechaModificacion = DateTime.UtcNow;

        return base.SaveChangesAsync(cancellationToken);
    }
}
