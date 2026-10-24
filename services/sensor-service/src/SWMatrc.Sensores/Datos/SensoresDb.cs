using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SWMatrc.Sensores.Dominio;

namespace SWMatrc.Sensores.Datos;

public sealed class SensoresDb(DbContextOptions<SensoresDb> opciones) : DbContext(opciones)
{
    public DbSet<Sensor> Sensores => Set<Sensor>();
    public DbSet<Lectura> Lecturas => Set<Lectura>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        // Mismos nombres de tabla y columna que en la Fase 2 (convención de SQL Server): la
        // migración de datos puede copiar fila por fila conservando los id.
        var s = modelo.Entity<Sensor>();
        s.ToTable("Sensores");
        s.HasKey(x => x.Id);
        s.Property(x => x.Codigo).HasMaxLength(30).IsRequired();
        s.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
        s.Property(x => x.UnidadMedida).HasMaxLength(15).IsRequired();
        s.Property(x => x.Tipo).HasConversion<int>();
        s.Property(x => x.Estado).HasConversion<int>();
        s.Property(x => x.NivelActual).HasConversion<int?>();
        s.Property(x => x.Ubicacion).HasMaxLength(200);
        s.Property(x => x.Descripcion).HasMaxLength(500);
        s.Property(x => x.Latitud).HasPrecision(9, 6);
        s.Property(x => x.Longitud).HasPrecision(9, 6);
        s.Property(x => x.ValorMinimo).HasPrecision(10, 3);
        s.Property(x => x.ValorMaximo).HasPrecision(10, 3);
        s.Property(x => x.VariacionMaxima).HasPrecision(10, 3);
        s.Property(x => x.ValorActual).HasPrecision(10, 3);
        s.HasIndex(x => x.Codigo).IsUnique();
        s.HasIndex(x => new { x.ComunidadId, x.Tipo });

        var l = modelo.Entity<Lectura>();
        l.ToTable("Lecturas");
        l.HasKey(x => x.Id);
        l.Property(x => x.Valor).HasPrecision(10, 3);
        l.Property(x => x.UnidadMedida).HasMaxLength(15).IsRequired();
        l.Property(x => x.EstadoSensor).HasConversion<int>();
        l.Property(x => x.Nivel).HasConversion<int?>();

        // Los gráficos filtran por sensor y ventana de tiempo; los reportes, solo por fecha.
        l.HasIndex(x => new { x.SensorId, x.FechaHora }).IsDescending(false, true);
        l.HasIndex(x => x.FechaHora).IsDescending();

        // Las lecturas son del sensor: si el sensor se elimina, su histórico se va con él
        // (las alertas que generó se conservan en el Alert Service).
        l.HasOne(x => x.Sensor).WithMany(x => x.Lecturas).HasForeignKey(x => x.SensorId).OnDelete(DeleteBehavior.Cascade);

        // Todo se guarda en UTC, pero datetime2 no guarda la zona: sin esto las fechas
        // saldrían en el JSON sin "Z" y el navegador las tomaría como hora local.
        var utc = new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var utcNulable = new ValueConverter<DateTime?, DateTime?>(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
        foreach (var propiedad in modelo.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
        {
            if (propiedad.ClrType == typeof(DateTime))
                propiedad.SetValueConverter(utc);
            else if (propiedad.ClrType == typeof(DateTime?))
                propiedad.SetValueConverter(utcNulable);
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var entrada in ChangeTracker.Entries<Sensor>().Where(e => e.State == EntityState.Modified))
            entrada.Entity.FechaModificacion = DateTime.UtcNow;

        return base.SaveChangesAsync(ct);
    }
}

/// <summary>Solo para <c>dotnet ef migrations add</c>: no se usa en tiempo de ejecución.</summary>
internal sealed class FabricaDisenio : IDesignTimeDbContextFactory<SensoresDb>
{
    public SensoresDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SensoresDb>()
            .UseSqlServer("Server=localhost;Database=sensor_db;TrustServerCertificate=True")
            .Options);
}
