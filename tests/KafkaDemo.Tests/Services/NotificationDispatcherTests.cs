using System.Text.Json;
using KafkaDemo.DTO;
using KafkaDemo.Services;
using KafkaDemo.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KafkaDemo.Tests.Services;

public sealed class NotificationDispatcherTests
{
    [Fact]
    public async Task RejectsUnsupportedSchemaVersion()
    {
        var (connection, context) = await CreateContextAsync();
        await using var connectionScope = connection;
        await using var contextScope = context;
        var dispatcher = CreateDispatcher(context);
        var envelope = CreateEnvelope(schemaVersion: 2);

        var outcome = await dispatcher.DispatchAsync(
            envelope,
            CancellationToken.None);

        Assert.Equal(NotificationDeliveryOutcome.Rejected, outcome);
    }

    [Fact]
    public async Task ReturnsNotConnectedWhenUserHasNoLocalSocket()
    {
        var (connection, context) = await CreateContextAsync();
        await using var connectionScope = connection;
        await using var contextScope = context;
        var dispatcher = CreateDispatcher(context);
        var envelope = CreateEnvelope(schemaVersion: 1);

        var outcome = await dispatcher.DispatchAsync(
            envelope,
            CancellationToken.None);

        Assert.Equal(NotificationDeliveryOutcome.NotConnected, outcome);
    }

    private static NotificationDispatcher CreateDispatcher(AppDbContext context) =>
        new(
            context,
            new WebSocketConnectionManager(),
            Options.Create(new NotificationOptions
            {
                BootstrapServers = "localhost:9092",
                NotificationTopic = "notifications",
                NotificationConsumerGroupId = "test",
                InstanceId = "test-node",
                SocketSendTimeoutSeconds = 5
            }),
            NullLogger<NotificationDispatcher>.Instance);

    private static async Task<(SqliteConnection Connection, AppDbContext Context)> CreateContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return (connection, context);
    }

    private static NotificationEnvelope CreateEnvelope(int schemaVersion) =>
        new(
            schemaVersion,
            Guid.NewGuid(),
            42,
            "transaction.created",
            "corr-123",
            DateTimeOffset.UtcNow,
            JsonDocument.Parse("{\"message\":\"created\"}").RootElement.Clone());
}