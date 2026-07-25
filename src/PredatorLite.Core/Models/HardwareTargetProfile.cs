using System;

namespace PredatorLite.Core.Models;

[Flags]
public enum HardwareControlCapabilities
{
    None = 0,
    OperatingMode = 1 << 0,
    FanControl = 1 << 1,
    GpuMux = 1 << 2,
    BatteryHealth = 1 << 3,
    Lighting = 1 << 4,
    DeviceSettings = 1 << 5,
    Display = 1 << 6
}

public enum HardwareTransportKind
{
    AcerService,
    AcerWmi,
    WindowsDisplay
}

public sealed record HardwareControlProfile(
    HardwareControlCapabilities Control,
    HardwareTransportKind PrimaryTransport,
    HardwareTransportKind? FallbackTransport,
    bool RequiresReadBack,
    bool RequiresFanGuard = false,
    bool RequiresReboot = false);

public sealed record HardwareTargetProfile(
    string Id,
    IReadOnlyList<string> ManufacturerAliases,
    string Model,
    string BiosVersion,
    IReadOnlyList<HardwareControlProfile> ControlProfiles,
    IReadOnlySet<DeviceSettingId> UnsupportedDeviceSettings)
{
    public HardwareControlCapabilities AuthorizedControls =>
        ControlProfiles.Aggregate(
            HardwareControlCapabilities.None,
            (controls, profile) => controls | profile.Control);
}
