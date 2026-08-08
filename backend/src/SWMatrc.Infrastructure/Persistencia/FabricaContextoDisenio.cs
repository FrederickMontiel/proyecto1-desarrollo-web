using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SWMatrc.Infrastructure.Persistencia;

/// <summary>
/// Contexto que usan las herramientas de EF Core al generar migraciones. Evita que
/// <c>dotnet ef</c> tenga que arrancar la API completa —con su configuración, su clave de
/// firma y su conexión real— solo para leer el modelo.
/// </summary>
public sealed class FabricaContextoDisenio : IDesignTimeDbContextFactory<SwmatrcDbContext>
{
    /// <summary>
    /// Cadena de relleno: las migraciones se generan a partir del modelo, no de una base
    /// existente, así que basta con que el proveedor de SQL Server quede configurado.
    /// </summary>
    private const string CadenaDisenio =
        "Server=localhost,1433;Database=SwmatrcDb;User Id=sa;Password=DisenioLocal;TrustServerCertificate=True";

    public SwmatrcDbContext CreateDbContext(string[] args)
    {
        var cadena = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServer") ?? CadenaDisenio;

        var opciones = new DbContextOptionsBuilder<SwmatrcDbContext>()
            .UseSqlServer(cadena, sql => sql.MigrationsAssembly(typeof(SwmatrcDbContext).Assembly.FullName))
            .Options;

        return new SwmatrcDbContext(opciones);
    }
}
