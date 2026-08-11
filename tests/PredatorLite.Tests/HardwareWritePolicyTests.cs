using PredatorLite.Core.Models;
using PredatorLite.Platform.Windows;

namespace PredatorLite.Tests;

public sealed class HardwareWritePolicyTests
{
    [Theory]
    [InlineData(true, false, HardwareWriteBlockReason.None)]
    [InlineData(false, true, HardwareWriteBlockReason.None)]
    [InlineData(true, true, HardwareWriteBlockReason.None)]
    [InlineData(false, false, HardwareWriteBlockReason.ControlBackendUnavailable)]
    public void BackendAvailabilityDeterminesWriteBoundary(
        bool serviceAvailable,
        bool wmiAvailable,
        HardwareWriteBlockReason expected)
    {
        HardwareWriteBlockReason actual = PredatorPlatform.GetWriteBlockReason(
            serviceAvailable,
            wmiAvailable);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Lenovo", "Unknown Predator", "V1.00", true, true, HardwareWriteBlockReason.None)]
    [InlineData("Acer", "Predator PHN16-72", "V1.20", false, true, HardwareWriteBlockReason.None)]
    [InlineData("Acer", "Predator PHN16-71", "V1.21", true, false, HardwareWriteBlockReason.None)]
    public void UnmatchedIdentityNeverBlocksAnAvailableBackend(
        string manufacturer,
        string model,
        string bios,
        bool serviceAvailable,
        bool wmiAvailable,
        HardwareWriteBlockReason expected)
    {
        _ = new DeviceIdentity(manufacturer, model, bios, "10.0.26100");

        HardwareWriteBlockReason actual = PredatorPlatform.GetWriteBlockReason(
            serviceAvailable,
            wmiAvailable);

        Assert.Equal(expected, actual);
    }
}
