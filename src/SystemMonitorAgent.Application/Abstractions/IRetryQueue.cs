using SystemMonitorAgent.Application.Delivery;

namespace SystemMonitorAgent.Application.Abstractions;

public interface IRetryQueue
{
    void Enqueue(PendingSnapshotDelivery delivery);
    bool TryDequeueDue(DateTimeOffset utcNow, out PendingSnapshotDelivery? delivery);
    int Count { get; }
}
