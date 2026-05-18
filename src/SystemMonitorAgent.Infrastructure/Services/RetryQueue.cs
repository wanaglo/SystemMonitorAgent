using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemMonitorAgent.Application.Abstractions;
using SystemMonitorAgent.Application.Delivery;
using SystemMonitorAgent.Core.Configuration;

namespace SystemMonitorAgent.Infrastructure.Services;

/// <summary>
/// Ограниченная in-memory очередь для повторной отправки снимков, которые не удалось доставить.
/// </summary>
public sealed class RetryQueue : IRetryQueue
{
    private readonly object _syncRoot = new();
    private readonly List<PendingSnapshotDelivery> _items = [];
    private readonly int _maxSize;
    private readonly ILogger<RetryQueue> _logger;

    public RetryQueue(IOptions<AgentSettings> settings, ILogger<RetryQueue> logger)
    {
        _maxSize = Math.Max(1, settings.Value.RetryQueueMaxSize);
        _logger = logger;
    }

    public int Count
    {
        get
        {
            lock (_syncRoot)
            {
                return _items.Count;
            }
        }
    }

    public void Enqueue(PendingSnapshotDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        lock (_syncRoot)
        {
            if (_items.Count >= _maxSize)
            {
                var droppedDelivery = _items
                    .OrderBy(item => item.CreatedAtUtc)
                    .First();
                _items.Remove(droppedDelivery);

                _logger.LogWarning(
                    "Очередь повторных отправок достигла лимита {QueueCapacity}. Самый старый снимок будет отброшен. CreatedAtUtc: {CreatedAtUtc}, AttemptCount: {AttemptCount}, Hostname: {Hostname}, CollectedAtUtc: {CollectedAtUtc}",
                    _maxSize,
                    droppedDelivery.CreatedAtUtc,
                    droppedDelivery.AttemptCount,
                    droppedDelivery.Snapshot.Hostname,
                    droppedDelivery.Snapshot.CollectedAtUtc);
            }

            _items.Add(delivery);
        }
    }

    public bool TryDequeueDue(DateTimeOffset utcNow, out PendingSnapshotDelivery? delivery)
    {
        lock (_syncRoot)
        {
            var nextIndex = -1;

            for (var index = 0; index < _items.Count; index++)
            {
                var item = _items[index];
                if (item.NextRetryAtUtc > utcNow)
                {
                    continue;
                }

                if (nextIndex < 0
                    || item.NextRetryAtUtc < _items[nextIndex].NextRetryAtUtc
                    || item.NextRetryAtUtc == _items[nextIndex].NextRetryAtUtc && item.CreatedAtUtc < _items[nextIndex].CreatedAtUtc)
                {
                    nextIndex = index;
                }
            }

            if (nextIndex < 0)
            {
                delivery = null;
                return false;
            }

            delivery = _items[nextIndex];
            _items.RemoveAt(nextIndex);
            return true;
        }
    }
}
