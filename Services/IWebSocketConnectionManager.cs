using System.Net.WebSockets;

namespace KafkaDemo.Services;

public interface IWebSocketConnectionManager
{
    Guid AddConnection(int userId, WebSocket socket);
    void RemoveConnection(int userId, Guid connectionId);
    bool IsOnline(int userId);
    Task<bool> SendToUserAsync(
        int userId,
        byte[] payload,
        CancellationToken cancellationToken);
}