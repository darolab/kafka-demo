namespace KafkaDemo.Models;

public class OutboxMessage
{
    public long Id { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public string Topic { get; set; } = string.Empty;

    public string MessageKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? PublishedAt { get; set; }

    public string? LeaseOwner { get; set; }

    public DateTime? LeaseExpiresAtUtc { get; set; }

    public int RetryCount { get; set; }
}