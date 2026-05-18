using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemMonitorAgent.Application.Abstractions;
using SystemMonitorAgent.Application.Delivery;
using SystemMonitorAgent.Core.Configuration;
using SystemMonitorAgent.Core.Models;

namespace SystemMonitorAgent.Infrastructure.Services;

/// <summary>
/// Отправляет системный снимок в виде JSON POST-запросом на настроенный API-адрес.
/// </summary>
public sealed class ApiSender : IApiSender
{
    private readonly HttpClient _httpClient;
    private readonly AgentSettings _settings;
    private readonly ILogger<ApiSender> _logger;

    public ApiSender(
        IHttpClientFactory httpClientFactory,
        IOptions<AgentSettings> settings,
        ILogger<ApiSender> logger)
    {
        _httpClient = httpClientFactory.CreateClient("MetricsApi");
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SnapshotDeliveryResult> SendAsync(SystemSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                _settings.ApiUrl,
                snapshot,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return SnapshotDeliveryResult.Success();
            }

            var description = $"HTTP {(int)response.StatusCode} ({response.StatusCode})";
            if (IsRetryableStatusCode(response.StatusCode))
            {
                _logger.LogWarning(
                    "API метрик вернул код {StatusCode}, допускающий повторную отправку, для {ApiUrl}",
                    (int)response.StatusCode,
                    _settings.ApiUrl);

                return SnapshotDeliveryResult.RetryableFailure(description, response.StatusCode);
            }

            _logger.LogError(
                "API метрик вернул неретрайбл код {StatusCode} для {ApiUrl}",
                (int)response.StatusCode,
                _settings.ApiUrl);

            return SnapshotDeliveryResult.PermanentFailure(description, response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Не удалось обратиться к API метрик по адресу {ApiUrl}", _settings.ApiUrl);
            return SnapshotDeliveryResult.RetryableFailure($"Сетевая ошибка: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Истекло время ожидания при отправке снимка на {ApiUrl}", _settings.ApiUrl);
            return SnapshotDeliveryResult.RetryableFailure("HTTP-запрос превысил время ожидания");
        }
    }

    private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
    {
        var numericStatusCode = (int)statusCode;
        return statusCode == HttpStatusCode.RequestTimeout
            || numericStatusCode == 429
            || numericStatusCode >= 500;
    }
}
