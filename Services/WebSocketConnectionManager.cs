using System.Collections.Concurrent;
using System.Net.WebSockets;
using Microsoft.Extensions.Options;

namespace KafkaDemo.Services;

public sealed class WebSocketConnectionManager : IWebSocketConnectionManager
{
    private sealed class Connection(
        int userId,
        string instanceId,
        WebSocket socket)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public int UserId { get; } = userId;
        public string InstanceId { get; } = instanceId;
        public DateTimeOffset ConnectedAtUtc { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public string Status { get; set; } = "open";
        public WebSocket Socket { get; } = socket;
        public SemaphoreSlim SendLock { get; } = new(1, 1);
    }

    private readonly ConcurrentDictionary<int,
        ConcurrentDictionary<Guid, Connection>> _connections = new();
    private readonly TimeSpan _sendTimeout;

    public WebSocketConnectionManager(IOptions<NotificationOptions>? options = null)
    {
        _sendTimeout = TimeSpan.FromSeconds(
            options?.Value.SocketSendTimeoutSeconds ?? 5);
    }

    public Guid AddConnection(
        int userId,
        WebSocket socket,
        string instanceId = "unknown")
    {
        var connection = new Connection(userId, instanceId, socket);
        var userConnections = _connections.GetOrAdd(
            userId, _ => new ConcurrentDictionary<Guid, Connection>());

        userConnections[connection.Id] = connection;
        return connection.Id;
    }

    public void RemoveConnection(int userId, Guid connectionId)
    {
        if (_connections.TryGetValue(userId, out var userConnections))
        {
            userConnections.TryRemove(connectionId, out _);
            if (userConnections.IsEmpty)
            {
                ((ICollection<KeyValuePair<int,
                    ConcurrentDictionary<Guid, Connection>>>)_connections)
                    .Remove(new KeyValuePair<int,
                        ConcurrentDictionary<Guid, Connection>>(userId, userConnections));
            }
        }
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
            {
                RemoveConnection(userId, connection.Id);
                continue;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(_sendTimeout);
            var lockAcquired = false;
            try
            {
                await connection.SendLock.WaitAsync(timeout.Token);
                lockAcquired = true;

                if (connection.Socket.State != WebSocketState.Open)
                {
                    RemoveConnection(userId, connection.Id);
                    continue;
                }

                await connection.Socket.SendAsync(
                    new ArraySegment<byte>(payload),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    timeout.Token);

                connection.LastSeenAtUtc = DateTimeOffset.UtcNow;
                sent = true;
            }
            catch (WebSocketException)
            {
                connection.Status = "stale";
                connection.Socket.Abort();
                RemoveConnection(userId, connection.Id);
            }
            catch (OperationCanceledException) when (
                timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                connection.Status = "stale";
                connection.Socket.Abort();
                RemoveConnection(userId, connection.Id);
            }
            finally
            {
                if (lockAcquired)
                    connection.SendLock.Release();
            }
        }

        return sent;
    }
}