using Microsoft.EntityFrameworkCore;
using SWMatrc.Comun;
using SWMatrc.Sensores.Aplicacion;
using SWMatrc.Sensores.Datos;
using SWMatrc.Sensores.Integracion;
using SWMatrc.Sensores.Simulacion;
using SWMatrc.Sensores.TiempoReal;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

builder.Services.AgregarWebSwmatrc();
// El hub recibe el token en ?access_token= (el navegador no manda cabeceras en el handshake).
builder.Services.AgregarSeguridadSwmatrc(builder.Configuration, rutaTokenEnQuery: "/hubs");
builder.Services.AgregarIntegracionSwmatrc();

builder.Services.AddDbContext<SensoresDb>(o => o.UseSqlServer(
    builder.Configuration.GetConnectionString("SqlServer")
        ?? throw new InvalidOperationException("Defina ConnectionStrings__SqlServer."),
    sql => sql.EnableRetryOnFailure(5)));

builder.Services.AddHealthChecks().AddDbContextCheck<SensoresDb>("sqlserver", tags: [ExtensionesWeb.EtiquetaListo]);

builder.Services.AddSignalR(o =>
{
    // Latido frecuente: en un sistema de alerta temprana un canal caído sin avisar es
    // peor que no tenerlo.
    o.KeepAliveInterval = TimeSpan.FromSeconds(10);
    o.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
}).AddJsonProtocol(o => Json.Configurar(o.PayloadSerializerOptions));

builder.Services.Configure<OpcionesSimulacion>(builder.Configuration.GetSection(OpcionesSimulacion.Seccion));

builder.Services.AddMemoryCache();
builder.Services.AgregarClienteInterno<ClienteComunidades>(tiempoTotal: TimeSpan.FromSeconds(4));
// La evaluación debe caber en un ciclo (3 s por defecto) para no retrasar la adquisición.
builder.Services.AgregarClienteInterno<ClienteAlertas>(tiempoTotal: TimeSpan.FromSeconds(3));
builder.Services.AddScoped<IFuenteComunidades>(sp => sp.GetRequiredService<ClienteComunidades>());
builder.Services.AddScoped<IClienteAlertas>(sp => sp.GetRequiredService<ClienteAlertas>());

builder.Services.AddSingleton<ISimuladorClima, SimuladorClima>();
builder.Services.AddSingleton<INotificador, Notificador>();
builder.Services.AddScoped<ServicioMonitoreo>();
builder.Services.AddScoped<ServicioSensores>();
builder.Services.AddScoped<ServicioLecturas>();
builder.Services.AddHostedService<ServicioSimulacion>();

var app = builder.Build();

// EF Core toma un candado en la base: con varias réplicas solo una aplica las migraciones.
using (var ambito = app.Services.CreateScope())
    await ambito.ServiceProvider.GetRequiredService<SensoresDb>().Database.MigrateAsync();

app.UsarWebSwmatrc();
app.MapControllers();
app.MapHub<MonitoreoHub>(MonitoreoHub.Ruta);

app.Run();
