using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using KafkaDemo.Services;
using Xunit;

namespace KafkaDemo.Tests.Services;

public sealed class NotificationMetricsTests
{
    [Fact]
    public void RecordsNotificationAndConnectionCounters()
    {
        var measurements = new ConcurrentBag<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == NotificationMetrics.MeterName)
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, _, _, _) => measurements.Add(instrument.Name));
        listener.Start();

        using var metrics = new NotificationMetrics();
        metrics.RecordPublished("notification-events");
        metrics.RecordDelivery(NotificationDeliveryOutcome.Delivered, "transaction.created");
        metrics.RecordConnectionOpened();
        metrics.RecordConnectionClosed();

        Assert.Contains("notification.events.published", measurements);
        Assert.Contains("notification.delivery.outcomes", measurements);
        Assert.Contains("websocket.connections.opened", measurements);
        Assert.Contains("websocket.connections.closed", measurements);
    }
}