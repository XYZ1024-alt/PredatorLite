namespace PredatorLite.Core.Models;

public sealed record DeviceIdentity(
    string Manufacturer,
    string Model,
    string BiosVersion,
    string WindowsVersion);

public sealed record DeviceSettingState(
    DeviceSettingId Id,
    bool IsSupported,
    bool IsWritable,
    bool? Enabled,
    string? Detail = null);

public sealed record DeviceCapabilities
{
    public required DeviceIdentity Device { get; init; }

    public bool IsValidatedModel { get; init; }

    public string CompatibilityMessage { get; init; } = string.Empty;

    public bool AcerServiceAvailable { get; init; }

    public bool AcerWmiAvailable { get; init; }

    public bool BatteryControlAvailable { get; init; }

    public bool? ChargeLimitEnabled { get; init; }

    public bool LightingAvailable { get; init; }

    public bool FanControlAvailable { get; init; }

    public bool GpuMuxAvailable { get; init; }

    public IReadOnlyList<int> RefreshRates { get; init; } = [];

    public IReadOnlyDictionary<DeviceSettingId, DeviceSettingState> DeviceSettings { get; init; } =
        new Dictionary<DeviceSettingId, DeviceSettingState>();

    public bool CanWriteHardware => IsValidatedModel && (AcerServiceAvailable || AcerWmiAvailable);
}

public sealed record HardwareSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public int? CpuTemperatureC { get; init; }

    public int? GpuTemperatureC { get; init; }

    public int? CpuFanRpm { get; init; }

    public int? GpuFanRpm { get; init; }

    public double? CpuLoadPercent { get; init; }

    public double? GpuLoadPercent { get; init; }

    public double? CpuPowerWatts { get; init; }

    public double? GpuPowerWatts { get; init; }

    public double? CpuClockMhz { get; init; }

    public double? GpuClockMhz { get; init; }

    public double? MemoryUsedGb { get; init; }

    public double? MemoryTotalGb { get; init; }

    public double? VramUsedGb { get; init; }

    public double? VramTotalGb { get; init; }

    public int? BatteryPercent { get; init; }

    public bool? IsOnAcPower { get; init; }

    public int? DisplayRefreshRate { get; init; }

    public OperatingMode? OperatingMode { get; init; }

    public GpuMuxMode? GpuMuxMode { get; init; }

    public FanMode? FanMode { get; init; }
}

public sealed record ManagedServiceInfo(
    string Name,
    string DisplayName,
    string Status,
    string StartMode,
    bool IsRequired,
    bool IsManagedConflict);
