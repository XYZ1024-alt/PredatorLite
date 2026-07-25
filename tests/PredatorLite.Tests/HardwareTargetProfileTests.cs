using PredatorLite.Core.Models;
using PredatorLite.Platform.Windows;

namespace PredatorLite.Tests;

public sealed class HardwareTargetProfileTests
{
    [Theory]
    [InlineData("Acer", "Predator PHN16-71", "V1.20", true)]
    [InlineData("ACER INCORPORATED", "predator phn16-71", "v1.20", true)]
    [InlineData("Acer", "Predator PHN16-71", "V1.21", false)]
    [InlineData("Acer", "Predator PHN16-710", "V1.20", false)]
    [InlineData("Acer Predator", "Predator PHN16-71", "V1.20", false)]
    public void ResolverRequiresAnExactProfileMatch(
        string manufacturer,
        string model,
        string biosVersion,
        bool expected)
    {
        DeviceIdentity identity = new(manufacturer, model, biosVersion, "Windows 11");

        bool actual = HardwareTargetProfileCatalog.TryResolve(identity, out HardwareTargetProfile? profile);

        Assert.Equal(expected, actual);
        Assert.Equal(expected, profile is not null);
    }

    [Fact]
    public void CurrentProfileAuthorizesOnlyTheKnownControlSurface()
    {
        DeviceIdentity identity = new("Acer", "Predator PHN16-71", "V1.20", "Windows 11");

        Assert.True(HardwareTargetProfileCatalog.TryResolve(identity, out HardwareTargetProfile? profile));
        Assert.NotNull(profile);
        Assert.Equal(HardwareTargetProfileCatalog.Phn1671V120ProfileId, profile.Id);
        Assert.True(profile.AuthorizedControls.HasFlag(HardwareControlCapabilities.OperatingMode));
        Assert.True(profile.AuthorizedControls.HasFlag(HardwareControlCapabilities.FanControl));
        Assert.True(profile.AuthorizedControls.HasFlag(HardwareControlCapabilities.GpuMux));
        Assert.True(profile.AuthorizedControls.HasFlag(HardwareControlCapabilities.BatteryHealth));
        Assert.True(profile.AuthorizedControls.HasFlag(HardwareControlCapabilities.Lighting));
        Assert.True(profile.AuthorizedControls.HasFlag(HardwareControlCapabilities.DeviceSettings));
        Assert.True(profile.AuthorizedControls.HasFlag(HardwareControlCapabilities.Display));
        HardwareControlProfile fanProfile = Assert.Single(
            profile.ControlProfiles,
            control => control.Control == HardwareControlCapabilities.FanControl);
        Assert.Equal(HardwareTransportKind.AcerService, fanProfile.PrimaryTransport);
        Assert.Equal(HardwareTransportKind.AcerWmi, fanProfile.FallbackTransport);
        Assert.True(fanProfile.RequiresReadBack);
        Assert.True(fanProfile.RequiresFanGuard);
        HardwareControlProfile gpuProfile = Assert.Single(
            profile.ControlProfiles,
            control => control.Control == HardwareControlCapabilities.GpuMux);
        Assert.True(gpuProfile.RequiresReboot);
        Assert.Contains(DeviceSettingId.PanelDynamicRefresh, profile.UnsupportedDeviceSettings);
        Assert.Contains(DeviceSettingId.SoundMode, profile.UnsupportedDeviceSettings);
    }
}
