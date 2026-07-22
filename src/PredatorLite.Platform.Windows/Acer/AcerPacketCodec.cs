using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PredatorLite.Platform.Windows.Acer;

public static class AcerPacketCodec
{
    private static ReadOnlySpan<byte> Magic => "ACER"u8;

    public static byte[] Encode(uint packetId, string json, byte[]? aesKey = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        byte[] payload = aesKey is null
            ? Encoding.UTF8.GetBytes(json)
            : TransformAes(Encoding.UTF8.GetBytes(json), aesKey, encrypt: true);

        byte[] packet = new byte[8 + payload.Length];
        Magic.CopyTo(packet);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), packetId);
        payload.CopyTo(packet, 8);
        return packet;
    }

    public static string Decode(ReadOnlySpan<byte> packet, byte[]? aesKey = null)
    {
        if (packet.IsEmpty)
        {
            throw new InvalidDataException("AcerService returned an empty response.");
        }

        ReadOnlySpan<byte> payload = packet.Length >= 8 && packet[..4].SequenceEqual(Magic)
            ? packet[8..]
            : packet;

        byte[] plain = aesKey is null
            ? payload.ToArray()
            : TransformAes(payload.ToArray(), aesKey, encrypt: false);

        return Encoding.UTF8.GetString(plain).TrimEnd('\0');
    }

    private static byte[] TransformAes(byte[] input, byte[] key, bool encrypt)
    {
        if (key.Length is not (16 or 24 or 32))
        {
            throw new CryptographicException("AcerService AES key length is invalid.");
        }

        using Aes aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        using ICryptoTransform transform = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor();
        return transform.TransformFinalBlock(input, 0, input.Length);
    }
}
