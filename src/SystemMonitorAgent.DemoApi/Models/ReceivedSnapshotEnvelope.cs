using SystemMonitorAgent.Core.Models;

namespace SystemMonitorAgent.DemoApi.Models;

public sealed record ReceivedSnapshotEnvelope(
    SystemSnapshot Snapshot,
    DateTimeOffset ReceivedAtUtc);
