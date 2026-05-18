using SystemMonitorAgent.Core.Models;
using SystemMonitorAgent.Application.Delivery;

namespace SystemMonitorAgent.Application.Abstractions;

public interface IApiSender
{
    Task<SnapshotDeliveryResult> SendAsync(SystemSnapshot snapshot, CancellationToken cancellationToken = default);
}
