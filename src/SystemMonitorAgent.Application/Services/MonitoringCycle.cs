using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemMonitorAgent.Application.Abstractions;
using SystemMonitorAgent.Application.Delivery;
using SystemMonitorAgent.Core.Configuration;
using SystemMonitorAgent.Core.Models;

namespace SystemMonitorAgent.Application.Services;

/// <summary>
/// Выполняет один полный цикл мониторинга: повторные отправки, сбор нового снимка и его доставка.
/// </summary>
public sealed class MonitoringCycle : IMonitoringCycle
{
    private readonly ISystemInfoCollector _collector;
    private readonly IApiSender _sender;
    private readonly IRetryQueue _retryQueue;
    private readonly AgentSettings _settings;
    private readonly ILogger<MonitoringCycle> _logger;

    public MonitoringCycle(
        ISystemInfoCollector collector,
        IApiSender sender,
        IRetryQueue retryQueue,
        IOptions<AgentSettings> settings,
        ILogger<MonitoringCycle> logger)
    {
        _collector = collector;
        _sender = sender;
        _retryQueue = retryQueue;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (_retryQueue.Count > 0)
        {
            _logger.LogDebug(
                "Перед сбором нового снимка обрабатывается очередь повторных отправок. Размер очереди: {RetryQueueCount}",
                _retryQueue.Count);
        }

        await DrainRetryQueueAsync(cancellationToken);

        _logger.LogDebug("Собирается текущий системный снимок");
        var snapshot = await _collector.CollectAsync(cancellationToken);
        var deliveryResult = await _sender.SendAsync(snapshot, cancellationToken);

        if (deliveryResult.IsSuccess)
        {
            _logger.LogInformation(
                "Системный снимок успешно отправлен. Hostname: {Hostname}, CollectedAtUtc: {CollectedAtUtc}",
                snapshot.Hostname,
                snapshot.CollectedAtUtc);
            return;
        }

        if (!deliveryResult.ShouldRetry)
        {
            _logger.LogError(
                "Текущий снимок отброшен из-за неретрайбл ошибки доставки. Причина: {FailureReason}, StatusCode: {StatusCode}, Hostname: {Hostname}, CollectedAtUtc: {CollectedAtUtc}",
                deliveryResult.Description,
                (int?)deliveryResult.StatusCode,
                snapshot.Hostname,
                snapshot.CollectedAtUtc);
            return;
        }

        var retryDelivery = CreateInitialRetry(snapshot, deliveryResult);
        _retryQueue.Enqueue(retryDelivery);
        _logger.LogWarning(
            "Текущий снимок поставлен в очередь на повторную отправку. Размер очереди: {RetryQueueCount}, AttemptCount: {AttemptCount}, NextRetryAtUtc: {NextRetryAtUtc}, FailureReason: {FailureReason}, Hostname: {Hostname}, CollectedAtUtc: {CollectedAtUtc}",
            _retryQueue.Count,
            retryDelivery.AttemptCount,
            retryDelivery.NextRetryAtUtc,
            retryDelivery.LastFailureReason,
            snapshot.Hostname,
            snapshot.CollectedAtUtc);
    }

    private async Task DrainRetryQueueAsync(CancellationToken cancellationToken)
    {
        var deliveredRetryCount = 0;

        while (_retryQueue.TryDequeueDue(DateTimeOffset.UtcNow, out var delivery))
        {
            if (delivery is null)
            {
                continue;
            }

            _logger.LogDebug(
                "Выполняется повторная отправка снимка из очереди. AttemptNumber: {AttemptNumber}, QueueSize: {RetryQueueCount}, NextRetryAtUtc: {NextRetryAtUtc}, Hostname: {Hostname}, CollectedAtUtc: {CollectedAtUtc}",
                delivery.AttemptCount + 1,
                _retryQueue.Count,
                delivery.NextRetryAtUtc,
                delivery.Snapshot.Hostname,
                delivery.Snapshot.CollectedAtUtc);

            var deliveryResult = await _sender.SendAsync(delivery.Snapshot, cancellationToken);
            if (deliveryResult.IsSuccess)
            {
                deliveredRetryCount++;
                continue;
            }

            if (!deliveryResult.ShouldRetry)
            {
                _logger.LogError(
                    "Снимок из очереди отброшен из-за неретрайбл ошибки доставки. AttemptNumber: {AttemptNumber}, AgeSeconds: {AgeSeconds}, FailureReason: {FailureReason}, StatusCode: {StatusCode}, Hostname: {Hostname}, CollectedAtUtc: {CollectedAtUtc}",
                    delivery.AttemptCount + 1,
                    (DateTimeOffset.UtcNow - delivery.CreatedAtUtc).TotalSeconds,
                    deliveryResult.Description,
                    (int?)deliveryResult.StatusCode,
                    delivery.Snapshot.Hostname,
                    delivery.Snapshot.CollectedAtUtc);
                continue;
            }

            var rescheduledDelivery = ScheduleNextRetry(delivery, deliveryResult);
            if (rescheduledDelivery is null)
            {
                _logger.LogError(
                    "Снимок из очереди отброшен после достижения лимита повторных попыток. AttemptNumber: {AttemptNumber}, AgeSeconds: {AgeSeconds}, FailureReason: {FailureReason}, Hostname: {Hostname}, CollectedAtUtc: {CollectedAtUtc}",
                    delivery.AttemptCount + 1,
                    (DateTimeOffset.UtcNow - delivery.CreatedAtUtc).TotalSeconds,
                    deliveryResult.Description,
                    delivery.Snapshot.Hostname,
                    delivery.Snapshot.CollectedAtUtc);
                continue;
            }

            _retryQueue.Enqueue(rescheduledDelivery);
            _logger.LogWarning(
                "Снимок из очереди перенесён на следующую попытку после временной ошибки доставки. Размер очереди: {RetryQueueCount}, AttemptCount: {AttemptCount}, NextRetryAtUtc: {NextRetryAtUtc}, FailureReason: {FailureReason}, Hostname: {Hostname}, CollectedAtUtc: {CollectedAtUtc}",
                _retryQueue.Count,
                rescheduledDelivery.AttemptCount,
                rescheduledDelivery.NextRetryAtUtc,
                rescheduledDelivery.LastFailureReason,
                rescheduledDelivery.Snapshot.Hostname,
                rescheduledDelivery.Snapshot.CollectedAtUtc);
            break;
        }

        if (deliveredRetryCount > 0)
        {
            _logger.LogInformation(
                "Успешно отправлено снимков из очереди: {DeliveredRetryCount}",
                deliveredRetryCount);
        }
    }

    private PendingSnapshotDelivery CreateInitialRetry(SystemSnapshot snapshot, SnapshotDeliveryResult deliveryResult)
    {
        var utcNow = DateTimeOffset.UtcNow;

        return new PendingSnapshotDelivery(
            snapshot,
            utcNow,
            AttemptCount: 1,
            NextRetryAtUtc: utcNow.Add(GetRetryDelay(1)),
            LastFailureReason: deliveryResult.Description);
    }

    private PendingSnapshotDelivery? ScheduleNextRetry(
        PendingSnapshotDelivery delivery,
        SnapshotDeliveryResult deliveryResult)
    {
        var nextAttemptCount = delivery.AttemptCount + 1;
        if (nextAttemptCount >= _settings.RetryMaxAttempts)
        {
            return null;
        }

        var utcNow = DateTimeOffset.UtcNow;

        return delivery with
        {
            AttemptCount = nextAttemptCount,
            NextRetryAtUtc = utcNow.Add(GetRetryDelay(nextAttemptCount)),
            LastFailureReason = deliveryResult.Description
        };
    }

    private TimeSpan GetRetryDelay(int attemptCount)
    {
        var baseDelaySeconds = _settings.RetryInitialDelaySeconds;
        var exponentialDelaySeconds = baseDelaySeconds * Math.Pow(2, attemptCount - 1);
        var boundedDelaySeconds = Math.Min(exponentialDelaySeconds, _settings.RetryMaxDelaySeconds);

        return TimeSpan.FromSeconds(boundedDelaySeconds);
    }
}
