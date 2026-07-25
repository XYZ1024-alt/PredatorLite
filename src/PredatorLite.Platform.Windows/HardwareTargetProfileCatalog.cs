using PredatorLite.Core.Models;
using PredatorLite.Platform.Windows.SystemIntegration;

namespace PredatorLite.Platform.Windows;

public static class HardwareTargetProfileCatalog
{
    public const string Phn1671V120ProfileId = "acer-predator-phn16-71-v1.20";

    private static readonly IReadOnlyList<HardwareTargetProfile> Profiles =
    [
        new HardwareTargetProfile(
            Phn1671V120ProfileId,
            ["Acer", "Acer Incorporated"],
            "Predator PHN16-71",
            "V1.20",
            [
                new HardwareControlProfile(
                    HardwareControlCapabilities.OperatingMode,
                    HardwareTransportKind.AcerService,
                    HardwareTransportKind.AcerWmi,
                    RequiresReadBack: true),
                new HardwareControlProfile(
                    HardwareControlCapabilities.FanControl,
                    HardwareTransportKind.AcerService,
                    HardwareTransportKind.AcerWmi,
                    RequiresReadBack: true,
                    RequiresFanGuard: true),
                new HardwareControlProfile(
                    HardwareControlCapabilities.GpuMux,
                    HardwareTransportKind.AcerService,
                    null,
                    RequiresReadBack: true,
                    RequiresReboot: true),
                new HardwareControlProfile(
                    HardwareControlCapabilities.BatteryHealth,
                    HardwareTransportKind.AcerWmi,
                    null,
                    RequiresReadBack: true),
                new HardwareControlProfile(
                    HardwareControlCapabilities.Lighting,
                    HardwareTransportKind.AcerService,
                    null,
                    RequiresReadBack: false),
                new HardwareControlProfile(
                    HardwareControlCapabilities.DeviceSettings,
                    HardwareTransportKind.AcerService,
                    HardwareTransportKind.AcerWmi,
                    RequiresReadBack: true),
                new HardwareControlProfile(
                    HardwareControlCapabilities.Display,
                    HardwareTransportKind.WindowsDisplay,
                    null,
                    RequiresReadBack: true)
            ],
            new HashSet<DeviceSettingId>
            {
                DeviceSettingId.PanelDynamicRefresh,
                DeviceSettingId.SoundMode
            })
    ];

    public static bool TryResolve(
        DeviceIdentity identity,
        out HardwareTargetProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(identity);
        string manufacturer = identity.Manufacturer.Trim();
        string model = identity.Model.Trim();
        string biosVersion = identity.BiosVersion.Trim();
        profile = Profiles.FirstOrDefault(candidate =>
            candidate.ManufacturerAliases.Any(alias =>
                string.Equals(alias, manufacturer, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(candidate.Model, model, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.BiosVersion, biosVersion, StringComparison.OrdinalIgnoreCase));
        return profile is not null;
    }

    public static bool TryResolveCurrent(out HardwareTargetProfile? profile) =>
        TryResolve(SystemIdentityReader.Read(), out profile);
}
