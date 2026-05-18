namespace SystemMonitorAgent.Application.Abstractions;

public interface IMonitoringCycle
{
    Task RunOnceAsync(CancellationToken cancellationToken = default);
}
