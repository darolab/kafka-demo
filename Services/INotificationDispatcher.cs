using KafkaDemo.DTO;

namespace KafkaDemo.Services;

public interface INotificationDispatcher
{
    Task<NotificationDeliveryOutcome> DispatchAsync(
        NotificationEnvelope envelope,
        CancellationToken cancellationToken);
}

public enum NotificationDeliveryOutcome
{
    Delivered,
    NotConnected,
    Rejected,
    Failed
}