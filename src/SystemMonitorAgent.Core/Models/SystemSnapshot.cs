namespace SystemMonitorAgent.Core.Models;

/// <summary>
/// Представляет один снимок системных метрик, собранный в конкретный момент времени.
/// </summary>
public sealed class SystemSnapshot
{
    public DateTime CollectedAtUtc { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public List<string> IpAddresses { get; set; } = new();
    public string WindowsVersion { get; set; } = string.Empty;
    public double UptimeSeconds { get; set; }
    public double CpuUsagePercent { get; set; }
    public RamUsageInfo Ram { get; set; } = new();
    public List<DiskUsageInfo> Disks { get; set; } = new();
    public List<string> RunningProcesses { get; set; } = new();
    public Dictionary<string, bool> WatchedProcesses { get; set; } = new();
}

public sealed class RamUsageInfo
{
    public long TotalBytes { get; set; }
    public long UsedBytes { get; set; }
    public double UsagePercent { get; set; }
}

public sealed class DiskUsageInfo
{
    public string Drive { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public double FreePercent { get; set; }
}
