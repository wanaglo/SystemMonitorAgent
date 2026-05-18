using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SystemMonitorAgent.Application.Abstractions;
using SystemMonitorAgent.Core.Configuration;
using MonitorWorker = SystemMonitorAgent.Worker.Worker;

namespace SystemMonitorAgent.UnitTests;

public class WorkerTests
{
    [Fact]
    public async Task StartAsync_ContinuesScheduling_AfterCycleException()
    {
        var secondCycleReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;

        var monitoringCycle = new DelegateMonitoringCycle(_ =>
        {
            var currentInvocation = Interlocked.Increment(ref invocationCount);
            if (currentInvocation == 1)
            {
                throw new InvalidOperationException("boom");
            }

            secondCycleReached.TrySetResult();
            return Task.CompletedTask;
        });

        var worker = CreateWorker(monitoringCycle, intervalSeconds: 1);

        await worker.StartAsync(CancellationToken.None);
        await secondCycleReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.True(Volatile.Read(ref invocationCount) >= 2);
    }

    [Fact]
    public async Task StopAsync_CancelsActiveCycle()
    {
        var cycleStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var monitoringCycle = new DelegateMonitoringCycle(async cancellationToken =>
        {
            cycleStarted.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancellationObserved.TrySetResult();
                throw;
            }
        });

        var worker = CreateWorker(monitoringCycle, intervalSeconds: 30);

        await worker.StartAsync(CancellationToken.None);
        await cycleStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StopAsync(stopCts.Token);

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static MonitorWorker CreateWorker(IMonitoringCycle monitoringCycle, int intervalSeconds)
    {
        return new MonitorWorker(
            monitoringCycle,
            Options.Create(new AgentSettings
            {
                IntervalSeconds = intervalSeconds,
                ApiUrl = "https://metrics.local/api/metrics"
            }),
            NullLogger<MonitorWorker>.Instance);
    }

    private sealed class DelegateMonitoringCycle : IMonitoringCycle
    {
        private readonly Func<CancellationToken, Task> _callback;

        public DelegateMonitoringCycle(Func<CancellationToken, Task> callback)
        {
            _callback = callback;
        }

        public Task RunOnceAsync(CancellationToken cancellationToken = default)
        {
            return _callback(cancellationToken);
        }
    }
}
