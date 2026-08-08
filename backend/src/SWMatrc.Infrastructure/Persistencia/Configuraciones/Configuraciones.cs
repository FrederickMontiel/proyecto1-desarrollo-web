using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SWMatrc.Domain.Entities;

namespace SWMatrc.Infrastructure.Persistencia.Configuraciones;

public sealed class UsuarioConfig : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> b)
    {
        b.ToTable("Usuarios");
        b.HasKey(u => u.Id);

        b.Property(u => u.NombreCompleto).HasMaxLength(120).IsRequired();
        b.Property(u => u.Email).HasMaxLength(160).IsRequired();
        b.Property(u => u.PasswordHash).HasMaxLength(200).IsRequired();
        b.Property(u => u.Rol).HasConversion<int>();

        // El correo es la credencial de acceso: se exige único a nivel de motor,
        // no solo en la validación de aplicación.
        b.HasIndex(u => u.Email).IsUnique();
    }
}

public sealed class ComunidadConfig : IEntityTypeConfiguration<Comunidad>
{
    public void Configure(EntityTypeBuilder<Comunidad> b)
    {
        b.ToTable("Comunidades");
        b.HasKey(c => c.Id);

        b.Property(c => c.Nombre).HasMaxLength(120).IsRequired();
        b.Property(c => c.Municipio).HasMaxLength(120);
        b.Property(c => c.Departamento).HasMaxLength(120);
        b.Property(c => c.Latitud).HasPrecision(9, 6);
        b.Property(c => c.Longitud).HasPrecision(9, 6);

        b.HasIndex(c => c.Nombre).IsUnique();
    }
}

public sealed class SensorConfig : IEntityTypeConfiguration<Sensor>
{
    public void Configure(EntityTypeBuilder<Sensor> b)
    {
        b.ToTable("Sensores");
        b.HasKey(s => s.Id);

        b.Property(s => s.Codigo).HasMaxLength(30).IsRequired();
        b.Property(s => s.Nombre).HasMaxLength(120).IsRequired();
        b.Property(s => s.UnidadMedida).HasMaxLength(15).IsRequired();
        b.Property(s => s.Tipo).HasConversion<int>();
        b.Property(s => s.Estado).HasConversion<int>();

        b.Property(s => s.Latitud).HasPrecision(9, 6);
        b.Property(s => s.Longitud).HasPrecision(9, 6);

        foreach (var propiedad in new[]
        {
            nameof(Sensor.ValorMinimo), nameof(Sensor.ValorMaximo), nameof(Sensor.VariacionMaxima),
            nameof(Sensor.ValorActual),
            nameof(Sensor.UmbralAmarilloAlto), nameof(Sensor.UmbralNaranjaAlto), nameof(Sensor.UmbralRojoAlto),
            nameof(Sensor.UmbralAmarilloBajo), nameof(Sensor.UmbralNaranjaBajo), nameof(Sensor.UmbralRojoBajo)
        })
        {
            b.Property(propiedad).HasPrecision(10, 3);
        }

        b.HasIndex(s => s.Codigo).IsUnique();
        b.HasIndex(s => new { s.ComunidadId, s.Tipo });

        b.HasOne(s => s.Comunidad)
            .WithMany(c => c.Sensores)
            .HasForeignKey(s => s.ComunidadId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class LecturaConfig : IEntityTypeConfiguration<Lectura>
{
    public void Configure(EntityTypeBuilder<Lectura> b)
    {
        b.ToTable("Lecturas");
        b.HasKey(l => l.Id);

        b.Property(l => l.Valor).HasPrecision(10, 3);

        // Toda consulta de gráficos filtra por sensor y ventana de tiempo; este índice
        // descendente cubre exactamente ese patrón sobre la tabla más grande del sistema.
        b.HasIndex(l => new { l.SensorId, l.FechaHora }).IsDescending(false, true);

        b.HasOne(l => l.Sensor)
            .WithMany(s => s.Lecturas)
            .HasForeignKey(l => l.SensorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AlertaConfig : IEntityTypeConfiguration<Alerta>
{
    public void Configure(EntityTypeBuilder<Alerta> b)
    {
        b.ToTable("Alertas");
        b.HasKey(a => a.Id);

        b.Property(a => a.Nivel).HasConversion<int>();
        b.Property(a => a.Fenomeno).HasConversion<int>();
        b.Property(a => a.Mensaje).HasMaxLength(600).IsRequired();
        b.Property(a => a.ValorDisparo).HasPrecision(10, 3);

        b.Ignore(a => a.Activa);

        // Índice filtrado: el dashboard consulta constantemente las alertas abiertas,
        // que son una fracción mínima del total acumulado.
        b.HasIndex(a => new { a.ComunidadId, a.FechaCierre })
            .HasFilter("[FechaCierre] IS NULL")
            .HasDatabaseName("IX_Alertas_Abiertas");

        b.HasIndex(a => a.FechaHora).IsDescending();

        b.HasOne(a => a.Comunidad)
            .WithMany(c => c.Alertas)
            .HasForeignKey(a => a.ComunidadId)
            .OnDelete(DeleteBehavior.Cascade);

        // Sin accion en cascada por dos motivos. Primero, borrar evidencia porque cambie
        // el inventario de sensores seria inaceptable. Segundo, SQL Server rechaza tener
        // dos caminos de borrado en cascada hacia la misma tabla, y Comunidad ya alcanza
        // Alertas tanto directamente como a traves de Sensores. La baja de un sensor pone
        // SensorId en nulo de forma explicita en el servicio correspondiente.
        b.HasOne(a => a.Sensor)
            .WithMany(s => s.Alertas)
            .HasForeignKey(a => a.SensorId)
            .OnDelete(DeleteBehavior.NoAction);

        // Las cuentas no se eliminan, se deshabilitan: no hace falta accion en cascada.
        b.HasOne(a => a.ReconocidaPorUsuario)
            .WithMany()
            .HasForeignKey(a => a.ReconocidaPorUsuarioId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class EventoHistorialConfig : IEntityTypeConfiguration<EventoHistorial>
{
    public void Configure(EntityTypeBuilder<EventoHistorial> b)
    {
        b.ToTable("EventosHistorial");
        b.HasKey(e => e.Id);

        b.Property(e => e.Fenomeno).HasConversion<int>();
        b.Property(e => e.NivelMaximo).HasConversion<int>();
        b.Property(e => e.Descripcion).HasMaxLength(600).IsRequired();
        b.Property(e => e.OrigenSensor).HasMaxLength(160);
        b.Property(e => e.ValorRegistrado).HasPrecision(10, 3);

        b.Ignore(e => e.Duracion);

        b.HasIndex(e => new { e.ComunidadId, e.FechaInicio }).IsDescending(false, true);
        b.HasIndex(e => e.Fenomeno);

        b.HasOne(e => e.Comunidad)
            .WithMany()
            .HasForeignKey(e => e.ComunidadId)
            .OnDelete(DeleteBehavior.Cascade);

        // El historial sobrevive a la depuracion de alertas antiguas. Igual que en Alertas,
        // se evita el segundo camino en cascada desde Comunidad.
        b.HasOne(e => e.Alerta)
            .WithMany()
            .HasForeignKey(e => e.AlertaId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class BitacoraConfig : IEntityTypeConfiguration<Bitacora>
{
    public void Configure(EntityTypeBuilder<Bitacora> b)
    {
        b.ToTable("Bitacora");
        b.HasKey(x => x.Id);

        b.Property(x => x.UsuarioEmail).HasMaxLength(160).IsRequired();
        b.Property(x => x.Accion).HasMaxLength(80).IsRequired();
        b.Property(x => x.Entidad).HasMaxLength(80).IsRequired();
        b.Property(x => x.EntidadId).HasMaxLength(40);
        b.Property(x => x.Detalle).HasMaxLength(2000);
        b.Property(x => x.DireccionIp).HasMaxLength(60);

        b.HasIndex(x => x.FechaHora).IsDescending();
        b.HasIndex(x => x.Accion);

        // La bitácora conserva el rastro aunque la cuenta se elimine: por eso el correo
        // se guarda desnormalizado y la relación queda en nulo.
        b.HasOne(x => x.Usuario)
            .WithMany(u => u.AccionesRegistradas)
            .HasForeignKey(x => x.UsuarioId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
