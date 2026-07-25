using System.Text.Json.Nodes;
using PredatorLite.Core.Models;
using PredatorLite.ElevatedHelper;
using ElevatedHelperProgram = PredatorLite.ElevatedHelper.Program;
using FanGuardProgram = PredatorLite.FanGuard.Program;

namespace PredatorLite.Tests;

public sealed class CompanionSafetyBoundaryTests
{
    [Theory]
    [InlineData("disable", true)]
    [InlineData("restore", true)]
    [InlineData("status", false)]
    [InlineData("", false)]
    public void ElevatedHelperAcceptsOnlyFixedCommands(string command, bool expected)
    {
        Assert.Equal(expected, ElevatedHelperProgram.IsSupportedCommand(command));
    }

    [Theory]
    [InlineData("AcerCCAgentSvis", true)]
    [InlineData("AcerDIAgentSvis", true)]
    [InlineData("AcerDeviceEnablingServiceV2", true)]
    [InlineData("PredatorService", true)]
    [InlineData("AcerServiceSvc", false)]
    [InlineData("Spooler", false)]
    public void ElevatedHelperServiceAllowlistIsExact(string serviceName, bool expected)
    {
        Assert.Equal(expected, ElevatedHelperProgram.IsManagedService(serviceName));
    }

    [Fact]
    public void ElevatedHelperRejectsTamperedBackupEntries()
    {
        ServiceBackup backup = new()
        {
            IsApplied = true,
            Services =
            [
                new ServiceBackupItem
                {
                    Name = "Spooler",
                    StartValue = 2
                }
            ]
        };

        Assert.False(ElevatedHelperProgram.IsValidBackup(backup));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void ElevatedHelperRejectsInvalidServiceStartValues(int startValue)
    {
        ServiceBackup backup = new()
        {
            Services =
            [
                new ServiceBackupItem
                {
                    Name = "PredatorService",
                    StartValue = startValue
                }
            ]
        };

        Assert.False(ElevatedHelperProgram.IsValidBackup(backup));
    }

    [Fact]
    public void ElevatedHelperAcceptsAValidBackup()
    {
        ServiceBackup backup = new()
        {
            Services =
            [
                new ServiceBackupItem
                {
                    Name = "PredatorService",
                    StartValue = 3
                }
            ]
        };

        Assert.True(ElevatedHelperProgram.IsValidBackup(backup));
    }

    [Fact]
    public void ElevatedHelperRequiresAnAppliedBackupForRestore()
    {
        ServiceBackup backup = new()
        {
            Services =
            [
                new ServiceBackupItem
                {
                    Name = "PredatorService",
                    StartValue = 3
                }
            ]
        };

        Assert.False(ElevatedHelperProgram.IsValidRestoreBackup(backup));
        backup.IsApplied = true;
        Assert.True(ElevatedHelperProgram.IsValidRestoreBackup(backup));
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(1, 3, 3)]
    [InlineData(2, 0, 2)]
    [InlineData(0, 0, 1)]
    public void FanGuardDoesNotReportUnverifiedRecoveryAsSuccess(
        int statusValue,
        int verifiedExitCode,
        int expectedExitCode)
    {
        FanGuardProgram.FanRecoveryStatus status =
            (FanGuardProgram.FanRecoveryStatus)statusValue;
        Assert.Equal(expectedExitCode, FanGuardProgram.RecoveryExitCode(status, verifiedExitCode));
    }

    [Fact]
    public void FanGuardRecoveryPayloadForcesAutomaticModeOnBothChannels()
    {
        JsonObject parameters = FanGuardProgram.CreateAutomaticFanParameters();

        Assert.Equal((int)FanMode.Auto, parameters["mode"]!.GetValue<int>());
        JsonArray fans = Assert.IsType<JsonArray>(parameters["custom_fan_data"]);
        Assert.Equal(2, fans.Count);
        AssertFan(fans[0], "CPU");
        AssertFan(fans[1], "GPU");
    }

    private static void AssertFan(JsonNode? node, string expectedName)
    {
        JsonObject fan = Assert.IsType<JsonObject>(node);
        Assert.Equal(1, fan["fan_custom_auto"]!.GetValue<int>());
        Assert.Equal(50, fan["fan_custom_speed"]!.GetValue<int>());
        Assert.Equal(expectedName, fan["fan_name"]!.GetValue<string>());
    }
}
