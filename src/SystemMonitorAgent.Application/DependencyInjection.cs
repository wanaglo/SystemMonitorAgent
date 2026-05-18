using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SystemMonitorAgent.Application.Abstractions;
using SystemMonitorAgent.Application.Configuration;
using SystemMonitorAgent.Application.Services;
using SystemMonitorAgent.Core.Configuration;

namespace SystemMonitorAgent.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<AgentSettings>, AgentSettingsValidator>();
        services.AddSingleton<IMonitoringCycle, MonitoringCycle>();
        return services;
    }
}
