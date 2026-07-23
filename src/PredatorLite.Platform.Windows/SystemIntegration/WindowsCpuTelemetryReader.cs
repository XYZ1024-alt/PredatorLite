using System.Management;
using PredatorLite.Core.Abstractions;

namespace PredatorLite.Platform.Windows.SystemIntegration;

internal sealed class WindowsCpuTelemetryReader
{
    private readonly object _sync = new();
    private readonly IAppLogger _logger;
    private bool _failureLogged;

    public WindowsCpuTelemetryReader(IAppLogger logger)
    {
        _logger = logger;
    }

    public WindowsCpuTelemetry Read()
    {
        lock (_sync)
        {
            try
            {
                using ManagementObjectSearcher searcher = new(
                    "SELECT PercentProcessorTime, ProcessorFrequency " +
                    "FROM Win32_PerfFormattedData_Counters_ProcessorInformation " +
                    "WHERE Name = '_Total'");
                using ManagementObjectCollection collection = searcher.Get();
                foreach (ManagementObject item in collection)
                {
                    using (item)
                    {
                        WindowsCpuTelemetry telemetry = new(
                            LoadPercent: ReadRange(item["PercentProcessorTime"], 0, 100),
                            ClockMhz: ReadRange(item["ProcessorFrequency"], 0, 10000));
                        if (_failureLogged)
                        {
                            _logger.Info("Windows CPU performance-counter telemetry recovered.");
                            _failureLogged = false;
                        }

                        return telemetry;
                    }
                }

                throw new InvalidDataException("Windows CPU performance counters returned no total processor.");
            }
            catch (Exception exception)
            {
                if (!_failureLogged)
                {
                    _logger.Error("Windows CPU performance-counter telemetry failed", exception);
                    _failureLogged = true;
                }

                return new WindowsCpuTelemetry();
            }
        }
    }

    private static double? ReadRange(object? value, double minimum, double maximum)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            double number = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            return double.IsFinite(number) && number >= minimum && number <= maximum
                ? number
                : null;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }
}

internal sealed record WindowsCpuTelemetry(
    double? LoadPercent = null,
    double? ClockMhz = null);
