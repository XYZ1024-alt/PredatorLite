using PredatorLite.Core.Models;

namespace PredatorLite.Tests;

public sealed class DeviceCapabilitiesTests
{
    [Theory]
    [InlineData(false, HardwareWriteBlockReason.UnsupportedTargetProfile, true, true, false)]
    [InlineData(true, HardwareWriteBlockReason.ControlBackendUnavailable, false, false, false)]
    [InlineData(true, HardwareWriteBlockReason.None, true, false, true)]
    [InlineData(true, HardwareWriteBlockReason.None, false, true, true)]
    [InlineData(true, HardwareWriteBlockReason.UnsupportedTargetProfile, true, true, false)]
    public void CanWriteHardwareRequiresValidationNoBlockReasonAndAPlatformTransport(
        bool validated,
        HardwareWriteBlockReason blockReason,
        bool service,
        bool wmi,
        bool expected)
    {
        DeviceCapabilities capabilities = new()
        {
            Device = new DeviceIdentity("Acer", "Predator PHN16-71", "V1.20", "Windows 11"),
            TargetProfileId = validated ? "test-profile" : null,
            IsValidatedTarget = validated,
            WriteBlockReason = blockReason,
            AcerServiceAvailable = service,
            AcerWmiAvailable = wmi
        };

        Assert.Equal(expected, capabilities.CanWriteHardware);
    }

    [Fact]
    public void MissingTargetProfileIdCannotAuthorizeWrites()
    {
        DeviceCapabilities capabilities = new()
        {
            Device = new DeviceIdentity("Acer", "Predator PHN16-71", "V1.20", "Windows 11"),
            IsValidatedTarget = true,
            WriteBlockReason = HardwareWriteBlockReason.None,
            AcerServiceAvailable = true
        };

        Assert.False(capabilities.CanWriteHardware);
    }

    [Fact]
    public void GpuMuxEnumContainsOnlyOfficialRoutingChoices()
    {
        Assert.Equal([GpuMuxMode.Discrete, GpuMuxMode.Hybrid], Enum.GetValues<GpuMuxMode>());
    }

    [Fact]
    public void AcerSystemMonitorDoesNotAuthorizeHardwareWrites()
    {
        DeviceCapabilities capabilities = new()
        {
            Device = new DeviceIdentity("Acer", "Predator PHN16-71", "V1.20", "Windows 11"),
            TargetProfileId = "test-profile",
            IsValidatedTarget = true,
            WriteBlockReason = HardwareWriteBlockReason.ControlBackendUnavailable,
            AcerSystemMonitorAvailable = true
        };

        Assert.False(capabilities.CanWriteHardware);
    }
}
