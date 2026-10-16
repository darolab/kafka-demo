using KafkaDemo;
using KafkaDemo.Data;
using KafkaDemo.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Data.Sqlite;

namespace KafkaDemo.Tests;

public sealed class AuthenticationTestFactory : WebApplicationFactory<Program>
{
    public const string JwtKey = "test-only-authentication-signing-key-32-bytes";
    public const string JwtIssuer = "KafkaDemo.Tests";
    public const string JwtAudience = "KafkaDemo.Tests.Client";
    public const string BootstrapSecret = "test-only-bootstrap-secret-at-least-32-chars";
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly IPasswordHasher<User>? _passwordHasher;

    public AuthenticationTestFactory(IPasswordHasher<User>? passwordHasher = null)
    {
        _passwordHasher = passwordHasher;
        _connection.Open();
    }

    public MutableTimeProvider Clock { get; } = new();

    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task SeedUserAsync(
        int userId,
        string username,
        string[] roleNames,
        bool isEnabled = true)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roles = await context.Roles
            .Where(role => roleNames.Contains(role.Name))
            .ToListAsync();

        context.Users.Add(new User
        {
            Id = userId,
            Name = $"Test {username}",
            Email = $"{username}@example.test",
            NormalizedEmail = $"{username}@example.test".ToUpperInvariant(),
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            PasswordHash = "test-only-hash",
            IsEnabled = isEnabled,
            CreatedAt = Clock.GetUtcNow().UtcDateTime,
            UserRoles = roles.Select(role => new UserRole { Role = role }).ToList()
        });
        await context.SaveChangesAsync();
    }

    public async Task SetUserRolesAsync(int userId, params string[] roleNames)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await context.Users
            .Include(account => account.UserRoles)
            .SingleAsync(account => account.Id == userId);
        context.UserRoles.RemoveRange(user.UserRoles);
        user.UserRoles.Clear();

        var roles = await context.Roles
            .Where(role => roleNames.Contains(role.Name))
            .ToListAsync();
        foreach (var role in roles)
            user.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id, Role = role });

        await context.SaveChangesAsync();
    }

    public string CreateToken(
        int? userId = null,
        string[]? roles = null,
        string? issuer = null,
        string? audience = null,
        DateTime? expiresUtc = null,
        string? signingKey = null)
    {
        var claims = new List<Claim>();
        if (userId.HasValue)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, userId.Value.ToString()));
            claims.Add(new Claim("userId", userId.Value.ToString()));
        }

        claims.AddRange((roles ?? []).Select(role => new Claim("role", role)));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? JwtKey));
        var token = new JwtSecurityToken(
            issuer: issuer ?? JwtIssuer,
            audience: audience ?? JwtAudience,
            claims: claims,
            expires: expiresUtc ?? Clock.GetUtcNow().AddMinutes(2).UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = JwtKey,
                ["Jwt:Issuer"] = JwtIssuer,
                ["Jwt:Audience"] = JwtAudience,
                ["Authentication:BootstrapSecret"] = BootstrapSecret,
                ["Authentication:MaxFailedLoginAttempts"] = "5",
                ["Authentication:LockoutMinutes"] = "5"
            }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            if (_passwordHasher is not null)
            {
                services.RemoveAll<IPasswordHasher<User>>();
                services.AddSingleton<IPasswordHasher<User>>(_passwordHasher);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _connection.Dispose();

        base.Dispose(disposing);
    }

    public sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
    }
}
