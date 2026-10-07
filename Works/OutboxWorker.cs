using Microsoft.EntityFrameworkCore;
using System.Data;
using KafkaDemo.Data;
using KafkaDemo.Services;
using Microsoft.Extensions.Options;

namespace KafkaDemo.Workers;

public class OutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxWorker> _logger;
    private readonly NotificationOptions _options;
    private readonly NotificationMetrics _metrics;

    public OutboxWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxWorker> logger,
        IOptions<NotificationOptions> options,
        NotificationMetrics metrics)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Outbox worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessMessages(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error processing outbox messages.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(2),
                stoppingToken);
        }
    }

    private async Task ProcessMessages(
        CancellationToken cancellationToken)
    {
        using var scope =
            _scopeFactory.CreateScope();

        var db =
            scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

        var kafka =
            scope.ServiceProvider
                .GetRequiredService<KafkaProducerService>();

        var now = DateTime.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var messages = await db.OutboxMessages
            .FromSqlInterpolated($"""
                SELECT TOP (20) *
                FROM [OutboxMessages] WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE [PublishedAt] IS NULL
                  AND ([LeaseExpiresAtUtc] IS NULL OR [LeaseExpiresAtUtc] <= {now})
                ORDER BY [Id]
                """)
            .ToListAsync(cancellationToken);

        var leaseExpiry = now.AddSeconds(30);
        foreach (var message in messages)
        {
            message.LeaseOwner = _options.InstanceId;
            message.LeaseExpiresAtUtc = leaseExpiry;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                await kafka.ProduceAsync(
                    message.Topic,
                    string.IsNullOrWhiteSpace(message.MessageKey)
                        ? message.Id.ToString()
                        : message.MessageKey,
                    message.Payload,
                    cancellationToken);
                _metrics.RecordPublished(message.Topic);

                message.PublishedAt =
                    DateTime.UtcNow;
                message.LeaseOwner = null;
                message.LeaseExpiresAtUtc = null;

                await db.SaveChangesAsync(
                    cancellationToken);

                _logger.LogInformation(
                    "Outbox message {Id} published.",
                    message.Id);
            }
            catch (Exception ex)
            {
                message.RetryCount++;

                await db.SaveChangesAsync(
                    cancellationToken);

                _logger.LogError(
                    ex,
                    "Failed to publish outbox message {Id}. Retry count: {RetryCount}",
                    message.Id,
                    message.RetryCount);
            }
        }
    }
}
