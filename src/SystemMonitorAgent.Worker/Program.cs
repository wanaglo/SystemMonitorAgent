using Serilog;
using SystemMonitorAgent.Application;
using SystemMonitorAgent.Application.Configuration;
using SystemMonitorAgent.Core.Configuration;
using SystemMonitorAgent.Infrastructure;

namespace SystemMonitorAgent.Worker;

public static class Program
{
    public static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            var builder = Host.CreateApplicationBuilder(args);
            ConfigureServices(builder);

            using var host = builder.Build();
            host.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Приложение завершилось из-за необработанной ошибки");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void ConfigureServices(HostApplicationBuilder builder)
    {
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "SystemMonitorAgent";
        });

        builder.Services.AddApplication();
        builder.Services.AddOptions<AgentSettings>()
            .Bind(builder.Configuration.GetSection(AgentSettings.SectionName))
            .ValidateOnStart();
        builder.Services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(30);
        });

        var agentSettings = builder.Configuration
            .GetSection(AgentSettings.SectionName)
            .Get<AgentSettings>();

        AgentSettingsValidation.EnsureValid(agentSettings);
        var validatedSettings = agentSettings!;
        var resolvedLogFilePath = ResolveLogFilePath(validatedSettings.LogFilePath);

        builder.Services.AddSerilog(loggerConfiguration =>
        {
            EnsureLogDirectoryExists(resolvedLogFilePath);

            loggerConfiguration
                .ReadFrom.Configuration(builder.Configuration)
                .WriteTo.Console()
                .WriteTo.File(
                    resolvedLogFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

            if (OperatingSystem.IsWindows())
            {
                loggerConfiguration.WriteTo.EventLog("SystemMonitorAgent", manageEventSource: false);
            }
        });

        builder.Services.AddInfrastructure();
        builder.Services.AddHostedService<Worker>();
    }

    private static string ResolveLogFilePath(string configuredPath)
    {
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));
    }

    private static void EnsureLogDirectoryExists(string logFilePath)
    {
        var logDirectoryPath = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrWhiteSpace(logDirectoryPath))
        {
            Directory.CreateDirectory(logDirectoryPath);
        }
    }
}
