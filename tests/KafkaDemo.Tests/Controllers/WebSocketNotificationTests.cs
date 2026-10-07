using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using KafkaDemo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace KafkaDemo.Tests.Controllers;

public sealed class WebSocketNotificationTests
{
    private const string JwtKey = "test-only-notification-signing-key-32-bytes";

    [Fact]
    public async Task ServesNotificationPlaygroundAtApplicationRoot()
    {
        using var factory = CreateFactory();
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
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/WebSocket/connect");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RejectsAuthenticatedTokenWithoutUserIdentity()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var token = CreateToken(new Claim(ClaimTypes.Name, "authenticated"));

        var response = await client.GetAsync(
            $"/api/WebSocket/connect?access_token={Uri.EscapeDataString(token)}");

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedUserReachesWebSocketUpgradeRequirement()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var token = CreateToken(new Claim("userId", "42"));

        var response = await client.GetAsync(
            $"/api/WebSocket/connect?access_token={Uri.EscapeDataString(token)}");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LoginTokenUsesSameConfiguredKeyAsWebSocketValidation()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = "user2", password = "123456" });
        loginResponse.EnsureSuccessStatusCode();
        using var login = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        var token = login.RootElement.GetProperty("token").GetString();

        var response = await client.GetAsync(
            $"/api/WebSocket/connect?access_token={Uri.EscapeDataString(token!)}");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:SigningKey"] = JwtKey
                }));
            builder.ConfigureTestServices(services =>
                services.RemoveAll<IHostedService>());
        });

    private static string CreateToken(params Claim[] claims)
    {
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}