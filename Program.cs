using KafkaDemo;
using KafkaDemo.Workers;
using Microsoft.EntityFrameworkCore;
using KafkaDemo.Data;
using KafkaDemo.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using OpenTelemetry.Metrics;
using KafkaDemo.Repository;
using KafkaDemo.Middleware;
using System.Net.WebSockets;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(
                "http://localhost:5173",
                "http://localhost:3000")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

builder.Services
    .AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddPrometheusExporter();
        metrics.AddMeter("System.Runtime");
        metrics.AddMeter(
            "Microsoft.AspNetCore.Hosting",
            "Microsoft.AspNetCore.Server.Kestrel");
    });

var jwtKey = "my-super-secret-key-1234567890AB";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey))
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(token) &&
                    path.StartsWithSegments("/api/WebSocket/connect"))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },

            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtDiagnostics");

                logger.LogWarning(
                    context.Exception,
                    "JWT authentication failed for {Path}",
                    context.HttpContext.Request.Path);

                return Task.CompletedTask;
            },

            OnChallenge = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtDiagnostics");

                logger.LogWarning(
                    "JWT challenge for {Path}. Error={Error}; Description={Description}",
                    context.HttpContext.Request.Path,
                    context.Error,
                    context.ErrorDescription);

                return Task.CompletedTask;
            },

            OnForbidden = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtDiagnostics");

                logger.LogWarning(
                    "JWT-authenticated request forbidden for {Path}. ClaimTypes={ClaimTypes}",
                    context.HttpContext.Request.Path,
                    string.Join(", ", context.Principal?.Claims.Select(claim => claim.Type)
                        ?? Enumerable.Empty<string>()));

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddHttpClient<PostApiClient>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddAuthorization();
// builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddControllers();
builder.Services.AddScoped<ControllerAspect>();

builder.Logging.AddConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

builder.Services.AddSingleton<KafkaProducerService>();
builder.Services.AddSingleton<KafkaProducer>();
builder.Services.AddSingleton<VaultService>();
builder.Services.AddSingleton<IWebSocketConnectionManager, WebSocketConnectionManager>();
builder.Services.AddScoped<PrivateMessageService>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseWebSockets();

app.MapControllers();
app.MapPrometheusScrapingEndpoint();

app.Run();