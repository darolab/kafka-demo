using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace KafkaDemo.Tests.Controllers;

public sealed class WebSocketNotificationTests
{
    [Fact]
    public async Task ServesNotificationPlaygroundAtApplicationRoot()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Private notifications", html);
        Assert.Contains("/api/WebSocket/connect", html);
    }

    [Fact]
    public async Task RejectsConnectionWithoutJwt()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/WebSocket/connect");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RejectsAuthenticatedTokenWithoutUserIdentity()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        var token = factory.CreateToken(roles: ["User"]);

        var response = await client.GetAsync(
            $"/api/WebSocket/connect?access_token={Uri.EscapeDataString(token)}");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedUserReachesWebSocketUpgradeRequirement()
    {
        await using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        var token = factory.CreateToken(userId: 42, roles: ["User"]);

        var response = await client.GetAsync(
            $"/api/WebSocket/connect?access_token={Uri.EscapeDataString(token)}");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LoginTokenUsesSameConfiguredKeyAsWebSocketValidation()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                username = "user2",
                email = "user2@example.test",
                fullName = "Test User",
                password = "correct-horse-battery-staple"
            });
        registerResponse.EnsureSuccessStatusCode();

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = "user2", password = "correct-horse-battery-staple" });
        loginResponse.EnsureSuccessStatusCode();
        using var login = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        var token = login.RootElement.GetProperty("token").GetString();

        var response = await client.GetAsync(
            $"/api/WebSocket/connect?access_token={Uri.EscapeDataString(token!)}");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<AuthenticationTestFactory> CreateFactoryAsync()
    {
        var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        await factory.SeedUserAsync(42, "websocket-user", ["User"]);
        return factory;
    }
}