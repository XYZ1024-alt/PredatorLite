using PredatorLite.Core.Models;
using PredatorLite.Core.Services;

namespace PredatorLite.Tests;

public sealed class StartupOperatingModePolicyTests
{
    [Theory]
    [InlineData(OperatingMode.Silent)]
    [InlineData(OperatingMode.Balanced)]
    [InlineData(OperatingMode.Performance)]
    [InlineData(OperatingMode.Turbo)]
    public void AcPowerRestoresEverySavedNonEcoMode(OperatingMode savedMode)
    {
        OperatingMode? result = StartupOperatingModePolicy.Resolve(
            savedMode,
            autoEcoOnBattery: true,
            isOnAcPower: true);

        Assert.Equal(savedMode, result);
    }

    [Fact]
    public void BatteryUsesEcoWhenAutomationIsEnabled()
    {
        OperatingMode? result = StartupOperatingModePolicy.Resolve(
            OperatingMode.Turbo,
            autoEcoOnBattery: true,
            isOnAcPower: false);

        Assert.Equal(OperatingMode.Eco, result);
    }

    [Theory]
    [InlineData(OperatingMode.Silent)]
    [InlineData(OperatingMode.Balanced)]
    [InlineData(OperatingMode.Performance)]
    [InlineData(OperatingMode.Turbo)]
    public void BatteryRestoresSavedModeWhenAutomationIsDisabled(OperatingMode savedMode)
    {
        OperatingMode? result = StartupOperatingModePolicy.Resolve(
            savedMode,
            autoEcoOnBattery: false,
            isOnAcPower: false);

        Assert.Equal(savedMode, result);
    }

    [Fact]
    public void UnknownPowerSkipsWriteWhenAutomationIsEnabled()
    {
        OperatingMode? result = StartupOperatingModePolicy.Resolve(
            OperatingMode.Performance,
            autoEcoOnBattery: true,
            isOnAcPower: null);

        Assert.Null(result);
    }

    [Fact]
    public void UnknownPowerRestoresSavedModeWhenAutomationIsDisabled()
    {
        OperatingMode? result = StartupOperatingModePolicy.Resolve(
            OperatingMode.Performance,
            autoEcoOnBattery: false,
            isOnAcPower: null);

        Assert.Equal(OperatingMode.Performance, result);
    }

    [Theory]
    [InlineData(OperatingMode.Eco)]
    [InlineData((OperatingMode)0x02)]
    [InlineData((OperatingMode)0xFF)]
    public void InvalidSavedModesFallBackToBalanced(OperatingMode savedMode)
    {
        Assert.Equal(
            OperatingMode.Balanced,
            StartupOperatingModePolicy.NormalizeSavedMode(savedMode));
        Assert.Equal(
            OperatingMode.Balanced,
            StartupOperatingModePolicy.Resolve(savedMode, autoEcoOnBattery: false, isOnAcPower: true));
    }

    [Theory]
    [InlineData(OperatingMode.Silent, true)]
    [InlineData(OperatingMode.Balanced, true)]
    [InlineData(OperatingMode.Performance, true)]
    [InlineData(OperatingMode.Turbo, true)]
    [InlineData(OperatingMode.Eco, false)]
    [InlineData((OperatingMode)0x02, false)]
    public void OnlyDefinedNonEcoModesAreRemembered(OperatingMode mode, bool expected)
    {
        Assert.Equal(expected, StartupOperatingModePolicy.ShouldRemember(mode));
    }
}
