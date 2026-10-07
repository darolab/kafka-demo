using KafkaDemo.Services;
using Xunit;

namespace KafkaDemo.Tests.Services;

public sealed class NotificationOptionsTests
{
    [Fact]
    public void ConsumerGroupIdIncludesStableInstanceId()
    {
        var options = new NotificationOptions
        {
            NotificationTopic = "notification-events",
            NotificationConsumerGroupId = "kafka-demo-notifications",
            InstanceId = "api-2",
            SocketSendTimeoutSeconds = 5
        };

        Assert.Equal("kafka-demo-notifications-api-2", options.ConsumerGroupId);
    }
}