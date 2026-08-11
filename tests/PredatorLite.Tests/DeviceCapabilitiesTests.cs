using PredatorLite.Core.Models;

namespace PredatorLite.Tests;

public sealed class DeviceCapabilitiesTests
{
    [Theory]
    [InlineData(HardwareWriteBlockReason.None, true, false, true)]
    [InlineData(HardwareWriteBlockReason.None, false, true, true)]
    [InlineData(HardwareWriteBlockReason.None, true, true, true)]
    [InlineData(HardwareWriteBlockReason.None, false, false, false)]
    [InlineData(HardwareWriteBlockReason.ControlBackendUnavailable, true, false, false)]
    [InlineData(HardwareWriteBlockReason.UnsupportedTargetProfile, true, true, false)]
    public void CanWriteHardwareRequiresNoBlockReasonAndAPlatformTransport(
        HardwareWriteBlockReason blockReason,
        bool service,
        bool wmi,
        bool expected)
    {
        DeviceCapabilities capabilities = new()
        {
            Device = new DeviceIdentity("Lenovo", "Unknown Predator", "V1.00", "Windows 11"),
            TargetProfileId = null,
            IsValidatedTarget = false,
            WriteBlockReason = blockReason,
            AcerServiceAvailable = service,
            AcerWmiAvailable = wmi
        };

        Assert.Equal(expected, capabilities.CanWriteHardware);
    }

    [Fact]
    public void UnvalidatedTargetWithBackendCanWrite()
    {
        DeviceCapabilities capabilities = new()
        {
            Device = new DeviceIdentity("Lenovo", "Unknown Predator", "V1.00", "Windows 11"),
            TargetProfileId = null,
            IsValidatedTarget = false,
            WriteBlockReason = HardwareWriteBlockReason.None,
            AcerServiceAvailable = true
        };

        Assert.True(capabilities.CanWriteHardware);
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
