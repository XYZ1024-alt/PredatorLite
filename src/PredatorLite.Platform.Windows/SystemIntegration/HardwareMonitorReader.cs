using LibreHardwareMonitor.Hardware;
using PredatorLite.Core.Abstractions;

namespace PredatorLite.Platform.Windows.SystemIntegration;

internal sealed class HardwareMonitorReader : IDisposable
{
    private readonly object _sync = new();
    private readonly IAppLogger _logger;
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = false,
        IsGpuEnabled = true,
        IsMemoryEnabled = true
    };

    private bool _opened;
    private bool _failureLogged;

    public HardwareMonitorReader(IAppLogger logger)
    {
        _logger = logger;
    }

    public ExtraTelemetry Read()
    {
        lock (_sync)
        {
            try
            {
                if (!_opened)
                {
                    _computer.Open();
                    _opened = true;
                }

                UpdateVisitor visitor = new();
                _computer.Accept(visitor);

                List<ISensor> gpuSensors = GetPreferredGpuSensors();
                List<ISensor> memorySensors = GetPhysicalMemorySensors();
                ExtraTelemetry telemetry = new(
                    GpuTemperatureC: FindExact(gpuSensors, SensorType.Temperature, "GPU Core"),
                    GpuLoadPercent: FindExact(gpuSensors, SensorType.Load, "GPU Core"),
                    GpuPowerWatts: FindExact(gpuSensors, SensorType.Power, "GPU Package"),
                    GpuClockMhz: FindExact(gpuSensors, SensorType.Clock, "GPU Core"),
                    MemoryUsedGb: ConvertMemoryToGb(
                        FindExact(memorySensors, SensorType.Data, "Memory Used")),
                    MemoryTotalGb: SumMemory(
                        FindExact(memorySensors, SensorType.Data, "Memory Used"),
                        FindExact(memorySensors, SensorType.Data, "Memory Available")),
                    VramUsedGb: ConvertMemoryToGb(
                        FindExact(gpuSensors, SensorType.SmallData, "GPU Memory Used")),
                    VramTotalGb: ConvertMemoryToGb(
                        FindExact(gpuSensors, SensorType.SmallData, "GPU Memory Total")));

                if (_failureLogged)
                {
                    _logger.Info("LibreHardwareMonitor GPU and memory telemetry recovered.");
                    _failureLogged = false;
                }

                return telemetry;
            }
            catch (Exception exception)
            {
                if (!_failureLogged)
                {
                    _logger.Error("LibreHardwareMonitor GPU or memory enumeration failed", exception);
                    _failureLogged = true;
                }

                return new ExtraTelemetry();
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_opened)
            {
                _computer.Close();
                _opened = false;
            }
        }
    }

    private List<ISensor> GetPreferredGpuSensors()
    {
        foreach (HardwareType type in new[]
                 {
                     HardwareType.GpuNvidia,
                     HardwareType.GpuAmd,
                     HardwareType.GpuIntel
                 })
        {
            List<ISensor> sensors = GetSensors(type);
            if (sensors.Count > 0)
            {
                return sensors;
            }
        }

        return [];
    }

    private List<ISensor> GetPhysicalMemorySensors()
    {
        IHardware? physicalMemory = _computer.Hardware.FirstOrDefault(hardware =>
            hardware.HardwareType == HardwareType.Memory &&
            string.Equals(hardware.Identifier.ToString(), "/ram", StringComparison.OrdinalIgnoreCase));
        physicalMemory ??= _computer.Hardware.FirstOrDefault(hardware =>
            hardware.HardwareType == HardwareType.Memory &&
            !string.Equals(hardware.Identifier.ToString(), "/vram", StringComparison.OrdinalIgnoreCase) &&
            !hardware.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase));
        return physicalMemory is null
            ? []
            : Flatten(physicalMemory).ToList();
    }

    private List<ISensor> GetSensors(params HardwareType[] types) =>
        _computer.Hardware
            .Where(hardware => types.Contains(hardware.HardwareType))
            .SelectMany(Flatten)
            .ToList();

    private static IEnumerable<ISensor> Flatten(IHardware hardware)
    {
        foreach (ISensor sensor in hardware.Sensors)
        {
            yield return sensor;
        }

        foreach (IHardware subHardware in hardware.SubHardware)
        {
            foreach (ISensor sensor in Flatten(subHardware))
            {
                yield return sensor;
            }
        }
    }

    private static double? FindExact(
        IEnumerable<ISensor> sensors,
        SensorType type,
        string name)
    {
        ISensor? sensor = sensors.FirstOrDefault(candidate =>
            candidate.SensorType == type &&
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return sensor?.Value;
    }

    private static double? SumMemory(double? used, double? available)
    {
        double? usedGb = ConvertMemoryToGb(used);
        double? availableGb = ConvertMemoryToGb(available);
        return usedGb.HasValue && availableGb.HasValue
            ? usedGb + availableGb
            : null;
    }

    private static double? ConvertMemoryToGb(double? value)
    {
        if (!value.HasValue || !double.IsFinite(value.Value) || value.Value < 0)
        {
            return null;
        }

        return value.Value > 256 ? value.Value / 1024d : value.Value;
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (IHardware subHardware in hardware.SubHardware)
            {
                subHardware.Accept(this);
            }
        }

        public void VisitSensor(ISensor sensor)
        {
        }

        public void VisitParameter(IParameter parameter)
        {
        }
    }
}

internal sealed record ExtraTelemetry(
    double? GpuTemperatureC = null,
    double? GpuLoadPercent = null,
    double? GpuPowerWatts = null,
    double? GpuClockMhz = null,
    double? MemoryUsedGb = null,
    double? MemoryTotalGb = null,
    double? VramUsedGb = null,
    double? VramTotalGb = null);
