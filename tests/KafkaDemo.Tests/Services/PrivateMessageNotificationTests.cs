using System.Text.Json;
using KafkaDemo.Data;
using KafkaDemo.Models;
using KafkaDemo.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Xunit;

namespace KafkaDemo.Tests.Services;

public sealed class PrivateMessageNotificationTests
{
    [Fact]
    public async Task PersistsMessageAndVersionedNotificationOutboxTogether()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var context = CreateContext(connection);
        var recipientId = await AddRecipientAsync(context);
        var service = CreateService(context);

        await service.SendAsync(8, recipientId, "hello", CancellationToken.None);

        var message = await context.ChatMessages.SingleAsync();
        var outbox = await context.OutboxMessages.SingleOrDefaultAsync(
            item => item.EventType == "chat.message.created");

        Assert.NotNull(outbox);
        Assert.Equal(recipientId.ToString(), outbox.MessageKey);
        using var payload = JsonDocument.Parse(outbox.Payload);
        Assert.Equal(1, payload.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(recipientId, payload.RootElement.GetProperty("userId").GetInt32());
        Assert.Equal("Pending", message.Status);
    }

    [Fact]
    public async Task RollsBackMessageWhenOutboxPersistenceFails()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var context = CreateContext(connection, new FailOutboxSaveInterceptor());
        var recipientId = await AddRecipientAsync(context);
        var service = CreateService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendAsync(8, recipientId, "hello", CancellationToken.None));

        Assert.Empty(await context.ChatMessages.ToListAsync());
        Assert.Empty(await context.OutboxMessages.ToListAsync());
    }

    private static async Task<SqliteConnection> OpenDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        return connection;
    }

    private static AppDbContext CreateContext(
        SqliteConnection connection,
        SaveChangesInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection);
        if (interceptor is not null)
            options.AddInterceptors(interceptor);
        return new AppDbContext(options.Options);
    }

    private static async Task<int> AddRecipientAsync(AppDbContext context)
    {
        var recipient = new User
        {
            Name = "Recipient",
            Email = $"recipient-{Guid.NewGuid():N}@example.test",
            CreatedAt = DateTime.UtcNow
        };
        context.Users.Add(recipient);
        await context.SaveChangesAsync();
        return recipient.Id;
    }

    private static PrivateMessageService CreateService(AppDbContext context) =>
        new(context, Options.Create(new NotificationOptions
        {
            NotificationTopic = "notification-events"
        }));

    private sealed class FailOutboxSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<OutboxMessage>()
                    .Any(entry => entry.State == EntityState.Added) == true)
            {
                throw new InvalidOperationException("Simulated outbox persistence failure.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}