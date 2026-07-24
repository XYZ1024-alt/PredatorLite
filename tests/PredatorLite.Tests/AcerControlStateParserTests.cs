using System.Text.Json;
using PredatorLite.Core.Models;
using PredatorLite.Platform.Windows.Acer;

namespace PredatorLite.Tests;

public sealed class AcerControlStateParserTests
{
    [Theory]
    [InlineData(0, OperatingMode.Silent)]
    [InlineData(1, OperatingMode.Balanced)]
    [InlineData(4, OperatingMode.Performance)]
    [InlineData(5, OperatingMode.Turbo)]
    [InlineData(6, OperatingMode.Eco)]
    public void OperatingModeAcceptsDefinedByteValues(int value, OperatingMode expected)
    {
        Assert.True(AcerControlStateParser.TryReadOperatingMode(CreateResponse(value), out OperatingMode mode));
        Assert.Equal(expected, mode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(255)]
    [InlineData(260)]
    public void OperatingModeRejectsOutOfRangeAndUnknownValues(int value)
    {
        Assert.False(AcerControlStateParser.TryReadOperatingMode(CreateResponse(value), out _));
    }

    [Theory]
    [InlineData(0, FanMode.Auto)]
    [InlineData(1, FanMode.Max)]
    [InlineData(2, FanMode.Custom)]
    public void FanModeAcceptsDefinedValues(int value, FanMode expected)
    {
        Assert.True(AcerControlStateParser.TryReadFanMode(CreateResponse(value), out FanMode mode));
        Assert.Equal(expected, mode);
    }

    [Theory]
    [InlineData(1, GpuMuxMode.Discrete)]
    [InlineData(2, GpuMuxMode.Hybrid)]
    public void GpuMuxModeAcceptsDefinedValues(int value, GpuMuxMode expected)
    {
        Assert.True(AcerControlStateParser.TryReadGpuMuxMode(CreateResponse(value), out GpuMuxMode mode));
        Assert.Equal(expected, mode);
    }

    [Fact]
    public void MissingModeFieldIsRejected()
    {
        AcerResponse response = CreateResponseJson("{}");

        Assert.False(AcerControlStateParser.TryReadOperatingMode(response, out _));
        Assert.False(AcerControlStateParser.TryReadFanMode(response, out _));
        Assert.False(AcerControlStateParser.TryReadGpuMuxMode(response, out _));
    }

    [Fact]
    public void FailedResponseIsRejectedEvenWithValidModeData()
    {
        AcerResponse response = CreateResponse(4) with { Result = 1 };

        Assert.False(AcerControlStateParser.TryReadOperatingMode(response, out _));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(260)]
    public void FanModeRejectsUnknownValues(int value)
    {
        Assert.False(AcerControlStateParser.TryReadFanMode(CreateResponse(value), out _));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(260)]
    public void GpuMuxModeRejectsUnknownValues(int value)
    {
        Assert.False(AcerControlStateParser.TryReadGpuMuxMode(CreateResponse(value), out _));
    }

    private static AcerResponse CreateResponse(int mode) =>
        CreateResponseJson($"{{\"mode\":{mode}}}");

    private static AcerResponse CreateResponseJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return new AcerResponse(0, "test", document.RootElement.Clone(), json);
    }
}
