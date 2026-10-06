using System.Text.Json;
using KafkaDemo.Data;
using KafkaDemo.Models;
using Microsoft.EntityFrameworkCore;

namespace KafkaDemo.Services;

public sealed class PrivateMessageService(
    AppDbContext context,
    IWebSocketConnectionManager connections)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task SendAsync(
        int senderId,
        int recipientId,
        string text,
        CancellationToken cancellationToken)
    {
        var recipientExists = await context.Users
            .AnyAsync(user => user.Id == recipientId, cancellationToken);

        if (!recipientExists)
            throw new KeyNotFoundException("Recipient not found.");

        var message = new ChatMessage
        {
            SenderId = senderId,
            RecipientId = recipientId,
            Message = text,
            SentAt = DateTime.UtcNow,
            Status = "Pending"
        };

        context.ChatMessages.Add(message);
        await context.SaveChangesAsync(cancellationToken);

        var outgoing = new
        {
            senderId = senderId.ToString(),
            recipientId = recipientId.ToString(),
            message = text,
            timestamp = message.SentAt
        };

        var payload = JsonSerializer.SerializeToUtf8Bytes(outgoing, JsonOptions);

        if (await connections.SendToUserAsync(
                recipientId, payload, cancellationToken))
        {
            message.Status = "Sent";
            message.DeliveredAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}