using System.Text;
using BenchmarkDotNet.Attributes;
using PredatorLite.Platform.Windows.Acer;

namespace PredatorLite.Benchmarks;

[MemoryDiagnoser]
public class AcerPacketCodecBenchmarks
{
    private readonly string _payload =
        "{\"Function\":\"OPERATING_MODE\",\"Parameter\":{\"mode\":4}}";

    private readonly byte[] _encoded;
    private readonly byte[] _encryptedKey = Encoding.ASCII.GetBytes("A6052DC8A6E44210");

    public AcerPacketCodecBenchmarks()
    {
        _encoded = AcerPacketCodec.Encode(20, _payload);
    }

    [Benchmark]
    public byte[] EncodePlain() => AcerPacketCodec.Encode(20, _payload);

    [Benchmark]
    public string DecodePlain() => AcerPacketCodec.Decode(_encoded);

    [Benchmark]
    public byte[] EncodeEncrypted() => AcerPacketCodec.Encode(20, _payload, _encryptedKey);
}
