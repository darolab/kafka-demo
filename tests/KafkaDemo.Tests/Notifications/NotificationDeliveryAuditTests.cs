using System.Net.WebSockets;
using System.Text.Json;
using KafkaDemo.Data;
using KafkaDemo.DTO;
using KafkaDemo.Models;
using KafkaDemo.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KafkaDemo.Tests.Notifications;

public sealed class NotificationDeliveryAuditTests
{
    [Theory]
    [InlineData(1, false, false, NotificationDeliveryOutcome.NotConnected, "not_connected")]
    [InlineData(1, true, false, NotificationDeliveryOutcome.Delivered, "sent")]
    [InlineData(2, false, false, NotificationDeliveryOutcome.Rejected, "rejected")]
    [InlineData(1, true, true, NotificationDeliveryOutcome.Failed, "failed")]
    public async Task PersistsOneAuditRecordForEachDispatchOutcome(
        int schemaVersion,
        bool send,
        bool failSend,
        NotificationDeliveryOutcome expectedOutcome,
        string expectedAuditResult)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var envelope = new NotificationEnvelope(
            schemaVersion,
            Guid.NewGuid(),
            42,
            "transaction.created",
            "corr-test",
            DateTimeOffset.UtcNow,
            JsonDocument.Parse("{\"message\":\"created\"}").RootElement.Clone());
        var dispatcher = new NotificationDispatcher(
            context,
            new OutcomeConnectionManager(send, failSend),
            Options.Create(new NotificationOptions
            {
                BootstrapServers = "localhost:9092",
                NotificationTopic = "notifications",
                NotificationConsumerGroupId = "test",
                InstanceId = "api-test",
                SocketSendTimeoutSeconds = 5
            }),
            NullLogger<NotificationDispatcher>.Instance);

        var outcome = await dispatcher.DispatchAsync(
            envelope,
            CancellationToken.None);

        Assert.Equal(expectedOutcome, outcome);
        var audit = await context.NotificationDeliveryAudits.SingleAsync();
        Assert.Equal(envelope.NotificationId, audit.NotificationId);
        Assert.Equal(envelope.UserId, audit.UserId);
        Assert.Equal("api-test", audit.InstanceId);
        Assert.Equal(envelope.CorrelationId, audit.CorrelationId);
        Assert.Equal(expectedAuditResult, audit.Result);
    }

    private sealed class OutcomeConnectionManager(bool send, bool failSend)
        : IWebSocketConnectionManager
    {
        public Guid AddConnection(int userId, WebSocket socket, string instanceId = "unknown") =>
            throw new NotSupportedException();

        public void RemoveConnection(int userId, Guid connectionId) =>
            throw new NotSupportedException();

        public bool IsOnline(int userId) => send;

        public Task<bool> SendToUserAsync(
            int userId,
            byte[] payload,
            CancellationToken cancellationToken)
        {
            if (failSend)
                throw new WebSocketException("Simulated socket failure.");

            return Task.FromResult(send);
        }
    }
}