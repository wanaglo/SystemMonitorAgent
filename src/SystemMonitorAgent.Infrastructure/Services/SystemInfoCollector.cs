using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemMonitorAgent.Application.Abstractions;
using SystemMonitorAgent.Core.Configuration;
using SystemMonitorAgent.Core.Models;

namespace SystemMonitorAgent.Infrastructure.Services;

/// <summary>
/// Собирает снимок состояния локальной машины через Windows API и WMI.
/// </summary>
public sealed class SystemInfoCollector : ISystemInfoCollector
{
    private readonly AgentSettings _settings;
    private readonly ILogger<SystemInfoCollector> _logger;

    public SystemInfoCollector(IOptions<AgentSettings> settings, ILogger<SystemInfoCollector> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SystemSnapshot> CollectAsync(CancellationToken cancellationToken = default)
    {
        var operatingSystemInfo = GetOperatingSystemInfo();
        var runningProcesses = GetRunningProcesses();
        var runningProcessLookup = new HashSet<string>(runningProcesses, StringComparer.OrdinalIgnoreCase);
        var totalMemoryBytes = operatingSystemInfo.TotalMemoryBytes;
        var freeMemoryBytes = operatingSystemInfo.FreeMemoryBytes;
        var usedMemoryBytes = Math.Max(0, totalMemoryBytes - freeMemoryBytes);

        return new SystemSnapshot
        {
            CollectedAtUtc = DateTime.UtcNow,
            Hostname = Environment.MachineName,
            IpAddresses = GetIpAddresses(),
            WindowsVersion = operatingSystemInfo.WindowsVersion,
            UptimeSeconds = TimeSpan.FromMilliseconds(Environment.TickCount64).TotalSeconds,
            CpuUsagePercent = await GetCpuUsagePercentAsync(cancellationToken),
            Ram = new RamUsageInfo
            {
                TotalBytes = totalMemoryBytes,
                UsedBytes = usedMemoryBytes,
                UsagePercent = totalMemoryBytes == 0
                    ? 0
                    : Math.Round(usedMemoryBytes * 100d / totalMemoryBytes, 2)
            },
            Disks = GetDiskUsage(),
            RunningProcesses = runningProcesses,
            WatchedProcesses = _settings.WatchedProcesses
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    processName => processName,
                    processName => runningProcessLookup.Contains(processName),
                    StringComparer.OrdinalIgnoreCase)
        };
    }

    private async Task<double> GetCpuUsagePercentAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ = cpuCounter.NextValue();

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            return Math.Round(cpuCounter.NextValue(), 2);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Не удалось прочитать счётчик загрузки CPU");
            return 0;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Доступ к счётчику загрузки CPU запрещён");
            return 0;
        }
        catch (Win32Exception ex)
        {
            _logger.LogWarning(ex, "Счётчики производительности Windows недоступны");
            return 0;
        }
    }

    private OperatingSystemInfo GetOperatingSystemInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Caption, Version, TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            using var results = searcher.Get();
            var osInfo = results.Cast<ManagementObject>().FirstOrDefault();

            if (osInfo is null)
            {
                return new OperatingSystemInfo(RuntimeInformation.OSDescription, 0, 0);
            }

            var caption = osInfo["Caption"]?.ToString();
            var version = osInfo["Version"]?.ToString();
            var totalMemoryBytes = Convert.ToInt64(osInfo["TotalVisibleMemorySize"] ?? 0L) * 1024;
            var freeMemoryBytes = Convert.ToInt64(osInfo["FreePhysicalMemory"] ?? 0L) * 1024;

            var windowsVersion = string.IsNullOrWhiteSpace(caption)
                ? RuntimeInformation.OSDescription
                : $"{caption} ({version})";

            return new OperatingSystemInfo(windowsVersion, totalMemoryBytes, freeMemoryBytes);
        }
        catch (ManagementException ex)
        {
            _logger.LogWarning(ex, "Не удалось получить сведения об операционной системе через WMI");
            return new OperatingSystemInfo(RuntimeInformation.OSDescription, 0, 0);
        }
        catch (COMException ex)
        {
            _logger.LogWarning(ex, "WMI недоступен при чтении сведений об операционной системе");
            return new OperatingSystemInfo(RuntimeInformation.OSDescription, 0, 0);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Сведения об операционной системе недоступны");
            return new OperatingSystemInfo(RuntimeInformation.OSDescription, 0, 0);
        }
    }

    private static List<DiskUsageInfo> GetDiskUsage()
    {
        return DriveInfo.GetDrives()
            .Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed)
            .Select(drive => new DiskUsageInfo
            {
                Drive = drive.Name,
                TotalBytes = drive.TotalSize,
                FreeBytes = drive.TotalFreeSpace,
                FreePercent = drive.TotalSize == 0
                    ? 0
                    : Math.Round(drive.TotalFreeSpace * 100d / drive.TotalSize, 2)
            })
            .OrderBy(disk => disk.Drive, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> GetIpAddresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface =>
                networkInterface.OperationalStatus == OperationalStatus.Up &&
                networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
            .Select(addressInformation => addressInformation.Address)
            .Where(address =>
                address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(address))
            .Select(address => address.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> GetRunningProcesses()
    {
        var processNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                processNames.Add(process.ProcessName);
            }
        }

        return processNames
            .OrderBy(processName => processName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed record OperatingSystemInfo(string WindowsVersion, long TotalMemoryBytes, long FreeMemoryBytes);
}
