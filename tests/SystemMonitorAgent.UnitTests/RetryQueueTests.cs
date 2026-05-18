using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SystemMonitorAgent.Application.Delivery;
using SystemMonitorAgent.Core.Configuration;
using SystemMonitorAgent.Core.Models;
using SystemMonitorAgent.Infrastructure.Services;

namespace SystemMonitorAgent.UnitTests;

public class RetryQueueTests
{
    [Fact]
    public void Enqueue_EvictsOldestItem_WhenQueueCapacityIsReached()
    {
        var queue = CreateQueue(maxSize: 2);
        var oldest = CreateDelivery("oldest", DateTimeOffset.UtcNow.AddMinutes(-3), DateTimeOffset.UtcNow.AddSeconds(-10));
        var middle = CreateDelivery("middle", DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddSeconds(-10));
        var newest = CreateDelivery("newest", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddSeconds(-10));

        queue.Enqueue(oldest);
        queue.Enqueue(middle);
        queue.Enqueue(newest);

        var dequeuedHosts = new List<string>();
        while (queue.TryDequeueDue(DateTimeOffset.UtcNow, out var delivery))
        {
            dequeuedHosts.Add(delivery!.Snapshot.Hostname);
        }

        Assert.Equal(2, dequeuedHosts.Count);
        Assert.DoesNotContain("oldest", dequeuedHosts);
        Assert.Contains("middle", dequeuedHosts);
        Assert.Contains("newest", dequeuedHosts);
    }

    [Fact]
    public void TryDequeueDue_ReturnsItemsInRetryOrder_AndSkipsFutureEntries()
    {
        var queue = CreateQueue(maxSize: 10);
        var now = DateTimeOffset.UtcNow;
        var earliestDue = CreateDelivery("earliest", now.AddMinutes(-4), now.AddSeconds(-15));
        var laterDue = CreateDelivery("later", now.AddMinutes(-3), now.AddSeconds(-5));
        var future = CreateDelivery("future", now.AddMinutes(-2), now.AddMinutes(5));

        queue.Enqueue(laterDue);
        queue.Enqueue(future);
        queue.Enqueue(earliestDue);

        Assert.True(queue.TryDequeueDue(now, out var first));
        Assert.Equal("earliest", first!.Snapshot.Hostname);

        Assert.True(queue.TryDequeueDue(now, out var second));
        Assert.Equal("later", second!.Snapshot.Hostname);

        Assert.False(queue.TryDequeueDue(now, out var third));
        Assert.Null(third);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void TryDequeueDue_ReturnsFalse_WhenQueueIsEmpty()
    {
        var queue = CreateQueue(maxSize: 10);

        Assert.False(queue.TryDequeueDue(DateTimeOffset.UtcNow, out var delivery));
        Assert.Null(delivery);
    }

    private static RetryQueue CreateQueue(int maxSize)
    {
        return new RetryQueue(
            Options.Create(new AgentSettings { RetryQueueMaxSize = maxSize }),
            NullLogger<RetryQueue>.Instance);
    }

    private static PendingSnapshotDelivery CreateDelivery(
        string hostname,
        DateTimeOffset createdAtUtc,
        DateTimeOffset nextRetryAtUtc)
    {
        return new PendingSnapshotDelivery(
            new SystemSnapshot
            {
                Hostname = hostname,
                CollectedAtUtc = createdAtUtc.UtcDateTime
            },
            createdAtUtc,
            AttemptCount: 1,
            NextRetryAtUtc: nextRetryAtUtc,
            LastFailureReason: "HTTP 503");
    }
}
