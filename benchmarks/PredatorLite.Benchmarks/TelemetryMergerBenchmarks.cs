using BenchmarkDotNet.Attributes;
using PredatorLite.Core.Models;
using PredatorLite.Platform.Windows.Acer;
using PredatorLite.Platform.Windows.SystemIntegration;

namespace PredatorLite.Benchmarks;

[MemoryDiagnoser]
public class TelemetryMergerBenchmarks
{
    private readonly AcerMonitorTelemetry _acer = new(
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

    private readonly AcerWmiTelemetry _wmi = new(80, 75, 4500, 4600);

    private readonly ExtraTelemetry _extra = new(
        GpuTemperatureC: 55,
        GpuLoadPercent: 45,
        GpuPowerWatts: 80,
        GpuClockMhz: 1700,
        MemoryUsedGb: 8,
        MemoryTotalGb: 16,
        VramUsedGb: 3,
        VramTotalGb: 8);

    private readonly WindowsCpuTelemetry _windowsCpu = new(50, 2400);

    [Benchmark]
    public HardwareSnapshot Merge() => TelemetryMerger.Merge(_acer, _wmi, _extra, _windowsCpu);
}
