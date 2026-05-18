using Microsoft.Extensions.Options;
using SystemMonitorAgent.Application.Configuration;
using SystemMonitorAgent.Core.Configuration;

namespace SystemMonitorAgent.UnitTests;

public class AgentSettingsValidationTests
{
    [Fact]
    public void EnsureValid_DoesNotThrow_ForValidSettings()
    {
        var settings = CreateValidSettings();

        AgentSettingsValidation.EnsureValid(settings);
    }

    [Fact]
    public void EnsureValid_Throws_ForInvalidApiUrl()
    {
        var settings = CreateValidSettings();
        settings.ApiUrl = "metrics-api";

        var exception = Assert.Throws<OptionsValidationException>(
            () => AgentSettingsValidation.EnsureValid(settings));

        Assert.Contains(
            "AgentSettings:ApiUrl должен быть абсолютным HTTP- или HTTPS-адресом.",
            exception.Failures);
    }

    [Fact]
    public void EnsureValid_Throws_ForEmptyProcessName()
    {
        var settings = CreateValidSettings();
        settings.WatchedProcesses = ["notepad", " "];

        var exception = Assert.Throws<OptionsValidationException>(
            () => AgentSettingsValidation.EnsureValid(settings));

        Assert.Contains(
            "AgentSettings:WatchedProcesses не должен содержать пустые значения.",
            exception.Failures);
    }

    [Fact]
    public void EnsureValid_Throws_ForInvalidRetryAttemptSettings()
    {
        var settings = CreateValidSettings();
        settings.RetryMaxAttempts = 0;

        var exception = Assert.Throws<OptionsValidationException>(
            () => AgentSettingsValidation.EnsureValid(settings));

        Assert.Contains(
            "AgentSettings:RetryMaxAttempts должен быть больше 0.",
            exception.Failures);
    }

    [Fact]
    public void EnsureValid_Throws_WhenRetryDelayBoundsAreInverted()
    {
        var settings = CreateValidSettings();
        settings.RetryInitialDelaySeconds = 60;
        settings.RetryMaxDelaySeconds = 30;

        var exception = Assert.Throws<OptionsValidationException>(
            () => AgentSettingsValidation.EnsureValid(settings));

        Assert.Contains(
            "AgentSettings:RetryMaxDelaySeconds должен быть больше или равен RetryInitialDelaySeconds.",
            exception.Failures);
    }

    private static AgentSettings CreateValidSettings()
    {
        return new AgentSettings
        {
            ApiUrl = "https://localhost:5001/api/metrics",
            IntervalSeconds = 30,
            HttpTimeoutSeconds = 10,
            LogFilePath = "logs\\agent-.log",
            RetryQueueMaxSize = 100,
            RetryMaxAttempts = 8,
            RetryInitialDelaySeconds = 15,
            RetryMaxDelaySeconds = 600,
            WatchedProcesses = ["notepad", "explorer"]
        };
    }
}
