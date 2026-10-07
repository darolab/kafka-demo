using System.Text.Json;
using KafkaDemo.DTO;
using Xunit;

namespace KafkaDemo.Tests.Contracts;

public sealed class NotificationEnvelopeContractTests
{
    [Fact]
    public void SerializesVersionOneEnvelopeWithDocumentedJsonNames()
    {
        using var payloadDocument = JsonDocument.Parse("{\"message\":\"created\"}");
        var envelope = new NotificationEnvelope(
            1,
            Guid.Parse("a2b56a20-0a6b-4a24-8fd6-f55c9b81a7c4"),
            42,
            "transaction.created",
            "corr-123",
            DateTimeOffset.Parse("2026-10-06T12:34:56Z"),
            payloadDocument.RootElement.Clone());

        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(envelope.NotificationId, root.GetProperty("notificationId").GetGuid());
        Assert.Equal(42, root.GetProperty("userId").GetInt32());
        Assert.Equal("transaction.created", root.GetProperty("eventType").GetString());
        Assert.Equal("corr-123", root.GetProperty("correlationId").GetString());
        Assert.Equal("2026-10-06T12:34:56+00:00", root.GetProperty("timestampUtc").GetString());
        Assert.Equal("created", root.GetProperty("payload").GetProperty("message").GetString());
    }
}