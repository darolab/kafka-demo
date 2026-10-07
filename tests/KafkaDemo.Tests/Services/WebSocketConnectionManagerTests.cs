using System.Collections.Concurrent;
using System.Net.WebSockets;
using KafkaDemo.Services;
using Xunit;

namespace KafkaDemo.Tests.Services;

public sealed class WebSocketConnectionManagerTests
{
    [Fact]
    public async Task SendsOnlyToConnectionsOwnedByTargetUser()
    {
        var manager = new WebSocketConnectionManager();
        var target = new RecordingWebSocket();
        var otherUser = new RecordingWebSocket();
        manager.AddConnection(42, target);
        manager.AddConnection(7, otherUser);

        var sent = await manager.SendToUserAsync(
            42,
            "notification"u8.ToArray(),
            CancellationToken.None);

        Assert.True(sent);
        Assert.Single(target.Messages);
        Assert.Empty(otherUser.Messages);
    }

    [Fact]
    public async Task SerializesConcurrentSendsToTheSameSocket()
    {
        var manager = new WebSocketConnectionManager();
        var socket = new RecordingWebSocket();
        manager.AddConnection(42, socket);
        var payload = "notification"u8.ToArray();

        await Task.WhenAll(
            manager.SendToUserAsync(42, payload, CancellationToken.None),
            manager.SendToUserAsync(42, payload, CancellationToken.None));

        Assert.Equal(2, socket.Messages.Count);
        Assert.Equal(1, socket.MaximumConcurrentSends);
    }

    [Fact]
    public async Task RemovesSocketThatTimesOutDuringSend()
    {
        var manager = new WebSocketConnectionManager(
            Microsoft.Extensions.Options.Options.Create(new NotificationOptions
            {
                SocketSendTimeoutSeconds = 1
            }));
        var socket = new RecordingWebSocket(blockSends: true);
        manager.AddConnection(42, socket, "api-node-a");

        var sent = await manager.SendToUserAsync(
            42,
            "notification"u8.ToArray(),
            CancellationToken.None);

        Assert.False(sent);
        Assert.Equal(WebSocketState.Aborted, socket.State);
        Assert.False(manager.IsOnline(42));
    }

    [Fact]
    public async Task RemovesSocketThatIsAlreadyClosed()
    {
        var manager = new WebSocketConnectionManager();
        var socket = new RecordingWebSocket();
        socket.Dispose();
        manager.AddConnection(42, socket);

        var sent = await manager.SendToUserAsync(
            42,
            "notification"u8.ToArray(),
            CancellationToken.None);

        Assert.False(sent);
        Assert.False(manager.IsOnline(42));
    }

    private sealed class RecordingWebSocket(bool blockSends = false) : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;
        private int _activeSends;
        private int _maximumConcurrentSends;

        public ConcurrentQueue<byte[]> Messages { get; } = new();
        public int MaximumConcurrentSends => _maximumConcurrentSends;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public override void Dispose() => _state = WebSocketState.Closed;

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken) =>
            Task.FromResult(new WebSocketReceiveResult(
                0,
                WebSocketMessageType.Close,
                true));

        public override async Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            if (blockSends)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            var active = Interlocked.Increment(ref _activeSends);
            UpdateMaximum(active);
            try
            {
                await Task.Delay(20, cancellationToken);
                Messages.Enqueue(buffer.ToArray());
            }
            finally
            {
                Interlocked.Decrement(ref _activeSends);
            }
        }

        private void UpdateMaximum(int value)
        {
            int current;
            do
            {
                current = _maximumConcurrentSends;
                if (value <= current)
                    return;
            }
            while (Interlocked.CompareExchange(
                       ref _maximumConcurrentSends,
                       value,
                       current) != current);
        }
    }
}