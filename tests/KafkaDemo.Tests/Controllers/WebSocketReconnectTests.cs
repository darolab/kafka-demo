using System.Net.WebSockets;
using KafkaDemo.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KafkaDemo.Tests.Controllers;

public sealed class WebSocketReconnectTests
{
    [Fact]
    public async Task ReconnectedUserReceivesANewLiveSessionAfterDisconnect()
    {
        await using var factory = new AuthenticationTestFactory();
        await factory.InitializeDatabaseAsync();
        await factory.SeedUserAsync(42, "websocket-reconnect-user", ["User"]);
        var manager = factory.Services.GetRequiredService<IWebSocketConnectionManager>();
        var token = factory.CreateToken(userId: 42, roles: ["User"]);
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

}