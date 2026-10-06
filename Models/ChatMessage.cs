namespace KafkaDemo.Models;

public class ChatMessage
{
    public long Id { get; set; }
    public int SenderId { get; set; }
    public int RecipientId { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public string Status { get; set; } = "Pending";
}