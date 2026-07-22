using PredatorLite.Core.Models;

namespace PredatorLite.Tests;

public sealed class DeviceCapabilitiesTests
{
    [Theory]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    public void CanWriteHardwareRequiresValidationAndAPlatformTransport(
        bool validated,
        bool service,
        bool wmi,
        bool expected)
    {
        DeviceCapabilities capabilities = new()
        {
            Device = new DeviceIdentity("Acer", "Predator PHN16-71", "V1.20", "Windows 11"),
            IsValidatedModel = validated,
            AcerServiceAvailable = service,
            AcerWmiAvailable = wmi
        };

        Assert.Equal(expected, capabilities.CanWriteHardware);
    }

    [Fact]
    public void GpuMuxEnumContainsOnlyOfficialRoutingChoices()
    {
        Assert.Equal([GpuMuxMode.Discrete, GpuMuxMode.Hybrid], Enum.GetValues<GpuMuxMode>());
    }
}
