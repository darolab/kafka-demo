using System.Net.WebSockets;
using System.Text.Json;
using KafkaDemo.Data;
using KafkaDemo.DTO;
using KafkaDemo.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KafkaDemo.Services;

public sealed class NotificationDispatcher : INotificationDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IWebSocketConnectionManager _connections;
    private readonly AppDbContext _context;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationDispatcher> _logger;
    private readonly NotificationMetrics? _metrics;

    public NotificationDispatcher(
        AppDbContext context,
        IWebSocketConnectionManager connections,
        IOptions<NotificationOptions> options,
        ILogger<NotificationDispatcher> logger,
        NotificationMetrics? metrics = null)
    {
        _context = context;
        _connections = connections;
        _options = options.Value;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<NotificationDeliveryOutcome> DispatchAsync(
        NotificationEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (envelope.SchemaVersion != 1)
        {
            _logger.LogWarning(
                "Rejected notification with unsupported schema. NotificationId={NotificationId}; SchemaVersion={SchemaVersion}; UserId={UserId}; CorrelationId={CorrelationId}",
                envelope.NotificationId,
                envelope.SchemaVersion,
                envelope.UserId,
                envelope.CorrelationId);
            return await RecordOutcomeAsync(
                envelope,
                NotificationDeliveryOutcome.Rejected,
                "Unsupported schema version.",
                cancellationToken);
        }

        if (envelope.NotificationId == Guid.Empty ||
            envelope.UserId <= 0 ||
            string.IsNullOrWhiteSpace(envelope.EventType) ||
            string.IsNullOrWhiteSpace(envelope.CorrelationId) ||
            envelope.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            _logger.LogWarning(
                "Rejected notification with invalid required metadata. NotificationId={NotificationId}; UserId={UserId}; CorrelationId={CorrelationId}",
                envelope.NotificationId,
                envelope.UserId,
                envelope.CorrelationId);
            return await RecordOutcomeAsync(
                envelope,
                NotificationDeliveryOutcome.Rejected,
                "Required notification metadata is invalid.",
                cancellationToken);
        }

        if (!_connections.IsOnline(envelope.UserId))
        {
            return await RecordOutcomeAsync(
                envelope,
                NotificationDeliveryOutcome.NotConnected,
                null,
                cancellationToken);
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(_options.SocketSendTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        try
        {
            var sent = await _connections.SendToUserAsync(
                envelope.UserId,
                payload,
                linked.Token);

            var outcome = sent
                ? NotificationDeliveryOutcome.Delivered
                : NotificationDeliveryOutcome.Failed;
            return await RecordOutcomeAsync(
                envelope,
                outcome,
                sent ? null : "Socket closed before send completed.",
                cancellationToken);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Notification send timed out. NotificationId={NotificationId}; UserId={UserId}; CorrelationId={CorrelationId}; TimeoutSeconds={TimeoutSeconds}",
                envelope.NotificationId,
                envelope.UserId,
                envelope.CorrelationId,
                _options.SocketSendTimeoutSeconds);
            return await RecordOutcomeAsync(
                envelope,
                NotificationDeliveryOutcome.Failed,
                "WebSocket send timed out.",
                cancellationToken);
        }
        catch (WebSocketException exception)
        {
            _logger.LogWarning(
                exception,
                "WebSocket send failed. NotificationId={NotificationId}; UserId={UserId}; CorrelationId={CorrelationId}",
                envelope.NotificationId,
                envelope.UserId,
                envelope.CorrelationId);
            return await RecordOutcomeAsync(
                envelope,
                NotificationDeliveryOutcome.Failed,
                exception.Message,
                cancellationToken);
        }
    }

    private async Task<NotificationDeliveryOutcome> RecordOutcomeAsync(
        NotificationEnvelope envelope,
        NotificationDeliveryOutcome outcome,
        string? errorDetail,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        _context.NotificationDeliveryAudits.Add(new NotificationDeliveryAudit
        {
            NotificationId = envelope.NotificationId,
            UserId = envelope.UserId,
            InstanceId = _options.InstanceId,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
            EventType = envelope.EventType ?? string.Empty,
            Result = outcome switch
            {
                NotificationDeliveryOutcome.Delivered => "sent",
                NotificationDeliveryOutcome.NotConnected => "not_connected",
                NotificationDeliveryOutcome.Rejected => "rejected",
                _ => "failed"
            },
            CreatedAtUtc = now,
            DeliveredAtUtc = outcome == NotificationDeliveryOutcome.Delivered
                ? now
                : null,
            ErrorDetail = errorDetail
        });
        await _context.SaveChangesAsync(cancellationToken);
        _metrics?.RecordDelivery(outcome, envelope.EventType ?? string.Empty);

        _logger.LogInformation(
            "Notification delivery recorded. NotificationId={NotificationId}; UserId={UserId}; InstanceId={InstanceId}; CorrelationId={CorrelationId}; EventType={EventType}; Outcome={Outcome}",
            envelope.NotificationId,
            envelope.UserId,
            _options.InstanceId,
            envelope.CorrelationId,
            envelope.EventType,
            outcome);

        return outcome;
    }
}
