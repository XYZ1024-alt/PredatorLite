using LibreHardwareMonitor.Hardware;

namespace PredatorLite.Platform.Windows.SystemIntegration;

internal sealed class HardwareMonitorReader : IDisposable
{
    private readonly object _sync = new();
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true
    };
    private bool _opened;

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

                List<ISensor> cpuSensors = GetSensors(HardwareType.Cpu);
                List<ISensor> gpuSensors = GetSensors(HardwareType.GpuNvidia, HardwareType.GpuIntel, HardwareType.GpuAmd);
                List<ISensor> memorySensors = GetSensors(HardwareType.Memory);

                return new ExtraTelemetry(
                    CpuTemperatureC: Find(cpuSensors, SensorType.Temperature, "Package"),
                    GpuTemperatureC: Find(gpuSensors, SensorType.Temperature, "Core"),
                    CpuLoadPercent: Find(cpuSensors, SensorType.Load, "Total"),
                    GpuLoadPercent: Find(gpuSensors, SensorType.Load, "Core"),
                    CpuPowerWatts: Find(cpuSensors, SensorType.Power, "Package"),
                    GpuPowerWatts: Find(gpuSensors, SensorType.Power, "Package", "GPU"),
                    CpuClockMhz: Average(cpuSensors, SensorType.Clock, "Core"),
                    GpuClockMhz: Find(gpuSensors, SensorType.Clock, "Core"),
                    MemoryUsedGb: ConvertMemoryToGb(Find(memorySensors, SensorType.Data, "Used")),
                    MemoryTotalGb: ConvertMemoryToGb(Find(memorySensors, SensorType.Data, "Available") +
                        Find(memorySensors, SensorType.Data, "Used")),
                    VramUsedGb: ConvertMemoryToGb(Find(gpuSensors, SensorType.SmallData, "Used")),
                    VramTotalGb: ConvertMemoryToGb(Find(gpuSensors, SensorType.SmallData, "Total")));
            }
            catch
            {
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

    private static double? Find(
        IEnumerable<ISensor> sensors,
        SensorType type,
        params string[] nameFragments)
    {
        ISensor? sensor = sensors.FirstOrDefault(candidate =>
            candidate.SensorType == type &&
            nameFragments.Any(fragment => candidate.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
        return sensor?.Value;
    }

    private static double? Average(IEnumerable<ISensor> sensors, SensorType type, string nameFragment)
    {
        float[] values = sensors
            .Where(sensor =>
                sensor.SensorType == type &&
                sensor.Name.Contains(nameFragment, StringComparison.OrdinalIgnoreCase) &&
                sensor.Value.HasValue)
            .Select(sensor => sensor.Value!.Value)
            .ToArray();
        return values.Length == 0 ? null : values.Average();
    }

    private static double? ConvertMemoryToGb(double? value)
    {
        if (!value.HasValue)
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
    double? CpuTemperatureC = null,
    double? GpuTemperatureC = null,
    double? CpuLoadPercent = null,
    double? GpuLoadPercent = null,
    double? CpuPowerWatts = null,
    double? GpuPowerWatts = null,
    double? CpuClockMhz = null,
    double? GpuClockMhz = null,
    double? MemoryUsedGb = null,
    double? MemoryTotalGb = null,
    double? VramUsedGb = null,
    double? VramTotalGb = null);
