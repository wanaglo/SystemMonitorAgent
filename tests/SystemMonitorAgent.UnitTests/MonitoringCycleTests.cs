using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SystemMonitorAgent.Application.Abstractions;
using SystemMonitorAgent.Application.Delivery;
using SystemMonitorAgent.Application.Services;
using SystemMonitorAgent.Core.Configuration;
using SystemMonitorAgent.Core.Models;
using System.Net;

namespace SystemMonitorAgent.UnitTests;

public class MonitoringCycleTests
{
    [Fact]
    public async Task RunOnceAsync_QueuesCurrentSnapshotWithRetryMetadata_WhenDeliveryFailsTransiently()
    {
        var snapshot = new SystemSnapshot
        {
            Hostname = "agent-host"
        };

        var collector = new Mock<ISystemInfoCollector>();
        collector
            .Setup(service => service.CollectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var sender = new Mock<IApiSender>();
        sender
            .Setup(service => service.SendAsync(snapshot, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SnapshotDeliveryResult.RetryableFailure("HTTP 503", HttpStatusCode.ServiceUnavailable));

        var retryQueue = new FakeRetryQueue();
        var settings = CreateSettings(intervalSeconds: 30, retryInitialDelaySeconds: 15);

        var beforeCall = DateTimeOffset.UtcNow;
        var sut = new MonitoringCycle(
            collector.Object,
            sender.Object,
            retryQueue,
            Options.Create(settings),
            NullLogger<MonitoringCycle>.Instance);

        await sut.RunOnceAsync();
        var afterCall = DateTimeOffset.UtcNow;

        var queuedDelivery = Assert.Single(retryQueue.EnqueuedItems);
        Assert.Equal(snapshot, queuedDelivery.Snapshot);
        Assert.Equal(1, queuedDelivery.AttemptCount);
        Assert.Equal("HTTP 503", queuedDelivery.LastFailureReason);
        Assert.InRange(
            queuedDelivery.NextRetryAtUtc,
            beforeCall.AddSeconds(15),
            afterCall.AddSeconds(15));
    }

    [Fact]
    public async Task RunOnceAsync_DoesNotQueueCurrentSnapshot_WhenDeliveryFailsPermanently()
    {
        var snapshot = new SystemSnapshot
        {
            Hostname = "agent-host"
        };

        var collector = new Mock<ISystemInfoCollector>();
        collector
            .Setup(service => service.CollectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var sender = new Mock<IApiSender>();
        sender
            .Setup(service => service.SendAsync(snapshot, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SnapshotDeliveryResult.PermanentFailure("HTTP 400", HttpStatusCode.BadRequest));

        var retryQueue = new FakeRetryQueue();

        var sut = new MonitoringCycle(
            collector.Object,
            sender.Object,
            retryQueue,
            Options.Create(new AgentSettings { IntervalSeconds = 30 }),
            NullLogger<MonitoringCycle>.Instance);

        await sut.RunOnceAsync();

        Assert.Empty(retryQueue.EnqueuedItems);
    }

    [Fact]
    public async Task RunOnceAsync_RequeuesDueRetryAndStillCollectsCurrentSnapshot_WhenRetryDeliveryFailsTransiently()
    {
        var retrySnapshot = new SystemSnapshot
        {
            Hostname = "retry-host",
            CollectedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        var currentSnapshot = new SystemSnapshot
        {
            Hostname = "current-host",
            CollectedAtUtc = DateTime.UtcNow
        };

        var queuedDelivery = new PendingSnapshotDelivery(
            retrySnapshot,
            DateTimeOffset.UtcNow.AddMinutes(-2),
            AttemptCount: 1,
            NextRetryAtUtc: DateTimeOffset.UtcNow.AddSeconds(-5),
            LastFailureReason: "HTTP timeout");

        var collector = new Mock<ISystemInfoCollector>();
        collector
            .Setup(service => service.CollectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentSnapshot);

        var sender = new Mock<IApiSender>();
        sender
            .SetupSequence(service => service.SendAsync(It.IsAny<SystemSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SnapshotDeliveryResult.RetryableFailure("HTTP 503", HttpStatusCode.ServiceUnavailable))
            .ReturnsAsync(SnapshotDeliveryResult.Success());

        var retryQueue = new FakeRetryQueue();
        retryQueue.SeedDue(queuedDelivery);
        var settings = CreateSettings(intervalSeconds: 30, retryInitialDelaySeconds: 15);

        var beforeCall = DateTimeOffset.UtcNow;
        var sut = new MonitoringCycle(
            collector.Object,
            sender.Object,
            retryQueue,
            Options.Create(settings),
            NullLogger<MonitoringCycle>.Instance);

        await sut.RunOnceAsync();
        var afterCall = DateTimeOffset.UtcNow;

        var rescheduledDelivery = Assert.Single(retryQueue.EnqueuedItems);
        Assert.Equal(2, rescheduledDelivery.AttemptCount);
        Assert.Equal(retrySnapshot, rescheduledDelivery.Snapshot);
        Assert.Equal(queuedDelivery.CreatedAtUtc, rescheduledDelivery.CreatedAtUtc);
        Assert.Equal("HTTP 503", rescheduledDelivery.LastFailureReason);
        Assert.InRange(
            rescheduledDelivery.NextRetryAtUtc,
            beforeCall.AddSeconds(30),
            afterCall.AddSeconds(30));
        collector.Verify(service => service.CollectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_ContinuesWithCurrentSnapshot_WhenQueuedRetryFailsPermanently()
    {
        var retrySnapshot = new SystemSnapshot
        {
            Hostname = "retry-host",
            CollectedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        var currentSnapshot = new SystemSnapshot
        {
            Hostname = "current-host",
            CollectedAtUtc = DateTime.UtcNow
        };

        var queuedDelivery = new PendingSnapshotDelivery(
            retrySnapshot,
            DateTimeOffset.UtcNow.AddMinutes(-2),
            AttemptCount: 2,
            NextRetryAtUtc: DateTimeOffset.UtcNow.AddSeconds(-5),
            LastFailureReason: "HTTP 503");

        var collector = new Mock<ISystemInfoCollector>();
        collector
            .Setup(service => service.CollectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentSnapshot);

        var sender = new Mock<IApiSender>();
        sender
            .SetupSequence(service => service.SendAsync(It.IsAny<SystemSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SnapshotDeliveryResult.PermanentFailure("HTTP 400", HttpStatusCode.BadRequest))
            .ReturnsAsync(SnapshotDeliveryResult.Success());

        var retryQueue = new FakeRetryQueue();
        retryQueue.SeedDue(queuedDelivery);

        var sut = new MonitoringCycle(
            collector.Object,
            sender.Object,
            retryQueue,
            Options.Create(new AgentSettings { IntervalSeconds = 30 }),
            NullLogger<MonitoringCycle>.Instance);

        await sut.RunOnceAsync();

        Assert.Empty(retryQueue.EnqueuedItems);
        collector.Verify(service => service.CollectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_DropsQueuedRetry_WhenRetryLimitIsExceeded_AndContinuesCurrentCycle()
    {
        var retrySnapshot = new SystemSnapshot
        {
            Hostname = "retry-host",
            CollectedAtUtc = DateTime.UtcNow.AddMinutes(-10)
        };
        var currentSnapshot = new SystemSnapshot
        {
            Hostname = "current-host",
            CollectedAtUtc = DateTime.UtcNow
        };

        var queuedDelivery = new PendingSnapshotDelivery(
            retrySnapshot,
            DateTimeOffset.UtcNow.AddMinutes(-15),
            AttemptCount: 7,
            NextRetryAtUtc: DateTimeOffset.UtcNow.AddSeconds(-5),
            LastFailureReason: "HTTP 503");

        var collector = new Mock<ISystemInfoCollector>();
        collector
            .Setup(service => service.CollectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentSnapshot);

        var sender = new Mock<IApiSender>();
        sender
            .SetupSequence(service => service.SendAsync(It.IsAny<SystemSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SnapshotDeliveryResult.RetryableFailure("HTTP 503", HttpStatusCode.ServiceUnavailable))
            .ReturnsAsync(SnapshotDeliveryResult.Success());

        var retryQueue = new FakeRetryQueue();
        retryQueue.SeedDue(queuedDelivery);

        var sut = new MonitoringCycle(
            collector.Object,
            sender.Object,
            retryQueue,
            Options.Create(new AgentSettings { IntervalSeconds = 30 }),
            NullLogger<MonitoringCycle>.Instance);

        await sut.RunOnceAsync();

        Assert.Empty(retryQueue.EnqueuedItems);
        collector.Verify(service => service.CollectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_CapsRetryDelay_WhenConfiguredMaximumIsReached()
    {
        var retrySnapshot = new SystemSnapshot
        {
            Hostname = "retry-host",
            CollectedAtUtc = DateTime.UtcNow.AddMinutes(-10)
        };
        var currentSnapshot = new SystemSnapshot
        {
            Hostname = "current-host",
            CollectedAtUtc = DateTime.UtcNow
        };

        var queuedDelivery = new PendingSnapshotDelivery(
            retrySnapshot,
            DateTimeOffset.UtcNow.AddMinutes(-12),
            AttemptCount: 4,
            NextRetryAtUtc: DateTimeOffset.UtcNow.AddSeconds(-5),
            LastFailureReason: "HTTP 503");

        var collector = new Mock<ISystemInfoCollector>();
        collector
            .Setup(service => service.CollectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentSnapshot);

        var sender = new Mock<IApiSender>();
        sender
            .SetupSequence(service => service.SendAsync(It.IsAny<SystemSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SnapshotDeliveryResult.RetryableFailure("HTTP 503", HttpStatusCode.ServiceUnavailable))
            .ReturnsAsync(SnapshotDeliveryResult.Success());

        var retryQueue = new FakeRetryQueue();
        retryQueue.SeedDue(queuedDelivery);
        var settings = CreateSettings(intervalSeconds: 30, retryInitialDelaySeconds: 15, retryMaxDelaySeconds: 120);

        var beforeCall = DateTimeOffset.UtcNow;
        var sut = new MonitoringCycle(
            collector.Object,
            sender.Object,
            retryQueue,
            Options.Create(settings),
            NullLogger<MonitoringCycle>.Instance);

        await sut.RunOnceAsync();
        var afterCall = DateTimeOffset.UtcNow;

        var rescheduledDelivery = Assert.Single(retryQueue.EnqueuedItems);
        Assert.Equal(5, rescheduledDelivery.AttemptCount);
        Assert.InRange(
            rescheduledDelivery.NextRetryAtUtc,
            beforeCall.AddSeconds(120),
            afterCall.AddSeconds(120));
        collector.Verify(service => service.CollectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AgentSettings CreateSettings(
        int intervalSeconds,
        int retryInitialDelaySeconds,
        int retryMaxAttempts = 8,
        int retryMaxDelaySeconds = 600)
    {
        return new AgentSettings
        {
            IntervalSeconds = intervalSeconds,
            RetryInitialDelaySeconds = retryInitialDelaySeconds,
            RetryMaxAttempts = retryMaxAttempts,
            RetryMaxDelaySeconds = retryMaxDelaySeconds
        };
    }

    private sealed class FakeRetryQueue : IRetryQueue
    {
        private readonly Queue<PendingSnapshotDelivery> _dueItems = new();

        public List<PendingSnapshotDelivery> EnqueuedItems { get; } = [];

        public int Count => _dueItems.Count + EnqueuedItems.Count;

        public void SeedDue(params PendingSnapshotDelivery[] deliveries)
        {
            foreach (var delivery in deliveries)
            {
                _dueItems.Enqueue(delivery);
            }
        }

        public void Enqueue(PendingSnapshotDelivery delivery)
        {
            EnqueuedItems.Add(delivery);
        }

        public bool TryDequeueDue(DateTimeOffset utcNow, out PendingSnapshotDelivery? delivery)
        {
            if (_dueItems.Count == 0)
            {
                delivery = null;
                return false;
            }

            var nextDelivery = _dueItems.Peek();
            if (nextDelivery.NextRetryAtUtc > utcNow)
            {
                delivery = null;
                return false;
            }

            delivery = _dueItems.Dequeue();
            return true;
        }
    }
}
