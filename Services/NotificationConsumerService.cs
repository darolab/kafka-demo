using System.Text.Json;
using Confluent.Kafka;
using KafkaDemo.DTO;
using Microsoft.Extensions.Options;

namespace KafkaDemo.Services;

public sealed class NotificationConsumerService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly NotificationOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationConsumerService> _logger;

    public NotificationConsumerService(
        IOptions<NotificationOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationConsumerService> logger)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, error) =>
                _logger.LogError(
                    "Kafka notification consumer error. Code={Code}; Reason={Reason}",
                    error.Code,
                    error.Reason))
            .Build();

        consumer.Subscribe(_options.NotificationTopic);
        _logger.LogInformation(
            "Notification consumer started. Topic={Topic}; GroupId={GroupId}",
            _options.NotificationTopic,
            _options.ConsumerGroupId);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result;
            try
            {
                result = consumer.Consume(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ConsumeException exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to consume notification event. Topic={Topic}",
                    _options.NotificationTopic);
                continue;
            }

            if (result is null || result.IsPartitionEOF)
                continue;

            NotificationEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<NotificationEnvelope>(
                    result.Message.Value,
                    JsonOptions);
            }
            catch (JsonException exception)
            {
                _logger.LogError(
                    exception,
                    "Rejected malformed notification event. Topic={Topic}; Partition={Partition}; Offset={Offset}",
                    result.Topic,
                    result.Partition.Value,
                    result.Offset.Value);
                consumer.Commit(result);
                continue;
            }

            if (envelope is null)
            {
                _logger.LogError(
                    "Rejected empty notification event. Topic={Topic}; Partition={Partition}; Offset={Offset}",
                    result.Topic,
                    result.Partition.Value,
                    result.Offset.Value);
                consumer.Commit(result);
                continue;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider
                    .GetRequiredService<INotificationDispatcher>();
                var outcome = await dispatcher.DispatchAsync(envelope, stoppingToken);

                _logger.LogInformation(
                    "Notification processed. NotificationId={NotificationId}; UserId={UserId}; EventType={EventType}; CorrelationId={CorrelationId}; Outcome={Outcome}; InstanceId={InstanceId}",
                    envelope.NotificationId,
                    envelope.UserId,
                    envelope.EventType,
                    envelope.CorrelationId,
                    outcome,
                    _options.InstanceId);

                consumer.Commit(result);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Notification dispatch failed; Kafka offset remains uncommitted. NotificationId={NotificationId}; UserId={UserId}; CorrelationId={CorrelationId}",
                    envelope.NotificationId,
                    envelope.UserId,
                    envelope.CorrelationId);
            }
        }
    }
}