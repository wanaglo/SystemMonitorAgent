using SystemMonitorAgent.Core.Models;

namespace SystemMonitorAgent.Application.Abstractions;

public interface ISystemInfoCollector
{
    Task<SystemSnapshot> CollectAsync(CancellationToken cancellationToken = default);
}
