namespace SystemMonitorAgent.Core.Configuration;

/// <summary>
/// Строго типизированная конфигурация агента мониторинга.
/// Заполняется из секции "AgentSettings" в appsettings.json.
/// </summary>
public sealed class AgentSettings
{
    public const string SectionName = "AgentSettings";

    public string ApiUrl { get; set; } = "http://localhost:5000/api/metrics";

    public int IntervalSeconds { get; set; } = 30;

    public int HttpTimeoutSeconds { get; set; } = 10;

    public string LogFilePath { get; set; } = "logs\\agent-.log";

    public List<string> WatchedProcesses { get; set; } = new() { "notepad", "explorer" };

    public int RetryQueueMaxSize { get; set; } = 100;

    public int RetryMaxAttempts { get; set; } = 8;

    public int RetryInitialDelaySeconds { get; set; } = 15;

    public int RetryMaxDelaySeconds { get; set; } = 600;
}
