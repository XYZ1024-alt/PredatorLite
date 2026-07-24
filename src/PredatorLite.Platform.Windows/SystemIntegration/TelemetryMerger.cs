using PredatorLite.Core.Models;
using PredatorLite.Platform.Windows.Acer;

namespace PredatorLite.Platform.Windows.SystemIntegration;

internal static class TelemetryMerger
{
    public static HardwareSnapshot Merge(
        AcerMonitorTelemetry? acer,
        AcerWmiTelemetry? wmi,
        ExtraTelemetry? hardwareMonitor,
        WindowsCpuTelemetry? windowsCpu)
    {
        ExtraTelemetry extra = hardwareMonitor ?? new ExtraTelemetry();
        WindowsCpuTelemetry cpu = windowsCpu ?? new WindowsCpuTelemetry();

        (double? memoryUsed, double? memoryTotal) = ValidatePair(
            extra.MemoryUsedGb,
            extra.MemoryTotalGb);
        if (!memoryUsed.HasValue || !memoryTotal.HasValue)
        {
            (memoryUsed, memoryTotal) = ValidatePair(
                acer?.MemoryUsedGb,
                acer?.MemoryTotalGb);
        }

        (double? vramUsed, double? vramTotal) = ValidatePair(
            extra.VramUsedGb,
            extra.VramTotalGb);

        return new HardwareSnapshot
        {
            CpuTemperatureC = FirstInteger(1, 120, acer?.CpuTemperatureC, wmi?.CpuTemperatureC),
            GpuTemperatureC = FirstInteger(
                1,
                120,
                acer?.GpuTemperatureC,
                wmi?.GpuTemperatureC,
                Round(extra.GpuTemperatureC)),
            CpuFanRpm = FirstInteger(0, 20000, acer?.CpuFanRpm, wmi?.CpuFanRpm),
            GpuFanRpm = FirstInteger(0, 20000, acer?.GpuFanRpm, wmi?.GpuFanRpm),
            CpuLoadPercent = FirstDouble(0, 100, acer?.CpuLoadPercent, cpu.LoadPercent),
            GpuLoadPercent = FirstDouble(0, 100, acer?.GpuLoadPercent, extra.GpuLoadPercent),
            CpuPowerWatts = null,
            CpuPowerSupported = false,
            GpuPowerWatts = FirstDouble(0, 1000, extra.GpuPowerWatts),
            CpuClockMhz = FirstDouble(0, 10000, acer?.CpuClockMhz, cpu.ClockMhz),
            GpuClockMhz = FirstDouble(0, 10000, acer?.GpuClockMhz, extra.GpuClockMhz),
            MemoryUsedGb = memoryUsed,
            MemoryTotalGb = memoryTotal,
            VramUsedGb = vramUsed,
            VramTotalGb = vramTotal
        };
    }

    private static int? Round(double? value) =>
        value.HasValue && double.IsFinite(value.Value)
            ? (int)Math.Round(value.Value)
            : null;

    private static int? FirstInteger(
        int minimum,
        int maximum,
        int? first,
        int? second = null,
        int? third = null)
    {
        if (IsValid(first, minimum, maximum))
        {
            return first;
        }

        return IsValid(second, minimum, maximum)
            ? second
            : IsValid(third, minimum, maximum) ? third : null;
    }

    private static double? FirstDouble(
        double minimum,
        double maximum,
        double? first,
        double? second = null)
    {
        if (IsValid(first, minimum, maximum))
        {
            return first;
        }

        return IsValid(second, minimum, maximum) ? second : null;
    }

    private static bool IsValid(int? value, int minimum, int maximum) =>
        value is not null && value >= minimum && value <= maximum;

    private static bool IsValid(double? value, double minimum, double maximum) =>
        value is not null &&
        double.IsFinite(value.Value) &&
        value.Value >= minimum &&
        value.Value <= maximum;

    private static (double? Used, double? Total) ValidatePair(double? used, double? total)
    {
        if (!used.HasValue ||
            !total.HasValue ||
            !double.IsFinite(used.Value) ||
            !double.IsFinite(total.Value) ||
            used.Value < 0 ||
            total.Value <= 0 ||
            used.Value > total.Value ||
            total.Value > 2048)
        {
            return (null, null);
        }

        return (used, total);
    }
}

internal sealed record AcerWmiTelemetry(
    int? CpuTemperatureC = null,
    int? GpuTemperatureC = null,
    int? CpuFanRpm = null,
    int? GpuFanRpm = null);
