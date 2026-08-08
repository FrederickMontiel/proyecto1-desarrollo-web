using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Infrastructure.Persistencia;
using SWMatrc.Infrastructure.Seguridad;
using SWMatrc.Infrastructure.Simulacion;

namespace SWMatrc.Infrastructure;

/// <summary>Registro de la persistencia, la seguridad y la fuente de datos simulada.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AgregarInfraestructura(
        this IServiceCollection services, IConfiguration configuration)
    {
        var cadena = configuration.GetConnectionString("SqlServer")
            ?? throw new InvalidOperationException(
                "Falta la cadena de conexión 'ConnectionStrings:SqlServer'. " +
                "En Docker se inyecta mediante la variable de entorno ConnectionStrings__SqlServer.");

        services.AddDbContext<SwmatrcDbContext>(opciones =>
            opciones.UseSqlServer(cadena, sql =>
            {
                sql.MigrationsAssembly(typeof(SwmatrcDbContext).Assembly.FullName);
                // Reintento ante caídas transitorias del enlace con el contenedor de SQL Server.
                sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
            }));

        services.AddScoped<ISwmatrcDbContext>(sp => sp.GetRequiredService<SwmatrcDbContext>());
        services.AddScoped<InicializadorBaseDatos>();

        services.Configure<OpcionesJwt>(configuration.GetSection(OpcionesJwt.Seccion));
        services.AddSingleton<IHasheadorPassword, HasheadorPasswordBCrypt>();
        services.AddSingleton<IGeneradorToken, GeneradorTokenJwt>();

        // El simulador conserva el episodio climático en curso, así que debe ser único
        // para toda la aplicación: con instancias por petición perdería su continuidad.
        services.AddSingleton<ISimuladorClima, SimuladorClimaEnMemoria>();

        return services;
    }
}
