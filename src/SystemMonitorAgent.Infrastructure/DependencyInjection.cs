using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SystemMonitorAgent.Application.Abstractions;
using SystemMonitorAgent.Core.Configuration;
using SystemMonitorAgent.Infrastructure.Services;

namespace SystemMonitorAgent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddHttpClient("MetricsApi")
            .ConfigureHttpClient((serviceProvider, client) =>
        {
            var agentSettings = serviceProvider.GetRequiredService<IOptions<AgentSettings>>().Value;
            client.Timeout = TimeSpan.FromSeconds(agentSettings.HttpTimeoutSeconds);
        });

        services.AddSingleton<ISystemInfoCollector, SystemInfoCollector>();
        services.AddSingleton<IApiSender, ApiSender>();
        services.AddSingleton<IRetryQueue, RetryQueue>();

        return services;
    }
}
