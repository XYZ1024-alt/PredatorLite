using System.Text.Json.Nodes;
using PredatorLite.Core.Models;
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
