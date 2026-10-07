using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace KafkaDemo.Services;

public sealed class NotificationMetrics : IDisposable
{
    public const string MeterName = "KafkaDemo.Notifications";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _published;
    private readonly Counter<long> _deliveryOutcomes;
    private readonly Counter<long> _connectionsOpened;
    private readonly Counter<long> _connectionsClosed;

    public NotificationMetrics()
    {
        _published = _meter.CreateCounter<long>("notification.events.published");
        _deliveryOutcomes = _meter.CreateCounter<long>("notification.delivery.outcomes");
        _connectionsOpened = _meter.CreateCounter<long>("websocket.connections.opened");
        _connectionsClosed = _meter.CreateCounter<long>("websocket.connections.closed");
    }

    public void RecordPublished(string topic)
    {
        var tags = new TagList { { "messaging.destination.name", topic } };
        _published.Add(1, tags);
    }

    public void RecordDelivery(
        NotificationDeliveryOutcome outcome,
        string eventType)
    {
        var tags = new TagList
        {
            { "outcome", outcome.ToString().ToLowerInvariant() },
            { "event.type", eventType }
        };
        _deliveryOutcomes.Add(1, tags);
    }

    public void RecordConnectionOpened() => _connectionsOpened.Add(1);

    public void RecordConnectionClosed() => _connectionsClosed.Add(1);

    public void Dispose() => _meter.Dispose();
}
