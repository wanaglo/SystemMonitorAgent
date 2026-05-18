using System.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting.WindowsServices;
using SystemMonitorAgent.Application.Abstractions;
using SystemMonitorAgent.Core.Configuration;

namespace SystemMonitorAgent.Worker;

/// <summary>
/// Планирует циклы мониторинга с фиксированным интервалом и изолирует каждую итерацию выполнения.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly IMonitoringCycle _monitoringCycle;
    private readonly AgentSettings _settings;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IMonitoringCycle monitoringCycle,
        IOptions<AgentSettings> settings,
        ILogger<Worker> logger)
    {
        _monitoringCycle = monitoringCycle;
        _settings = settings.Value;
        _logger = logger;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Получен запрос на остановку System Monitor Agent");
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_settings.IntervalSeconds);
        var runMode = WindowsServiceHelpers.IsWindowsService() ? "WindowsService" : "Console";

        _logger.LogInformation(
            "System Monitor Agent запущен. RunMode: {RunMode}, ProcessId: {ProcessId}, Interval: {Interval}, API: {ApiUrl}",
            runMode,
            Environment.ProcessId,
            interval,
            _settings.ApiUrl);

        using var timer = new PeriodicTimer(interval);
        var cycleNumber = 0L;

        try
        {
            await ExecuteCycleSafelyAsync(++cycleNumber, interval, stoppingToken, isStartupCycle: true);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ExecuteCycleSafelyAsync(++cycleNumber, interval, stoppingToken, isStartupCycle: false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Остановка System Monitor Agent подтверждена");
        }
        finally
        {
            _logger.LogInformation("System Monitor Agent остановлен");
        }
    }

    private async Task ExecuteCycleSafelyAsync(
        long cycleNumber,
        TimeSpan interval,
        CancellationToken stoppingToken,
        bool isStartupCycle)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogDebug(
            "Запускается цикл мониторинга {CycleKind}. Номер цикла: {CycleNumber}",
            isStartupCycle ? "startup" : "scheduled",
            cycleNumber);

        try
        {
            await _monitoringCycle.RunOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Цикл мониторинга {CycleNumber} отменён во время остановки через {ElapsedMilliseconds} мс",
                cycleNumber,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Цикл мониторинга {CycleNumber} завершился ошибкой через {ElapsedMilliseconds} мс",
                cycleNumber,
                stopwatch.ElapsedMilliseconds);
            return;
        }

        if (stopwatch.Elapsed > interval)
        {
            _logger.LogWarning(
                "Цикл мониторинга {CycleNumber} завершился за {ElapsedMilliseconds} мс и превысил настроенный интервал {IntervalMilliseconds} мс",
                cycleNumber,
                stopwatch.ElapsedMilliseconds,
                interval.TotalMilliseconds);
            return;
        }

        _logger.LogDebug(
            "Цикл мониторинга {CycleNumber} завершился за {ElapsedMilliseconds} мс",
            cycleNumber,
            stopwatch.ElapsedMilliseconds);
    }
}
