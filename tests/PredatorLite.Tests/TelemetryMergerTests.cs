using PredatorLite.Core.Models;
using PredatorLite.Platform.Windows.Acer;
using PredatorLite.Platform.Windows.SystemIntegration;

namespace PredatorLite.Tests;

public sealed class TelemetryMergerTests
{
    [Fact]
    public void MergeUsesAcerForPrimaryMetricsAndLhmForExtendedMetrics()
    {
        AcerMonitorTelemetry acer = new(
            CpuTemperatureC: 70,
            GpuTemperatureC: 60,
            CpuFanRpm: 3800,
            GpuFanRpm: 3900,
            CpuLoadPercent: 25,
            GpuLoadPercent: 35,
            CpuClockMhz: 3200,
            GpuClockMhz: 1800,
            MemoryUsedGb: 12,
            MemoryTotalGb: 32);
        AcerWmiTelemetry wmi = new(80, 75, 4500, 4600);
        ExtraTelemetry lhm = new(
            GpuTemperatureC: 55,
            GpuLoadPercent: 45,
            GpuPowerWatts: 80,
            GpuClockMhz: 1700,
            MemoryUsedGb: 8,
            MemoryTotalGb: 16,
            VramUsedGb: 3,
            VramTotalGb: 8);
        WindowsCpuTelemetry windowsCpu = new(50, 2400);

        HardwareSnapshot snapshot = TelemetryMerger.Merge(acer, wmi, lhm, windowsCpu);

        Assert.Equal(70, snapshot.CpuTemperatureC);
        Assert.Equal(60, snapshot.GpuTemperatureC);
        Assert.Equal(3800, snapshot.CpuFanRpm);
        Assert.Equal(3900, snapshot.GpuFanRpm);
        Assert.Equal(25, snapshot.CpuLoadPercent);
        Assert.Equal(35, snapshot.GpuLoadPercent);
        Assert.Equal(3200, snapshot.CpuClockMhz);
        Assert.Equal(1800, snapshot.GpuClockMhz);
        Assert.Equal(80, snapshot.GpuPowerWatts);
        Assert.Equal(8, snapshot.MemoryUsedGb);
        Assert.Equal(16, snapshot.MemoryTotalGb);
        Assert.Equal(3, snapshot.VramUsedGb);
        Assert.Equal(8, snapshot.VramTotalGb);
        Assert.Null(snapshot.CpuPowerWatts);
        Assert.False(snapshot.CpuPowerSupported);
        Assert.True(snapshot.HasLivePrimaryTelemetry);
    }

    [Fact]
    public void MergeFallsBackToWmiWindowsCountersAndLhm()
    {
        AcerMonitorTelemetry acer = new(
            MemoryUsedGb: 10,
            MemoryTotalGb: 32);
        AcerWmiTelemetry wmi = new(
            CpuTemperatureC: 68,
            GpuTemperatureC: 58,
            CpuFanRpm: 3600,
            GpuFanRpm: 3700);
        ExtraTelemetry lhm = new(
            GpuTemperatureC: 54,
            GpuLoadPercent: 42,
            GpuClockMhz: 1650);
        WindowsCpuTelemetry windowsCpu = new(
            LoadPercent: 30,
            ClockMhz: 2800);

        HardwareSnapshot snapshot = TelemetryMerger.Merge(acer, wmi, lhm, windowsCpu);

        Assert.Equal(68, snapshot.CpuTemperatureC);
        Assert.Equal(58, snapshot.GpuTemperatureC);
        Assert.Equal(3600, snapshot.CpuFanRpm);
        Assert.Equal(3700, snapshot.GpuFanRpm);
        Assert.Equal(30, snapshot.CpuLoadPercent);
        Assert.Equal(42, snapshot.GpuLoadPercent);
        Assert.Equal(2800, snapshot.CpuClockMhz);
        Assert.Equal(1650, snapshot.GpuClockMhz);
        Assert.Equal(10, snapshot.MemoryUsedGb);
        Assert.Equal(32, snapshot.MemoryTotalGb);
    }

    [Fact]
    public void MergeFallsBackToLhmGpuTemperatureAfterAcerAndWmi()
    {
        ExtraTelemetry lhm = new(GpuTemperatureC: 56);

        HardwareSnapshot snapshot = TelemetryMerger.Merge(null, null, lhm, null);

        Assert.Equal(56, snapshot.GpuTemperatureC);
        Assert.True(snapshot.HasLivePrimaryTelemetry);
    }

    [Fact]
    public void MergeRejectsInvalidLhmMemoryPairAndUsesAcerPair()
    {
        AcerMonitorTelemetry acer = new(MemoryUsedGb: 12, MemoryTotalGb: 32);
        ExtraTelemetry lhm = new(MemoryUsedGb: 20, MemoryTotalGb: 16);

        HardwareSnapshot snapshot = TelemetryMerger.Merge(acer, null, lhm, null);

        Assert.Equal(12, snapshot.MemoryUsedGb);
        Assert.Equal(32, snapshot.MemoryTotalGb);
    }

    [Fact]
    public void SnapshotWithoutPrimaryMetricsIsNotLive()
    {
        HardwareSnapshot snapshot = new()
        {
            BatteryPercent = 80,
            MemoryUsedGb = 10,
            MemoryTotalGb = 32
        };

        Assert.False(snapshot.HasLivePrimaryTelemetry);
    }
}
