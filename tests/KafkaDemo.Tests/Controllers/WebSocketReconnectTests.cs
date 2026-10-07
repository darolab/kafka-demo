using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using KafkaDemo;
using KafkaDemo.Services;
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

public sealed class WebSocketReconnectTests
{
    private const string JwtKey = "test-only-notification-signing-key-32-bytes";

    [Fact]
    public async Task ReconnectedUserReceivesANewLiveSessionAfterDisconnect()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Jwt:SigningKey"] = JwtKey
                    }));
                builder.ConfigureTestServices(services =>
                    services.RemoveAll<IHostedService>());
            });
        var manager = factory.Services.GetRequiredService<IWebSocketConnectionManager>();
        var token = CreateToken();
        var uri = new Uri(
            $"ws://localhost/api/WebSocket/connect?access_token={Uri.EscapeDataString(token)}");

        var firstClient = factory.Server.CreateWebSocketClient();
        using var firstSocket = await firstClient.ConnectAsync(uri, CancellationToken.None);
        await WaitUntilAsync(() => manager.IsOnline(42));

        await firstSocket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "test disconnect",
            CancellationToken.None);
        await WaitUntilAsync(() => !manager.IsOnline(42));

        var secondClient = factory.Server.CreateWebSocketClient();
        using var secondSocket = await secondClient.ConnectAsync(uri, CancellationToken.None);
        await WaitUntilAsync(() => manager.IsOnline(42));

        await secondSocket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "test complete",
            CancellationToken.None);
        await WaitUntilAsync(() => !manager.IsOnline(42));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(20, timeout.Token);
    }

    private static string CreateToken()
    {
        var token = new JwtSecurityToken(
            claims: [new Claim("userId", "42")],
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}