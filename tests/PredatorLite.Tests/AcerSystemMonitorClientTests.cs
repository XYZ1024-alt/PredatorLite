using System.Text.Json;
using PredatorLite.Platform.Windows.Acer;

namespace PredatorLite.Tests;

public sealed class AcerSystemMonitorClientTests
{
    [Theory]
    [InlineData("0")]
    [InlineData(0)]
    public void ParseResponseAcceptsStringAndNumericSuccessResults(object result)
    {
        string json = $$"""
            {
              "result": {{JsonSerializer.Serialize(result)}},
              "request": "GET_MONITOR_DATA",
              "data": {
                "CPU_TEMPERATURE": 72,
                "GPU1_TEMPERATURE": "57",
                "CPU_FANSPEED": 3816,
                "GPU1_FANSPEED": "3871",
                "CPU_USAGE": 14.5,
                "GPU1_USAGE": 5,
                "CPU_FREQUENCY": 3044.9,
                "CPU_MAX_FREQUENCY": 5000,
                "GPU1_FREQUENCY": 345,
                "GPU1_MAX_FREQUENCY": 3195,
                "RAM_TOTAL": 32768,
                "RAM_USAGE": 50
              }
            }
            """;

        AcerMonitorTelemetry telemetry = AcerSystemMonitorClient.ParseResponse(json);

        Assert.Equal(72, telemetry.CpuTemperatureC);
        Assert.Equal(57, telemetry.GpuTemperatureC);
        Assert.Equal(3816, telemetry.CpuFanRpm);
        Assert.Equal(3871, telemetry.GpuFanRpm);
        Assert.Equal(14.5, telemetry.CpuLoadPercent);
        Assert.Equal(5, telemetry.GpuLoadPercent);
        Assert.Equal(3044.9, telemetry.CpuClockMhz);
        Assert.Equal(345, telemetry.GpuClockMhz);
        Assert.Equal(16, telemetry.MemoryUsedGb);
        Assert.Equal(32, telemetry.MemoryTotalGb);
        Assert.True(telemetry.HasPrimaryTelemetry);
    }

    [Fact]
    public void ParseResponseRejectsMalformedJson()
    {
        Assert.ThrowsAny<JsonException>(() =>
            AcerSystemMonitorClient.ParseResponse("""{"result":0,"request":"GET_MONITOR_DATA","data":"""));
    }

    [Fact]
    public void ParseResponseRejectsWrongRequest()
    {
        const string json = """
            {
              "result": 0,
              "request": "OPERATING_MODE",
              "data": {}
            }
            """;

        Assert.Throws<InvalidDataException>(() => AcerSystemMonitorClient.ParseResponse(json));
    }

    [Fact]
    public void ParseResponseRejectsFrequencyAboveReportedMaximumMargin()
    {
        const string json = """
            {
              "result": 0,
              "request": "GET_MONITOR_DATA",
              "data": {
                "CPU_FREQUENCY": 7000,
                "CPU_MAX_FREQUENCY": 5000,
                "GPU1_FREQUENCY": 345,
                "GPU1_MAX_FREQUENCY": 3195
              }
            }
            """;

        AcerMonitorTelemetry telemetry = AcerSystemMonitorClient.ParseResponse(json);

        Assert.Null(telemetry.CpuClockMhz);
        Assert.Equal(345, telemetry.GpuClockMhz);
    }

    [Fact]
    public void ParseResponseRejectsMissingDataObject()
    {
        const string json = """
            {
              "result": "0",
              "request": "GET_MONITOR_DATA"
            }
            """;

        Assert.Throws<InvalidDataException>(() => AcerSystemMonitorClient.ParseResponse(json));
    }
}
