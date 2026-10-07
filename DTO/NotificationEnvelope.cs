using System.Text.Json;

namespace KafkaDemo.DTO;

public sealed record NotificationEnvelope(
    int SchemaVersion,
    Guid NotificationId,
    int UserId,
    string EventType,
    string CorrelationId,
    DateTimeOffset TimestampUtc,
    JsonElement Payload);
