using Microsoft.Extensions.Options;
using SystemMonitorAgent.Core.Configuration;

namespace SystemMonitorAgent.Application.Configuration;

public sealed class AgentSettingsValidator : IValidateOptions<AgentSettings>
{
    public ValidateOptionsResult Validate(string? name, AgentSettings options)
    {
        var errors = AgentSettingsValidation.GetErrors(options);
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

public static class AgentSettingsValidation
{
    public static void EnsureValid(AgentSettings? settings)
    {
        var errors = GetErrors(settings);
        if (errors.Count == 0)
        {
            return;
        }

        throw new OptionsValidationException(
            AgentSettings.SectionName,
            typeof(AgentSettings),
            errors);
    }

    public static IReadOnlyList<string> GetErrors(AgentSettings? settings)
    {
        var errors = new List<string>();

        if (settings is null)
        {
            errors.Add("Секция AgentSettings отсутствует или заполнена некорректно.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(settings.ApiUrl))
        {
            errors.Add("AgentSettings:ApiUrl обязателен.");
        }
        else if (!Uri.TryCreate(settings.ApiUrl, UriKind.Absolute, out var apiUri) ||
                 (apiUri.Scheme != Uri.UriSchemeHttp && apiUri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("AgentSettings:ApiUrl должен быть абсолютным HTTP- или HTTPS-адресом.");
        }

        if (settings.IntervalSeconds <= 0)
        {
            errors.Add("AgentSettings:IntervalSeconds должен быть больше 0.");
        }

        if (settings.HttpTimeoutSeconds <= 0)
        {
            errors.Add("AgentSettings:HttpTimeoutSeconds должен быть больше 0.");
        }

        if (string.IsNullOrWhiteSpace(settings.LogFilePath))
        {
            errors.Add("AgentSettings:LogFilePath обязателен.");
        }

        if (settings.RetryQueueMaxSize <= 0)
        {
            errors.Add("AgentSettings:RetryQueueMaxSize должен быть больше 0.");
        }

        if (settings.RetryMaxAttempts <= 0)
        {
            errors.Add("AgentSettings:RetryMaxAttempts должен быть больше 0.");
        }

        if (settings.RetryInitialDelaySeconds <= 0)
        {
            errors.Add("AgentSettings:RetryInitialDelaySeconds должен быть больше 0.");
        }

        if (settings.RetryMaxDelaySeconds <= 0)
        {
            errors.Add("AgentSettings:RetryMaxDelaySeconds должен быть больше 0.");
        }
        else if (settings.RetryMaxDelaySeconds < settings.RetryInitialDelaySeconds)
        {
            errors.Add("AgentSettings:RetryMaxDelaySeconds должен быть больше или равен RetryInitialDelaySeconds.");
        }

        if (settings.WatchedProcesses is null)
        {
            errors.Add("Коллекция AgentSettings:WatchedProcesses обязательна.");
        }
        else if (settings.WatchedProcesses.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("AgentSettings:WatchedProcesses не должен содержать пустые значения.");
        }

        return errors;
    }
}
