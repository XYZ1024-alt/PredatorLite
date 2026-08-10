using System.Text;
using BenchmarkDotNet.Attributes;
using PredatorLite.Platform.Windows.Acer;

namespace PredatorLite.Benchmarks;

[MemoryDiagnoser]
public class AcerSystemMonitorBenchmarks
{
    private const string Response = """
        {
          "result": 0,
          "request": "GET_MONITOR_DATA",
          "data": {
            "CPU_TEMPERATURE": 72,
            "GPU1_TEMPERATURE": 57,
            "CPU_FANSPEED": 3816,
            "GPU1_FANSPEED": 3871,
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

    private readonly byte[] _aesKey = Encoding.ASCII.GetBytes("A6052DC8A6E44210");
    private readonly byte[] _encryptedPacket;
    private readonly byte[] _plainPacket;

    public AcerSystemMonitorBenchmarks()
    {
        _plainPacket = AcerPacketCodec.Encode(24, Response);
        _encryptedPacket = AcerPacketCodec.Encode(24, Response, _aesKey);
    }

    [Benchmark(Baseline = true)]
    public int ParsePlain()
    {
        AcerSystemMonitorClient.TryParseResponse(
            _plainPacket,
            aesKey: null,
            out AcerMonitorTelemetry? telemetry);
        return telemetry?.CpuTemperatureC ?? 0;
    }

    [Benchmark]
    public int ParseEncrypted()
    {
        AcerSystemMonitorClient.TryParseResponse(
            _encryptedPacket,
            _aesKey,
            out AcerMonitorTelemetry? telemetry);
        return telemetry?.CpuTemperatureC ?? 0;
    }
}
