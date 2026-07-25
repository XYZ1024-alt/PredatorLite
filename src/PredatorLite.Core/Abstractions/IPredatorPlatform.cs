using PredatorLite.Core.Models;

namespace PredatorLite.Core.Abstractions;

public interface IPredatorPlatform : IAsyncDisposable
{
    Task<PlatformStartupState> ProbeStartupAsync(CancellationToken cancellationToken = default);

    Task<DeviceCapabilities> ProbeAsync(CancellationToken cancellationToken = default);

    Task<HardwareSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default);

    void SetExtendedTelemetryEnabled(bool enabled);

    Task<IReadOnlyDictionary<DeviceSettingId, DeviceSettingState>> ReadDeviceSettingsAsync(
        CancellationToken cancellationToken = default);

    Task<ApplyResult> EnsureStartupOperatingModeAsync(
        OperatingMode mode,
        CancellationToken cancellationToken = default);

    Task<ApplyResult> SetOperatingModeAsync(
        OperatingMode mode,
        CancellationToken cancellationToken = default);

    Task<ApplyResult> SetFanModeAsync(
        FanMode mode,
        int cpuSpeedPercent = 50,
        int gpuSpeedPercent = 50,
        CancellationToken cancellationToken = default);

    Task<ApplyResult> SetGpuMuxModeAsync(
        GpuMuxMode mode,
        CancellationToken cancellationToken = default);

    Task<ApplyResult> SetChargeLimitAsync(
        bool limitTo80Percent,
        CancellationToken cancellationToken = default);

    Task<ApplyResult> SetLightingAsync(
        LightingProfile profile,
        CancellationToken cancellationToken = default);

    Task<ApplyResult> SetDeviceSettingAsync(
        DeviceSettingId setting,
        bool enabled,
        CancellationToken cancellationToken = default);

    Task<ApplyResult> SetRefreshRateAsync(
        int refreshRate,
        bool enableOverdrive,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ManagedServiceInfo>> GetManagedServicesAsync(
        CancellationToken cancellationToken = default);
}

public interface IFanGuardWriteLease : IAsyncDisposable
{
    bool IsValid { get; }
}

public interface IFanGuardOwnership
{
    bool IsActive { get; }

    ValueTask<IFanGuardWriteLease?> AcquireHardwareWriteLeaseAsync(
        CancellationToken cancellationToken = default);
}

public interface IModeKeySource : IAsyncDisposable
{
    event EventHandler? ModeKeyPressed;

    Task StartAsync(CancellationToken cancellationToken = default);
}

public interface ISettingsStore : IDisposable
{
    string SettingsPath { get; }

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public interface IAppLogger : IDisposable
{
    string LogDirectory { get; }

    void Info(string message);

    void LogError(string message, Exception? exception = null);
}
