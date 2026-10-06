using Microsoft.EntityFrameworkCore;
using KafkaDemo.Data;
using KafkaDemo.Services;

namespace KafkaDemo.Workers;

public class OutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxWorker> _logger;

    public OutboxWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
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

        var messages =
            await db.OutboxMessages
                .Where(x => x.PublishedAt == null)
                .OrderBy(x => x.Id)
                .Take(20)
                .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                await kafka.ProduceAsync(
                    message.Topic,
                    message.Id.ToString(),
                    message.Payload,
                    cancellationToken);

                message.PublishedAt =
                    DateTime.UtcNow;

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
