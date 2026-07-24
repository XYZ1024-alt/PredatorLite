using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using PredatorLite.Core.Abstractions;

namespace PredatorLite.Platform.Windows.Acer;

public sealed class AcerServiceClient : IAsyncDisposable
{
    private const int MaxResponseBytes = 64 * 1024;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly IAppLogger _logger;
    private readonly byte[]? _aesKey;

    public AcerServiceClient(IAppLogger logger)
    {
        _logger = logger;
        _aesKey = ReadAesKey();
    }

    public Task<AcerResponse> QueryAsync(string function, CancellationToken cancellationToken = default) =>
        SendAsync(AcerProtocol.QueryPacket, function, null, cancellationToken);

    public Task<AcerResponse> SetAsync(
        string function,
        JsonObject parameters,
        CancellationToken cancellationToken = default) =>
        SendAsync(AcerProtocol.SetPacket, function, parameters, cancellationToken);

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            AcerResponse response = await QueryAsync(AcerProtocol.OperatingMode, cancellationToken).ConfigureAwait(false);
            return response.IsSuccess;
        }
        catch
        {
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        _commandGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<AcerResponse> SendAsync(
        uint packetId,
        string function,
        JsonObject? parameters,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(function);
        await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Exception? lastError = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    AcerResponse response = await SendCoreAsync(
                        packetId,
                        function,
                        parameters,
                        cancellationToken).ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(response.Request) &&
                        !string.Equals(response.Request, function, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            $"AcerService response belonged to {response.Request}, expected {function}.");
                    }

                    return response;
                }
                catch (Exception exception) when (exception is IOException or SocketException or JsonException or CryptographicException)
                {
                    lastError = exception;
                    _logger.LogError($"AcerService {function} attempt {attempt + 1} failed", exception);
                    if (attempt == 0)
                    {
                        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            throw new IOException($"AcerService request {function} failed.", lastError);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async Task<AcerResponse> SendCoreAsync(
        uint packetId,
        string function,
        JsonObject? parameters,
        CancellationToken cancellationToken)
    {
        JsonObject request = new() { ["Function"] = function };
        if (parameters is not null)
        {
            request["Parameter"] = parameters.DeepClone();
        }

        byte[] packet = AcerPacketCodec.Encode(packetId, request.ToJsonString(), _aesKey);
        using TcpClient client = new();
        using CancellationTokenSource connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(ConnectTimeout);
        await client.ConnectAsync("127.0.0.1", AcerProtocol.CommandPort, connectTimeout.Token).ConfigureAwait(false);

        await using NetworkStream stream = client.GetStream();
        using CancellationTokenSource requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(RequestTimeout);
        await stream.WriteAsync(packet, requestTimeout.Token).ConfigureAwait(false);

        byte[] buffer = new byte[MaxResponseBytes];
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), requestTimeout.Token)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (TryParseResponse(buffer.AsSpan(0, total), out AcerResponse? response))
            {
                return response!;
            }
        }

        if (total == 0)
        {
            throw new IOException("AcerService closed the connection without a response.");
        }

        throw new InvalidDataException("AcerService returned an incomplete or invalid response.");
    }

    private bool TryParseResponse(ReadOnlySpan<byte> packet, out AcerResponse? response)
    {
        response = null;
        try
        {
            string raw = AcerPacketCodec.Decode(packet, _aesKey);
            using JsonDocument document = JsonDocument.Parse(raw);
            JsonElement root = document.RootElement;
            int result = ReadInt(root, "result", -1);
            string responseFunction = root.TryGetProperty("request", out JsonElement requestElement)
                ? requestElement.GetString() ?? string.Empty
                : string.Empty;
            JsonElement? data = root.TryGetProperty("data", out JsonElement dataElement)
                ? dataElement.Clone()
                : null;

            response = new AcerResponse(result, responseFunction, data, raw);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or CryptographicException)
        {
            return false;
        }
    }

    private static int ReadInt(JsonElement element, string name, int fallback)
    {
        if (!element.TryGetProperty(name, out JsonElement property))
        {
            return fallback;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out int value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), out int value) => value,
            _ => fallback
        };
    }

    internal static byte[]? ReadAesKey()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Acer\XSense");
            if (key?.GetValue("AESkey") is string value && value.Length is 16 or 24 or 32)
            {
                return Encoding.ASCII.GetBytes(value);
            }
        }
        catch
        {
        }

        return null;
    }
}
