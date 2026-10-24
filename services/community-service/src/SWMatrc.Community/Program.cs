using Microsoft.EntityFrameworkCore;
using SWMatrc.Comun;
using SWMatrc.Community.Aplicacion;
using SWMatrc.Community.Datos;
using SWMatrc.Community.Integracion;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

builder.Services.AgregarWebSwmatrc();
builder.Services.AgregarSeguridadSwmatrc(builder.Configuration);
builder.Services.AgregarIntegracionSwmatrc();

builder.Services.AddDbContext<ComunidadesDb>(o => o
    .UseNpgsql(
        builder.Configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Defina ConnectionStrings__Postgres."),
        npgsql => npgsql.EnableRetryOnFailure(5))
    .UseSnakeCaseNamingConvention());

builder.Services.AddHealthChecks().AddDbContextCheck<ComunidadesDb>("postgres", tags: [ExtensionesWeb.EtiquetaListo]);

builder.Services.AddMemoryCache();
builder.Services.AgregarClienteInterno<ClienteSensores>(tiempoTotal: TimeSpan.FromSeconds(3));
builder.Services.AddScoped<IFuenteConteoSensores>(sp => sp.GetRequiredService<ClienteSensores>());
builder.Services.AddScoped<ServicioComunidades>();

var app = builder.Build();

// Las migraciones se aplican al arrancar. EF Core toma un candado en la base, así que con
// varias réplicas arrancando a la vez solo una las ejecuta.
using (var ambito = app.Services.CreateScope())
    await ambito.ServiceProvider.GetRequiredService<ComunidadesDb>().Database.MigrateAsync();

app.UsarWebSwmatrc();
app.MapControllers();

app.Run();
