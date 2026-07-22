using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using PredatorLite.Platform.Windows.Acer;

namespace PredatorLite.Tests;

public sealed class AcerPacketCodecTests
{
    [Fact]
    public void EncodeAndDecodePlainPacketPreservesPayloadAndHeader()
    {
        const string json = "{\"Function\":\"OPERATING_MODE\"}";

        byte[] packet = AcerPacketCodec.Encode(20, json);

        Assert.Equal("ACER", Encoding.ASCII.GetString(packet, 0, 4));
        Assert.Equal((uint)20, BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(4, 4)));
        Assert.Equal(json, AcerPacketCodec.Decode(packet));
    }

    [Fact]
    public void EncodeAndDecodeEncryptedPacketRoundTrips()
    {
        byte[] key = Encoding.ASCII.GetBytes("A6052DC8A6E44210");
        const string json = "{\"Function\":\"GPU_MODE\",\"Parameter\":{\"mode\":2}}";

        byte[] packet = AcerPacketCodec.Encode(100, json, key);

        Assert.NotEqual(json, Encoding.UTF8.GetString(packet.AsSpan(8)));
        Assert.Equal(json, AcerPacketCodec.Decode(packet, key));
    }

    [Fact]
    public void DecodeAcceptsPayloadWithoutAcerHeader()
    {
        byte[] payload = Encoding.UTF8.GetBytes("{\"result\":0}\0\0");

        Assert.Equal("{\"result\":0}", AcerPacketCodec.Decode(payload));
    }

    [Fact]
    public void InvalidAesKeyIsRejected()
    {
        Assert.Throws<CryptographicException>(() =>
            AcerPacketCodec.Encode(20, "{}", [1, 2, 3]));
    }
}
