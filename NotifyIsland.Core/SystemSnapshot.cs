namespace NotifyIsland;

/// <summary>One sample of the machine's live metrics. Immutable; produced by an ISystemMonitorSource.</summary>
public sealed record SystemSnapshot
{
    public double CpuPercent { get; init; }
    public long RamUsedBytes { get; init; }
    /// <summary>Physical RAM installed, or 0 when the sample could not read it. 0 means "hide the RAM slot".</summary>
    public long RamTotalBytes { get; init; }
    /// <summary>null on desktops or when the battery could not be read.</summary>
    public double? BatteryPercent { get; init; }
    public bool OnAcPower { get; init; }
    public long NetUpBytesPerSec { get; init; }
    public long NetDownBytesPerSec { get; init; }
    public DateTimeOffset CapturedAt { get; init; }

    public static SystemSnapshot Empty { get; } = new();

    public double RamPercent => RamTotalBytes > 0
        ? Math.Clamp(RamUsedBytes * 100.0 / RamTotalBytes, 0, 100)
        : 0;
}
