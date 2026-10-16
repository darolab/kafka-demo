using KafkaDemo;
using KafkaDemo.Workers;
using Microsoft.EntityFrameworkCore;
using KafkaDemo.Data;
using KafkaDemo.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using KafkaDemo.Repository;
using KafkaDemo.Middleware;
using KafkaDemo.Models;
using Microsoft.AspNetCore.Identity;
using System.Net.WebSockets;
using System.Threading.Tasks;
using System.Globalization;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Serilog;
using Elastic.CommonSchema.Serilog;

var builder = WebApplication.CreateBuilder(args);
//Add log
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console(new EcsTextFormatter())
    .WriteTo.File(
        new EcsTextFormatter(),
        @"C:\Logs\KafkaDemo\kafkademo-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7)
    .CreateLogger();

builder.Host.UseSerilog();


builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(
                "http://localhost:5173",
                "http://localhost:8000")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

builder.Services
    .AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddPrometheusExporter();
        metrics.AddMeter("System.Runtime");
        metrics.AddMeter(NotificationMetrics.MeterName);
        metrics.AddMeter(
            "Microsoft.AspNetCore.Hosting",
            "Microsoft.AspNetCore.Server.Kestrel");
    });

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection("Jwt"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<AuthenticationOptions>()
    .Bind(builder.Configuration.GetSection(AuthenticationOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            NameClaimType = JwtRegisteredClaimNames.Name,
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.MapInboundClaims = false;

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

            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                var subjectValue = principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                var compatibilityValue = principal?.FindFirst("userId")?.Value;

                if (!TryGetConsistentUserId(
                        subjectValue,
                        compatibilityValue,
                        out var userId))
                {
                    context.Fail("The token does not identify a valid account.");
                    return;
                }

                var repository = context.HttpContext.RequestServices
                    .GetRequiredService<AuthenticationRepository>();
                var account = await repository.FindEnabledAccountAsync(
                    userId,
                    context.HttpContext.RequestAborted);

                if (account is null)
                {
                    context.Fail("The account is unavailable.");
                    return;
                }

                if (principal?.Identity is not ClaimsIdentity identity)
                {
                    context.Fail("The token identity is invalid.");
                    return;
                }

                foreach (var claim in identity.FindAll("role").ToArray())
                    identity.RemoveClaim(claim);
                foreach (var claim in identity.FindAll(ClaimTypes.Role).ToArray())
                    identity.RemoveClaim(claim);
                foreach (var claim in identity.FindAll(JwtRegisteredClaimNames.Name).ToArray())
                    identity.RemoveClaim(claim);

                identity.AddClaim(new Claim(JwtRegisteredClaimNames.Name, account.Name));
                foreach (var role in account.Roles)
                    identity.AddClaim(new Claim("role", role));
            },

            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtDiagnostics");

                logger.LogWarning(
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
                    "JWT challenge for {Path}",
                    context.HttpContext.Request.Path);

                return Task.CompletedTask;
            },

            OnForbidden = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtDiagnostics");

                logger.LogWarning(
                    "JWT-authenticated request forbidden for {Path}",
                    context.HttpContext.Request.Path);

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>,
    JwtBearerOptionsPostConfigure>();

builder.Services.AddHttpClient<PostApiClient>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<AuthenticationRepository>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireAuthenticatedUser().RequireRole("Admin"));
});
// builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddControllers();
builder.Services.AddScoped<ControllerAspect>();

builder.Services.AddOptions<NotificationOptions>()
    .Configure(options =>
    {
        builder.Configuration.GetSection("Kafka").Bind(options);
        builder.Configuration.GetSection("Notification").Bind(options);
    })
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Logging.AddConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

builder.Services.AddSingleton<KafkaProducerService>();
builder.Services.AddSingleton<KafkaProducer>();
builder.Services.AddSingleton<VaultService>();
builder.Services.AddSingleton<NotificationMetrics>();
builder.Services.AddSingleton<IWebSocketConnectionManager, WebSocketConnectionManager>();
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
builder.Services.AddHostedService<NotificationConsumerService>();
// builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddScoped<PrivateMessageService>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseWebSockets();

app.MapControllers();
app.MapPrometheusScrapingEndpoint();

app.Run();

static bool TryGetConsistentUserId(
    string? subjectValue,
    string? compatibilityValue,
    out int userId)
{
    userId = 0;
    var subjectValid = int.TryParse(
        subjectValue,
        NumberStyles.None,
        CultureInfo.InvariantCulture,
        out var subjectId);
    var compatibilityValid = int.TryParse(
        compatibilityValue,
        NumberStyles.None,
        CultureInfo.InvariantCulture,
        out var compatibilityId);

    if (subjectValue is not null && !subjectValid ||
        compatibilityValue is not null && !compatibilityValid ||
        !subjectValid && !compatibilityValid ||
        subjectValid && compatibilityValid && subjectId != compatibilityId)
    {
        return false;
    }

    userId = subjectValid ? subjectId : compatibilityId;
    return userId > 0;
}