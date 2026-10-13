using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SWMatrc.Community.Dominio;

namespace SWMatrc.Community.Datos;

public sealed class ComunidadesDb(DbContextOptions<ComunidadesDb> opciones) : DbContext(opciones)
{
    public DbSet<Comunidad> Comunidades => Set<Comunidad>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        var c = modelo.Entity<Comunidad>();
        c.ToTable("comunidades");
        c.HasKey(x => x.Id);

        // Identidad "by default": la migración inserta las comunidades con el mismo id que
        // tenían en la Fase 2, porque Sensor y Alert las siguen referenciando por ese número.
        c.Property(x => x.Id).UseIdentityByDefaultColumn();

        c.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
        c.Property(x => x.Municipio).HasMaxLength(120).IsRequired();
        c.Property(x => x.Departamento).HasMaxLength(120).IsRequired();
        c.Property(x => x.Pais).HasMaxLength(80).IsRequired();
        c.Property(x => x.Descripcion).HasMaxLength(500);
        c.Property(x => x.Latitud).HasPrecision(9, 6);
        c.Property(x => x.Longitud).HasPrecision(9, 6);

        c.HasIndex(x => x.Nombre).IsUnique();
        c.HasIndex(x => x.Activa);
        c.HasIndex(x => new { x.Departamento, x.Municipio });
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var entrada in ChangeTracker.Entries<Comunidad>().Where(e => e.State == EntityState.Modified))
            entrada.Entity.FechaModificacion = DateTime.UtcNow;

        return base.SaveChangesAsync(ct);
    }
}

/// <summary>Solo para <c>dotnet ef migrations add</c>: no se usa en tiempo de ejecución.</summary>
internal sealed class FabricaDisenio : IDesignTimeDbContextFactory<ComunidadesDb>
{
    public ComunidadesDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ComunidadesDb>()
            .UseNpgsql("Host=localhost;Database=community_db")
            .UseSnakeCaseNamingConvention()
            .Options);
}
