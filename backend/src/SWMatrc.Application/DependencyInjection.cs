using Microsoft.Extensions.DependencyInjection;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Servicios;
using SWMatrc.Domain.Riesgo;
using SWMatrc.Domain.Riesgo.Reglas;

namespace SWMatrc.Application;

/// <summary>Registro de los servicios de aplicación y del catálogo de reglas de riesgo.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AgregarAplicacion(this IServiceCollection services)
    {
        services.AddScoped<IServicioBitacora, ServicioBitacora>();
        services.AddScoped<IServicioAutenticacion, ServicioAutenticacion>();
        services.AddScoped<IServicioSensores, ServicioSensores>();
        services.AddScoped<IServicioMonitoreo, ServicioMonitoreo>();
        services.AddScoped<IServicioAlertas, ServicioAlertas>();
        services.AddScoped<IServicioHistorial, ServicioHistorial>();

        // Las reglas son inmutables y sin estado, así que una sola instancia basta.
        // Añadir un fenómeno nuevo se reduce a sumar una línea aquí.
        services.AddSingleton<IReglaRiesgo, ReglaInundacion>();
        services.AddSingleton<IReglaRiesgo, ReglaTormenta>();
        services.AddSingleton<IReglaRiesgo, ReglaHelada>();
        services.AddSingleton<IReglaRiesgo, ReglaSequia>();
        services.AddSingleton<IReglaRiesgo, ReglaIncendioForestal>();
        services.AddSingleton<IMotorRiesgo, MotorRiesgo>();

        return services;
    }
}
