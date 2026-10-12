using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using SWMatrc.Gateway;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Servicios
// ---------------------------------------------------------------------------

// El gateway no anuncia qué servidor es: parte de ocultar la infraestructura interna.
builder.WebHost.ConfigureKestrel(opciones => opciones.AddServerHeader = false);

// En Kubernetes el pod recibe SIGTERM y tiene un plazo para terminar: se deja ese margen
// para que las peticiones en curso (una descarga de PDF, por ejemplo) alcancen a cerrar.
builder.Services.Configure<HostOptions>(opciones => opciones.ShutdownTimeout = TimeSpan.FromSeconds(25));

// Los errores que genera el propio gateway (una ruta que no existe, un método no admitido)
// salen en español y con detalle, igual que los de los microservicios.
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = contexto =>
{
    var problema = contexto.ProblemDetails;
    if (problema.Detail is not null)
        return;

    (problema.Title, problema.Detail) = problema.Status switch
    {
        StatusCodes.Status404NotFound => ("Recurso no encontrado", "La ruta solicitada no existe."),
        StatusCodes.Status405MethodNotAllowed => ("Método no permitido", "La ruta no admite este método."),
        StatusCodes.Status401Unauthorized => ("No autenticado", "Falta el token de acceso."),
        StatusCodes.Status403Forbidden => ("Acceso denegado", "No tiene permisos para realizar esta operación."),
        _ => (problema.Title, problema.Detail)
    };
});
builder.Services.AddHealthChecks();

// nginx de la VPS es quien habla con el navegador. Sin esto la IP del cliente sería la del
// proxy y el límite de peticiones castigaría a todos los usuarios como si fueran uno solo.
builder.Services.Configure<ForwardedHeadersOptions>(opciones =>
{
    opciones.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // El proxy llega desde la red de Docker o desde WSL, cuyas direcciones no son fijas.
    // El gateway nunca se publica fuera del loopback, así que confiar en cualquier origen
    // equivale a confiar en el nginx local.
    opciones.KnownIPNetworks.Clear();
    opciones.KnownProxies.Clear();
});

// --- Validación inicial del JWT ---
// Auth Service firma con RS256 y publica su llave pública. El gateway la descubre a partir
// del documento de metadatos (el mismo formato de OpenID Connect), así que en la Fase 4
// basta con apuntar a un proveedor externo sin tocar código.
var opcionesAuth = builder.Configuration.GetSection(OpcionesAutenticacion.Seccion).Get<OpcionesAutenticacion>()
                   ?? throw new InvalidOperationException("Falta la sección de configuración 'Autenticacion'.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opciones =>
    {
        opciones.MetadataAddress = opcionesAuth.UrlMetadatos;
        // Dentro de la red de Docker el Auth Service habla HTTP; TLS termina en nginx.
        opciones.RequireHttpsMetadata = opcionesAuth.RequiereHttps;
        // El gateway no reescribe los claims: los servicios de atrás leen el token tal cual.
        opciones.MapInboundClaims = false;
        opciones.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = opcionesAuth.Emisor,
            ValidAudience = opcionesAuth.Audiencia,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        opciones.Events = new JwtBearerEvents
        {
            OnMessageReceived = contexto =>
            {
                // El handshake de un WebSocket no admite cabeceras personalizadas desde el
                // navegador: SignalR manda el token en la query string.
                var token = contexto.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && contexto.Request.Path.StartsWithSegments("/hubs"))
                    contexto.Token = token;

                return Task.CompletedTask;
            },

            // Sin esto el 401 sale con el texto genérico de ASP.NET, en inglés y sin detalle:
            // el frontend recibiría dos formatos de error distintos según quién rechace.
            OnChallenge = async contexto =>
            {
                contexto.HandleResponse();
                var detalle = contexto.AuthenticateFailure is null
                    ? "Falta el token de acceso."
                    : "El token no es válido o ya venció.";
                await Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "No autenticado", detail: detalle)
                    .ExecuteAsync(contexto.HttpContext);
            },
            OnForbidden = contexto => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Acceso denegado",
                    detail: "No tiene permisos para realizar esta operación.")
                .ExecuteAsync(contexto.HttpContext)
        };
    });

builder.Services.AddAuthorization(opciones =>
    opciones.AddPolicy(Politicas.Autenticado, p => p.RequireAuthenticatedUser()));

// --- Límite de peticiones por IP ---
builder.Services.AddRateLimiter(opciones =>
{
    opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opciones.OnRejected = async (contexto, cancelacion) =>
    {
        if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
            contexto.HttpContext.Response.Headers.RetryAfter = ((int)espera.TotalSeconds).ToString();

        await Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Demasiadas solicitudes",
                detail: "Se superó el límite de solicitudes. Intente de nuevo en unos segundos.")
            .ExecuteAsync(contexto.HttpContext);
    };

    var limites = builder.Configuration.GetSection(OpcionesLimites.Seccion).Get<OpcionesLimites>() ?? new();

    opciones.AddPolicy(Politicas.LimiteGeneral, contexto => RateLimitPartition.GetFixedWindowLimiter(
        IpCliente(contexto),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limites.GeneralPorMinuto,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    // El login es el blanco de los ataques de fuerza bruta: su límite es mucho más corto.
    opciones.AddPolicy(Politicas.LimiteLogin, contexto => RateLimitPartition.GetFixedWindowLimiter(
        IpCliente(contexto),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limites.LoginPorMinuto,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

// --- CORS ---
// En producción el navegador entra por un solo origen (nginx) y no hay CORS que resolver;
// la política existe para el desarrollo con `ng serve` en otro puerto.
var origenesPermitidos = builder.Configuration.GetSection("Cors:Origenes").Get<string[]>()
                         ?? ["http://localhost:4200"];

builder.Services.AddCors(opciones =>
    opciones.AddPolicy(Politicas.CorsFrontend, politica => politica
        .WithOrigins(origenesPermitidos)
        .AllowAnyHeader()
        .AllowAnyMethod()
        // Obligatorio para que SignalR pueda negociar la conexión entre orígenes.
        .AllowCredentials()
        .WithExposedHeaders("Content-Disposition", CorrelacionMiddleware.Cabecera)));

// --- Proxy inverso ---
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(contexto =>
    {
        // Los servicios de atrás no deben filtrar su tecnología ni su versión al exterior.
        contexto.AddResponseHeaderRemove("Server");
        contexto.AddResponseHeaderRemove("X-Powered-By");
        contexto.AddResponseHeaderRemove("X-AspNet-Version");
    });

var app = builder.Build();

// ---------------------------------------------------------------------------
// Canalización HTTP
// ---------------------------------------------------------------------------

app.UseForwardedHeaders();
app.UseMiddleware<CorrelacionMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Sondas para Docker y Kubernetes. "live" solo confirma que el proceso responde; "ready"
// es la que decide si el balanceador le manda tráfico.
app.MapHealthChecks("/health/live").DisableRateLimiting();
app.MapHealthChecks("/health/ready").DisableRateLimiting();

app.MapReverseProxy(proxy =>
{
    proxy.UseMiddleware<ErroresProxyMiddleware>();
});

app.Run();

static string IpCliente(HttpContext contexto) =>
    contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
