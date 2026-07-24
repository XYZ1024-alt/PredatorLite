using PredatorLite.Core.Models;
using PredatorLite.Platform.Windows;

namespace PredatorLite.Tests;

public sealed class HardwareWritePolicyTests
{
    [Theory]
    [InlineData("Acer", "Predator PHN16-71", "V1.20", true, false, HardwareWriteBlockReason.None)]
    [InlineData("ACER INCORPORATED", "predator phn16-71", "v1.20", false, true, HardwareWriteBlockReason.None)]
    [InlineData("Lenovo", "Predator PHN16-71", "V1.20", true, true, HardwareWriteBlockReason.UnsupportedModel)]
    [InlineData("Acer", "Predator PHN16-72", "V1.20", true, true, HardwareWriteBlockReason.UnsupportedModel)]
    [InlineData("Acer", "Predator PHN16-71", "V1.21", true, true, HardwareWriteBlockReason.UnvalidatedBios)]
    [InlineData("Acer", "Predator PHN16-71", "V1.20", false, false, HardwareWriteBlockReason.ControlBackendUnavailable)]
    public void ExactIdentityAndBackendDetermineWriteBoundary(
        string manufacturer,
        string model,
        string bios,
        bool serviceAvailable,
        bool wmiAvailable,
        HardwareWriteBlockReason expected)
    {
        DeviceIdentity identity = new(manufacturer, model, bios, "10.0.26100");

        HardwareWriteBlockReason actual = PredatorPlatform.GetWriteBlockReason(
            identity,
            serviceAvailable,
            wmiAvailable);

        Assert.Equal(expected, actual);
    }
}
