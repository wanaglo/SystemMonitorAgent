using SystemMonitorAgent.Core.Models;

namespace SystemMonitorAgent.Application.Delivery;

public sealed record PendingSnapshotDelivery(
    SystemSnapshot Snapshot,
    DateTimeOffset CreatedAtUtc,
    int AttemptCount,
    DateTimeOffset NextRetryAtUtc,
    string LastFailureReason);
