using System.ComponentModel.DataAnnotations;

namespace KafkaDemo.Services;

public sealed class NotificationOptions
{
    [Required]
    public string BootstrapServers { get; set; } = string.Empty;

    [Required]
    public string NotificationTopic { get; set; } = string.Empty;

    [Required]
    public string NotificationConsumerGroupId { get; set; } = string.Empty;

    [Required]
    public string InstanceId { get; set; } = string.Empty;

    [Range(1, 60)]
    public int SocketSendTimeoutSeconds { get; set; } = 5;

    public string ConsumerGroupId =>
        $"{NotificationConsumerGroupId}-{InstanceId}";
}