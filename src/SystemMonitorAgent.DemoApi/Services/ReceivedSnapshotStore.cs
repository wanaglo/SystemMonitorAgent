using SystemMonitorAgent.Core.Models;
using SystemMonitorAgent.DemoApi.Models;

namespace SystemMonitorAgent.DemoApi.Services;

public sealed class ReceivedSnapshotStore
{
    private const int MaxStoredSnapshots = 100;

    private readonly object _syncRoot = new();
    private readonly List<ReceivedSnapshotEnvelope> _items = [];

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

    public ReceivedSnapshotEnvelope Add(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var envelope = new ReceivedSnapshotEnvelope(snapshot, DateTimeOffset.UtcNow);

        lock (_syncRoot)
        {
            _items.Add(envelope);

            if (_items.Count > MaxStoredSnapshots)
            {
                _items.RemoveRange(0, _items.Count - MaxStoredSnapshots);
            }
        }

        return envelope;
    }

    public ReceivedSnapshotEnvelope? GetLatest()
    {
        lock (_syncRoot)
        {
            return _items.Count == 0 ? null : _items[^1];
        }
    }

    public IReadOnlyList<ReceivedSnapshotEnvelope> GetRecent(int take)
    {
        var boundedTake = Math.Clamp(take, 1, MaxStoredSnapshots);

        lock (_syncRoot)
        {
            return _items
                .TakeLast(boundedTake)
                .Reverse()
                .ToList();
        }
    }
}
