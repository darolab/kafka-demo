using System.Text.Json;
using KafkaDemo.Data;
using KafkaDemo.DTO;
using KafkaDemo.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KafkaDemo.Services;

public sealed class PrivateMessageService(
    AppDbContext context,
    IOptions<NotificationOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task SendAsync(
        int senderId,
        int recipientId,
        string text,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken);

        try
        {
            var recipientExists = await context.Users
                .AnyAsync(user => user.Id == recipientId, cancellationToken);

            if (!recipientExists)
                throw new KeyNotFoundException("Recipient not found.");

            var now = DateTimeOffset.UtcNow;
            var message = new ChatMessage
            {
                SenderId = senderId,
                RecipientId = recipientId,
                Message = text,
                SentAt = now.UtcDateTime,
                Status = "Pending"
            };

            context.ChatMessages.Add(message);
            await context.SaveChangesAsync(cancellationToken);

            var payload = JsonSerializer.SerializeToElement(
                new
                {
                    senderId,
                    recipientId,
                    chatMessageId = message.Id,
                    message = text,
                    timestampUtc = now
                },
                JsonOptions);

            var envelope = new NotificationEnvelope(
                1,
                Guid.NewGuid(),
                recipientId,
                "chat.message.created",
                System.Diagnostics.Activity.Current?.TraceId.ToString()
                    ?? Guid.NewGuid().ToString("N"),
                now,
                payload);

            context.OutboxMessages.Add(new OutboxMessage
            {
                EventType = envelope.EventType,
                Topic = options.Value.NotificationTopic,
                MessageKey = recipientId.ToString(),
                Payload = JsonSerializer.Serialize(envelope, JsonOptions),
                CreatedAt = now.UtcDateTime
            });

            await context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}