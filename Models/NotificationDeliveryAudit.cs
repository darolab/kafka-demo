namespace KafkaDemo.Models;

public sealed class NotificationDeliveryAudit
{
    public long Id { get; set; }
    public Guid NotificationId { get; set; }
    public int UserId { get; set; }
    public Guid? ConnectionId { get; set; }
    public string InstanceId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public string? ErrorDetail { get; set; }
}