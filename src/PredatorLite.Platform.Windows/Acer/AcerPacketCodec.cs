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
        if (aesKey is null)
        {
            byte[] packet = new byte[8 + Encoding.UTF8.GetByteCount(json)];
            WriteHeader(packet, packetId);
            Encoding.UTF8.GetBytes(json.AsSpan(), packet.AsSpan(8));
            return packet;
        }

        byte[] payload = TransformAes(Encoding.UTF8.GetBytes(json), aesKey, encrypt: true);
        byte[] encryptedPacket = new byte[8 + payload.Length];
        WriteHeader(encryptedPacket, packetId);
        payload.CopyTo(encryptedPacket, 8);
        return encryptedPacket;
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

        byte[]? decrypted = aesKey is null
            ? null
            : TransformAes(payload.ToArray(), aesKey, encrypt: false);
        ReadOnlySpan<byte> plain = decrypted is null ? payload : decrypted;
        while (!plain.IsEmpty && plain[^1] == 0)
        {
            plain = plain[..^1];
        }

        return Encoding.UTF8.GetString(plain);
    }

    private static void WriteHeader(Span<byte> packet, uint packetId)
    {
        Magic.CopyTo(packet);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(4, 4), packetId);
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
