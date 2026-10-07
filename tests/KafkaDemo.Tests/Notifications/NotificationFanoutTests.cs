using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using Confluent.Kafka;
using KafkaDemo.DTO;
using KafkaDemo.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Kafka;
using Xunit;

namespace KafkaDemo.Tests.Notifications;

[Collection("KafkaContainer")]
public sealed class NotificationFanoutTests
{
    [Fact]
    public async Task EveryInstanceGroupReceivesEventAndRoutesOnlyToItsLocalUserSocket()
    {
        await using var kafka = new KafkaBuilder().Build();
        await kafka.StartAsync();

        var topic = $"notification-test-{Guid.NewGuid():N}";
        var targetSocket = new RecordingWebSocket();
        var targetOutcome = new TaskCompletionSource<NotificationDeliveryOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var otherNodeOutcome = new TaskCompletionSource<NotificationDeliveryOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var targetNode = CreateConsumer(
            kafka.GetBootstrapAddress(),
            topic,
            "api-node-a",
            42,
            targetSocket,
            targetOutcome);
        var otherNode = CreateConsumer(
            kafka.GetBootstrapAddress(),
            topic,
            "api-node-b",
            null,
            null,
            otherNodeOutcome);

        await targetNode.Consumer.StartAsync(CancellationToken.None);
        await otherNode.Consumer.StartAsync(CancellationToken.None);

        var envelope = new NotificationEnvelope(
            1,
            Guid.NewGuid(),
            42,
            "transaction.created",
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            JsonDocument.Parse("{\"message\":\"created\"}").RootElement.Clone());

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = kafka.GetBootstrapAddress(),
            EnableIdempotence = true,
            Acks = Acks.All
        };

        using (var producer = new ProducerBuilder<string, string>(producerConfig).Build())
        {
            await producer.ProduceAsync(
                topic,
                new Message<string, string>
                {
                    Key = envelope.UserId.ToString(),
                    Value = JsonSerializer.Serialize(
                        envelope,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web))
                });
        }

        var outcomes = await Task.WhenAll(
                targetOutcome.Task,
                otherNodeOutcome.Task)
            .WaitAsync(TimeSpan.FromSeconds(45));

        Assert.Contains(NotificationDeliveryOutcome.Delivered, outcomes);
        Assert.Contains(NotificationDeliveryOutcome.NotConnected, outcomes);
        Assert.Single(targetSocket.Messages);
        Assert.Equal(envelope.UserId, targetSocket.LastUserId);

        await targetNode.Consumer.StopAsync(CancellationToken.None);
        await otherNode.Consumer.StopAsync(CancellationToken.None);
        targetNode.Provider.Dispose();
        otherNode.Provider.Dispose();
    }

    [Fact]
    public async Task MeetsFiveSecondP95AtDocumentedTwoNodeBaseline()
    {
        await using var kafka = new KafkaBuilder().Build();
        await kafka.StartAsync();

        const int eventCount = 10;
        var topic = $"notification-latency-{Guid.NewGuid():N}";
        var connectionsA = new WebSocketConnectionManager();
        var connectionsB = new WebSocketConnectionManager();
        for (var userId = 1; userId <= 100; userId++)
        {
            var manager = userId % 2 == 1 ? connectionsA : connectionsB;
            manager.AddConnection(userId, new RecordingWebSocket(),
                userId % 2 == 1 ? "api-node-a" : "api-node-b");
        }

        var observerA = new BatchObserver(eventCount);
        var observerB = new BatchObserver(eventCount);
        var nodeA = CreateBatchConsumer(
            kafka.GetBootstrapAddress(), topic, "api-node-a", connectionsA, observerA);
        var nodeB = CreateBatchConsumer(
            kafka.GetBootstrapAddress(), topic, "api-node-b", connectionsB, observerB);

        await nodeA.Consumer.StartAsync(CancellationToken.None);
        await nodeB.Consumer.StartAsync(CancellationToken.None);
        try
        {
            var config = new ProducerConfig
            {
                BootstrapServers = kafka.GetBootstrapAddress(),
                EnableIdempotence = true,
                Acks = Acks.All
            };
            using var producer = new ProducerBuilder<string, string>(config).Build();
            for (var index = 0; index < eventCount; index++)
            {
                var now = DateTimeOffset.UtcNow;
                var envelope = new NotificationEnvelope(
                    1,
                    Guid.NewGuid(),
                    index + 1,
                    "transaction.created",
                    Guid.NewGuid().ToString("N"),
                    now,
                    JsonSerializer.SerializeToElement(new { sequence = index }));
                await producer.ProduceAsync(topic, new Message<string, string>
                {
                    Key = envelope.UserId.ToString(),
                    Value = JsonSerializer.Serialize(
                        envelope,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web))
                });
                await Task.Delay(100);
            }

            await Task.WhenAll(observerA.Completed, observerB.Completed)
                .WaitAsync(TimeSpan.FromSeconds(45));

            var latencies = observerA.LatenciesMilliseconds
                .Concat(observerB.LatenciesMilliseconds)
                .OrderBy(value => value)
                .ToArray();
            var p95 = latencies[(int)Math.Ceiling(latencies.Length * 0.95) - 1];

            Assert.Equal(eventCount, observerA.Count);
            Assert.Equal(eventCount, observerB.Count);
            Assert.True(
                p95 <= 5000,
                $"Baseline p95 was {p95:F0} ms; expected at most 5000 ms.");
        }
        finally
        {
            await nodeA.Consumer.StopAsync(CancellationToken.None);
            await nodeB.Consumer.StopAsync(CancellationToken.None);
            nodeA.Provider.Dispose();
            nodeB.Provider.Dispose();
        }
    }

    private static (NotificationConsumerService Consumer, ServiceProvider Provider) CreateConsumer(
        string bootstrapServers,
        string topic,
        string instanceId,
        int? connectedUserId,
        RecordingWebSocket? socket,
        TaskCompletionSource<NotificationDeliveryOutcome> outcome)
    {
        var services = new ServiceCollection();
        var connections = new WebSocketConnectionManager();
        if (connectedUserId.HasValue && socket is not null)
            connections.AddConnection(connectedUserId.Value, socket);

        services.AddSingleton<IWebSocketConnectionManager>(connections);
        services.AddScoped<INotificationDispatcher>(provider =>
            new RecordingDispatcher(
                provider.GetRequiredService<IWebSocketConnectionManager>(),
                outcome));
        var provider = services.BuildServiceProvider();
        var options = Options.Create(new NotificationOptions
        {
            NotificationTopic = topic,
            NotificationConsumerGroupId = "fanout-test",
            InstanceId = instanceId,
            SocketSendTimeoutSeconds = 5
        });
        options.Value.BootstrapServers = bootstrapServers;

        return (
            new NotificationConsumerService(
                options,
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<NotificationConsumerService>.Instance),
            provider);
    }

    private static (NotificationConsumerService Consumer, ServiceProvider Provider) CreateBatchConsumer(
        string bootstrapServers,
        string topic,
        string instanceId,
        IWebSocketConnectionManager connections,
        BatchObserver observer)
    {
        var services = new ServiceCollection();
        services.AddSingleton(connections);
        services.AddScoped<INotificationDispatcher>(provider =>
            new BatchDispatcher(
                provider.GetRequiredService<IWebSocketConnectionManager>(),
                observer));
        var provider = services.BuildServiceProvider();
        var options = Options.Create(new NotificationOptions
        {
            BootstrapServers = bootstrapServers,
            NotificationTopic = topic,
            NotificationConsumerGroupId = "latency-test",
            InstanceId = instanceId,
            SocketSendTimeoutSeconds = 5
        });

        return (
            new NotificationConsumerService(
                options,
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<NotificationConsumerService>.Instance),
            provider);
    }

    private sealed class RecordingDispatcher(
        IWebSocketConnectionManager connections,
        TaskCompletionSource<NotificationDeliveryOutcome> outcome) : INotificationDispatcher
    {
        public async Task<NotificationDeliveryOutcome> DispatchAsync(
            NotificationEnvelope envelope,
            CancellationToken cancellationToken)
        {
            var sent = await connections.SendToUserAsync(
                envelope.UserId,
                JsonSerializer.SerializeToUtf8Bytes(
                    envelope,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                cancellationToken);
            var result = sent
                ? NotificationDeliveryOutcome.Delivered
                : NotificationDeliveryOutcome.NotConnected;
            outcome.TrySetResult(result);
            return result;
        }
    }

    private sealed class BatchDispatcher(
        IWebSocketConnectionManager connections,
        BatchObserver observer) : INotificationDispatcher
    {
        public async Task<NotificationDeliveryOutcome> DispatchAsync(
            NotificationEnvelope envelope,
            CancellationToken cancellationToken)
        {
            var sent = await connections.SendToUserAsync(
                envelope.UserId,
                JsonSerializer.SerializeToUtf8Bytes(
                    envelope,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                cancellationToken);
            var outcome = sent
                ? NotificationDeliveryOutcome.Delivered
                : NotificationDeliveryOutcome.NotConnected;
            observer.Record(envelope, outcome);
            return outcome;
        }
    }

    private sealed class BatchObserver(int expected)
    {
        private readonly ConcurrentBag<double> _latenciesMilliseconds = [];
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;

        public IReadOnlyCollection<double> LatenciesMilliseconds =>
            _latenciesMilliseconds.ToArray();
        public int Count => Volatile.Read(ref _count);
        public Task Completed => _completion.Task;

        public void Record(
            NotificationEnvelope envelope,
            NotificationDeliveryOutcome outcome)
        {
            _latenciesMilliseconds.Add(
                (DateTimeOffset.UtcNow - envelope.TimestampUtc).TotalMilliseconds);
            if (Interlocked.Increment(ref _count) == expected)
                _completion.TrySetResult();
        }
    }

    private sealed class RecordingWebSocket : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;

        public ConcurrentQueue<byte[]> Messages { get; } = new();
        public int? LastUserId { get; private set; }
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;
        public override void Dispose() => _state = WebSocketState.Closed;

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

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken) =>
            Task.FromResult(new WebSocketReceiveResult(
                0,
                WebSocketMessageType.Close,
                true));

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            Messages.Enqueue(buffer.ToArray());
            using var document = JsonDocument.Parse(buffer);
            LastUserId = document.RootElement.GetProperty("userId").GetInt32();
            return Task.CompletedTask;
        }
    }
}