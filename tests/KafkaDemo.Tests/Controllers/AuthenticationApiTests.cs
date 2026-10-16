using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using KafkaDemo.Data;
using KafkaDemo.Models;
using KafkaDemo.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KafkaDemo.Tests.Controllers;

public sealed class AuthenticationApiTests
{
    private const string Password = "correct-horse-battery-staple";

    [Fact]
    public async Task RegisterCreatesEnabledUserWithHashedPasswordAndUserRole()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            Registration("casey", "casey@example.test"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Password, body, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FailedLoginCount", body, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.Include(item => item.UserRoles)
            .ThenInclude(link => link.Role)
            .SingleAsync();

        Assert.True(user.IsEnabled);
        Assert.NotEqual(Password, user.PasswordHash);
        Assert.Equal("CASEY", user.NormalizedUsername);
        Assert.Equal("CASEY@EXAMPLE.TEST", user.NormalizedEmail);
        Assert.Equal(["User"], user.UserRoles.Select(link => link.Role.Name));
    }

    [Fact]
    public async Task RegisterRejectsInvalidDataWithoutCreatingAnAccount()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            Registration("casey", "not-an-email", "short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task RegisterRejectsAttemptedRoleAssignment()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                username = "casey",
                email = "casey@example.test",
                fullName = "Casey Example",
                password = Password,
                role = "Admin"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task RegisterRejectsCaseInsensitiveDuplicateUsernameAndEmail()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        Assert.Equal(
            HttpStatusCode.Created,
            (await client.PostAsJsonAsync(
                "/api/auth/register",
                Registration("casey", "casey@example.test"))).StatusCode);

        var duplicateUsername = await client.PostAsJsonAsync(
            "/api/auth/register",
            Registration("CASEY", "different@example.test"));
        var duplicateEmail = await client.PostAsJsonAsync(
            "/api/auth/register",
            Registration("different", "CASEY@EXAMPLE.TEST"));

        Assert.Equal(HttpStatusCode.Conflict, duplicateUsername.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicateEmail.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public async Task LoginReturnsJwtForRegisteredUserWithoutCredentialData()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync(
            "/api/auth/register",
            Registration("casey", "casey@example.test"));

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = "casey", password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tokenString = json.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(tokenString));
        Assert.True(json.RootElement.TryGetProperty("expiresAtUtc", out _));
        Assert.False(json.RootElement.TryGetProperty("password", out _));
        Assert.False(json.RootElement.TryGetProperty("passwordHash", out _));

        var token = new JwtSecurityTokenHandler().ReadJwtToken(tokenString);
        Assert.Contains(token.Claims, claim => claim.Type == "userId" && claim.Value == "1");
        Assert.Contains(token.Claims, claim => claim.Type == "role" && claim.Value == "User");
        Assert.Equal("KafkaDemo.Tests", token.Issuer);
        Assert.Contains("KafkaDemo.Tests.Client", token.Audiences);
    }

    [Fact]
    public async Task GetUserDetailReturnsAuthenticatedUsersProfileWithoutRequestBody()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync(
            "/api/auth/register",
            Registration("casey", "casey@example.test"));

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = "casey", password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();
        var token = loginBody.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/user/getUserDetail");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, body.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("casey", body.RootElement.GetProperty("username").GetString());
        Assert.Equal("casey@example.test", body.RootElement.GetProperty("email").GetString());
        Assert.Equal("Casey Example", body.RootElement.GetProperty("fullName").GetString());
        Assert.Contains(
            body.RootElement.GetProperty("roles").EnumerateArray(),
            role => role.GetString() == "User");
        Assert.False(body.RootElement.TryGetProperty("password", out _));
        Assert.False(body.RootElement.TryGetProperty("passwordHash", out _));
    }

    [Fact]
    public async Task GetUserDetailRequiresBearerToken()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/user/getUserDetail");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginFailureIsGenericForUnknownAndIncorrectCredentials()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync(
            "/api/auth/register",
            Registration("casey", "casey@example.test"));

        var unknown = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = "missing", password = Password });
        var incorrect = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = "casey", password = "incorrect-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, incorrect.StatusCode);
        Assert.Equal(
            await unknown.Content.ReadAsStringAsync(),
            await incorrect.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LoginLocksAfterFiveFailuresAndUnlocksAfterConfiguredDuration()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync(
            "/api/auth/register",
            Registration("casey", "casey@example.test"));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { username = "casey", password = "incorrect-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var locked = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = "casey", password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);

        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var unlocked = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = "casey", password = Password });
        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync();
        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockoutUntilUtc);
    }

    [Fact]
    public async Task ProtectedUserApiRejectsMissingMalformedInvalidIssuerAudienceAndExpiredTokens()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var missing = await client.GetAsync("/api/user");
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        var malformed = await GetUsersWithTokenAsync(client, "not.a.jwt");
        Assert.Equal(HttpStatusCode.Unauthorized, malformed.StatusCode);

        var invalidSignature = await GetUsersWithTokenAsync(
            client,
            factory.CreateToken(
                userId: 1,
                signingKey: "invalid-test-signing-key-which-is-32-bytes"));
        Assert.Equal(HttpStatusCode.Unauthorized, invalidSignature.StatusCode);

        var wrongIssuer = await GetUsersWithTokenAsync(
            client,
            factory.CreateToken(userId: 1, issuer: "UntrustedIssuer"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongIssuer.StatusCode);

        var wrongAudience = await GetUsersWithTokenAsync(
            client,
            factory.CreateToken(userId: 1, audience: "UntrustedAudience"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongAudience.StatusCode);

        var expired = await GetUsersWithTokenAsync(
            client,
            factory.CreateToken(
                userId: 1,
                expiresUtc: factory.Clock.GetUtcNow().AddMinutes(-5).UtcDateTime));
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    private static async Task<HttpResponseMessage> GetUsersWithTokenAsync(
        HttpClient client,
        string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static object Registration(
        string username,
        string email,
        string password = Password) =>
        new
        {
            username,
            email,
            fullName = "Casey Example",
            password
        };
}
