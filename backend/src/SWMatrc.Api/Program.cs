using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.IdentityModel.Tokens;
using SWMatrc.Api.Hubs;
using SWMatrc.Api.Middleware;
using SWMatrc.Api.Servicios;
using SWMatrc.Api.Trabajos;
using SWMatrc.Application;
using SWMatrc.Application.Abstracciones;
using SWMatrc.Application.Comun;
using SWMatrc.Domain.Enums;
using SWMatrc.Infrastructure;
using SWMatrc.Infrastructure.Persistencia;
using SWMatrc.Infrastructure.Seguridad;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Servicios
// ---------------------------------------------------------------------------

builder.Services
    .AddControllers()
    .AddJsonOptions(opciones =>
    {
        // Los enumerados viajan como texto: el JSON del canal en tiempo real se lee sin
        // tener que mapear números contra el código del backend.
        opciones.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        opciones.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ManejadorExcepciones>();

// El ciclo de monitoreo y el servicio en segundo plano leen la misma sección: así el
// periodo del ciclo y la tolerancia ante el silencio de un sensor no se desincronizan.
builder.Services.Configure<OpcionesMonitoreo>(builder.Configuration.GetSection(OpcionesMonitoreo.Seccion));

builder.Services.AgregarAplicacion();
builder.Services.AgregarInfraestructura(builder.Configuration);

builder.Services.AddScoped<IUsuarioActual, UsuarioActualHttp>();
builder.Services.AddSingleton<INotificadorTiempoReal, NotificadorSignalR>();

builder.Services.AddSignalR(opciones =>
{
    opciones.EnableDetailedErrors = builder.Environment.IsDevelopment();
    // Latido frecuente para que una caída de red se note en segundos, no en minutos:
    // en un sistema de alerta temprana un canal caído sin avisar es peor que no tenerlo.
    opciones.KeepAliveInterval = TimeSpan.FromSeconds(10);
    opciones.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
})
.AddJsonProtocol(opciones =>
{
    opciones.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    opciones.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

builder.Services.AddHostedService<ServicioSimulacion>();

// --- Autenticación por token ---

var opcionesJwt = builder.Configuration.GetSection(OpcionesJwt.Seccion).Get<OpcionesJwt>()
                  ?? throw new InvalidOperationException("Falta la sección de configuración 'Jwt'.");

if (string.IsNullOrWhiteSpace(opcionesJwt.Clave) || opcionesJwt.Clave.Length < 32)
    throw new InvalidOperationException(
        "La clave de firma JWT debe tener al menos 32 caracteres. Defínala en la variable de entorno Jwt__Clave.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opciones =>
    {
        opciones.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = opcionesJwt.Emisor,
            ValidAudience = opcionesJwt.Audiencia,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opcionesJwt.Clave)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        opciones.Events = new JwtBearerEvents
        {
            OnMessageReceived = contexto =>
            {
                // El handshake de un WebSocket no admite cabeceras personalizadas desde el
                // navegador, así que el cliente manda el token en la query string y aquí
                // se traslada al flujo normal de validación.
                var token = contexto.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) &&
                    contexto.HttpContext.Request.Path.StartsWithSegments(MonitoreoHub.Ruta))
                {
                    contexto.Token = token;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(opciones =>
{
    // Los roles son acumulativos: quien administra también puede operar.
    opciones.AddPolicy("Operacion", p => p.RequireRole(
        nameof(RolUsuario.Operador), nameof(RolUsuario.Administrador)));

    opciones.AddPolicy("Administracion", p => p.RequireRole(nameof(RolUsuario.Administrador)));
});

// --- CORS ---
// En producción el frontend se sirve desde el mismo origen a través de nginx y esta
// política no llega a usarse; existe para el desarrollo con `ng serve`.
const string PoliticaCors = "FrontendSwmatrc";
var origenesPermitidos = builder.Configuration.GetSection("Cors:Origenes").Get<string[]>()
                         ?? ["http://localhost:4200"];

builder.Services.AddCors(opciones =>
    opciones.AddPolicy(PoliticaCors, politica => politica
        .WithOrigins(origenesPermitidos)
        .AllowAnyHeader()
        .AllowAnyMethod()
        // Obligatorio para que SignalR pueda negociar la conexión entre orígenes.
        .AllowCredentials()));

var app = builder.Build();

// ---------------------------------------------------------------------------
// Puesta a punto de la base de datos
// ---------------------------------------------------------------------------

using (var ambito = app.Services.CreateScope())
{
    var inicializador = ambito.ServiceProvider.GetRequiredService<InicializadorBaseDatos>();
    await inicializador.InicializarAsync();
}

// ---------------------------------------------------------------------------
// Canalización HTTP
// ---------------------------------------------------------------------------

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// Sin UseHttpsRedirection: dentro de Docker la API escucha en HTTP y es nginx (o el
// proxy inverso de la VPS) quien termina TLS de cara a Internet.

app.UseCors(PoliticaCors);

// Habilita el transporte WebSocket sobre el que viaja el hub.
app.UseWebSockets();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHub<MonitoreoHub>(MonitoreoHub.Ruta, opciones =>
{
    // Se restringe el transporte a WebSockets: el canal es un socket permanente y no
    // debe degradarse a long polling, que añadiría latencia justo donde no se admite.
    opciones.Transports = HttpTransportType.WebSockets;
});

// Sonda para el healthcheck de Docker y para el balanceador de la VPS.
app.MapGet("/health", () => Results.Ok(new { estado = "ok", utc = DateTime.UtcNow }))
   .AllowAnonymous()
   .WithName("Salud");

app.Run();
