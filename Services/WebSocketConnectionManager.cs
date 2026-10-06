using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace KafkaDemo.Services;

public sealed class WebSocketConnectionManager : IWebSocketConnectionManager
{
    private sealed class Connection(WebSocket socket)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public WebSocket Socket { get; } = socket;
        public SemaphoreSlim SendLock { get; } = new(1, 1);
    }

    private readonly ConcurrentDictionary<int,
        ConcurrentDictionary<Guid, Connection>> _connections = new();

    public Guid AddConnection(int userId, WebSocket socket)
    {
        var connection = new Connection(socket);
        var userConnections = _connections.GetOrAdd(
            userId, _ => new ConcurrentDictionary<Guid, Connection>());

        userConnections[connection.Id] = connection;
        return connection.Id;
    }

    public void RemoveConnection(int userId, Guid connectionId)
    {
        if (_connections.TryGetValue(userId, out var userConnections))
            userConnections.TryRemove(connectionId, out _);
    }

    public bool IsOnline(int userId) =>
        _connections.TryGetValue(userId, out var userConnections) &&
        userConnections.Values.Any(c => c.Socket.State == WebSocketState.Open);

    public async Task<bool> SendToUserAsync(
        int userId,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        if (!_connections.TryGetValue(userId, out var userConnections))
            return false;

        var sent = false;

        foreach (var connection in userConnections.Values)
        {
            if (connection.Socket.State != WebSocketState.Open)
                continue;

            await connection.SendLock.WaitAsync(cancellationToken);
            try
            {
                if (connection.Socket.State != WebSocketState.Open)
                    continue;

                await connection.Socket.SendAsync(
                    new ArraySegment<byte>(payload),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken);

                sent = true;
            }
            catch (WebSocketException)
            {
                // The receiving connection will be removed when it disconnects.
            }
            finally
            {
                connection.SendLock.Release();
            }
        }

        return sent;
    }
}