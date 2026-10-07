using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Json;
using KafkaDemo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KafkaDemo.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class WebSocketController(
    IWebSocketConnectionManager connections,
    PrivateMessageService messages,
    ILogger<WebSocketController> logger,
    IOptions<NotificationOptions> notificationOptions,
    NotificationMetrics metrics) : ControllerBase
{
    private sealed record IncomingMessage(string RecipientId, string Message);

    [HttpGet("connect")]
    public async Task Connect(CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "WebSocket action reached. Authenticated={Authenticated}; ClaimTypes={ClaimTypes}",
            User.Identity?.IsAuthenticated,
            string.Join(", ", User.Claims.Select(c => c.Type)));

        var userIdClaim = User.Claims.FirstOrDefault(claim =>
            claim.Type.Equals("userId", StringComparison.OrdinalIgnoreCase) ||
            claim.Type.Equals("user_id", StringComparison.OrdinalIgnoreCase) ||
            claim.Type.Equals("uid", StringComparison.OrdinalIgnoreCase) ||
            claim.Type.Equals("id", StringComparison.OrdinalIgnoreCase) ||
            claim.Type.Equals("sub", StringComparison.OrdinalIgnoreCase) ||
            claim.Type.Equals(
                ClaimTypes.NameIdentifier,
                StringComparison.OrdinalIgnoreCase));

        if (!int.TryParse(
                userIdClaim?.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var userId))
        {
            logger.LogWarning(
                "WebSocket rejected inside action: user ID claim missing or not an integer. ClaimFound={ClaimFound}",
                userIdClaim is not null);

            Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        logger.LogInformation("WebSocket accepted for user ID {UserId}", userId);

        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        var connectionId = connections.AddConnection(
            userId,
            socket,
            notificationOptions.Value.InstanceId);
        metrics.RecordConnectionOpened();
        var buffer = new byte[4096];

        try
        {
            while (socket.State == WebSocketState.Open &&
                   !cancellationToken.IsCancellationRequested)
            {
                using var content = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Connection closed",
                            CancellationToken.None);
                        return;
                    }

                    if (result.MessageType != WebSocketMessageType.Text ||
                        content.Length + result.Count > 16_384)
                    {
                        await socket.CloseAsync(
                            WebSocketCloseStatus.MessageTooBig,
                            "Invalid or oversized message",
                            CancellationToken.None);
                        return;
                    }

                    content.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                var incoming = JsonSerializer.Deserialize<IncomingMessage>(
                    content.ToArray(),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));

                if (incoming is null ||
                    !int.TryParse(incoming.RecipientId, out var recipientId) ||
                    string.IsNullOrWhiteSpace(incoming.Message) ||
                    incoming.Message.Length > 4000)
                {
                    continue;
                }

                try
                {
                    await messages.SendAsync(
                        userId,
                        recipientId,
                        incoming.Message,
                        cancellationToken);
                }
                catch (KeyNotFoundException)
                {
                    // Recipient does not exist; do not disclose extra user data.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Connection was cancelled or disconnected.
        }
        catch (WebSocketException)
        {
            // Connection ended unexpectedly.
        }
        catch (IOException exception)
        {
            logger.LogDebug(
                exception,
                "WebSocket transport closed for user {UserId}",
                userId);
        }
        finally
        {
            connections.RemoveConnection(userId, connectionId);
            metrics.RecordConnectionClosed();
        }
    }
}