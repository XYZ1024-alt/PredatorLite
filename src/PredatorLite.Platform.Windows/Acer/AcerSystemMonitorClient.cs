using System.Buffers;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using PredatorLite.Core.Abstractions;

namespace PredatorLite.Platform.Windows.Acer;

internal sealed class AcerSystemMonitorClient : IAsyncDisposable
{
    private const int MaxResponseBytes = 64 * 1024;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly IAppLogger _logger;
    private readonly byte[]? _aesKey;
    private readonly byte[] _requestPacket;

    private DateTimeOffset _retryAfter = DateTimeOffset.MinValue;
    private bool _failureLogged;

    public AcerSystemMonitorClient(IAppLogger logger)
    {
        _logger = logger;
        _aesKey = AcerServiceClient.ReadAesKey();
        string json = JsonSerializer.Serialize(
            new AcerMonitorRequest(AcerProtocol.GetMonitorData),
            AcerJsonContext.Default.AcerMonitorRequest);
        _requestPacket = AcerPacketCodec.Encode(AcerProtocol.MonitorPacket, json, _aesKey);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        AcerMonitorTelemetry? telemetry = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return telemetry?.HasPrimaryTelemetry == true;
    }

    public async Task<AcerMonitorTelemetry?> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (DateTimeOffset.UtcNow < _retryAfter)
            {
                return null;
            }

            try
            {
                AcerMonitorTelemetry telemetry = await SendCoreAsync(cancellationToken).ConfigureAwait(false);
                if (_failureLogged)
                {
                    _logger.Info("Acer system monitor telemetry recovered.");
                }

                _failureLogged = false;
                _retryAfter = DateTimeOffset.MinValue;
                return telemetry;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _retryAfter = DateTimeOffset.UtcNow.Add(FailureBackoff);
                if (!_failureLogged)
                {
                    _logger.LogError("Acer system monitor telemetry is unavailable; retrying in 10 seconds", exception);
                    _failureLogged = true;
                }

                return null;
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _requestGate.Dispose();
        return ValueTask.CompletedTask;
    }

    internal static AcerMonitorTelemetry ParseResponse(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        using JsonDocument document = JsonDocument.Parse(raw);
        return ParseResponse(document.RootElement);
    }

    internal static AcerMonitorTelemetry ParseResponse(ReadOnlyMemory<byte> utf8Json)
    {
        using JsonDocument document = JsonDocument.Parse(utf8Json);
        return ParseResponse(document.RootElement);
    }

    private static AcerMonitorTelemetry ParseResponse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Acer system monitor response root is not an object.");
        }

        int result = ReadRequiredInt(root, "result");
        if (result != 0)
        {
            throw new InvalidDataException($"Acer system monitor returned result {result}.");
        }

        string request = ReadRequiredString(root, "request");
        if (!string.Equals(request, AcerProtocol.GetMonitorData, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Acer system monitor response belonged to {request}, expected {AcerProtocol.GetMonitorData}.");
        }

        if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Acer system monitor response does not contain a data object.");
        }

        double? cpuMaxClock = ValidateClock(ReadDouble(data, "CPU_MAX_FREQUENCY"), null);
        double? gpuMaxClock = ValidateClock(ReadDouble(data, "GPU1_MAX_FREQUENCY"), null);
        double? memoryTotalMb = ValidateRange(ReadDouble(data, "RAM_TOTAL"), 128, 2 * 1024 * 1024);
        double? memoryUsagePercent = ValidateRange(ReadDouble(data, "RAM_USAGE"), 0, 100);
        double? memoryTotalGb = memoryTotalMb / 1024d;
        double? memoryUsedGb = memoryTotalMb.HasValue && memoryUsagePercent.HasValue
            ? memoryTotalGb * memoryUsagePercent / 100d
            : null;

        return new AcerMonitorTelemetry(
            CpuTemperatureC: ValidateInteger(ReadDouble(data, "CPU_TEMPERATURE"), 1, 120),
            GpuTemperatureC: ValidateInteger(ReadDouble(data, "GPU1_TEMPERATURE"), 1, 120),
            CpuFanRpm: ValidateInteger(ReadDouble(data, "CPU_FANSPEED"), 0, 20000),
            GpuFanRpm: ValidateInteger(ReadDouble(data, "GPU1_FANSPEED"), 0, 20000),
            CpuLoadPercent: ValidateRange(ReadDouble(data, "CPU_USAGE"), 0, 100),
            GpuLoadPercent: ValidateRange(ReadDouble(data, "GPU1_USAGE"), 0, 100),
            CpuClockMhz: ValidateClock(ReadDouble(data, "CPU_FREQUENCY"), cpuMaxClock),
            GpuClockMhz: ValidateClock(ReadDouble(data, "GPU1_FREQUENCY"), gpuMaxClock),
            MemoryUsedGb: memoryUsedGb,
            MemoryTotalGb: memoryTotalGb);
    }

    private async Task<AcerMonitorTelemetry> SendCoreAsync(CancellationToken cancellationToken)
    {
        using TcpClient client = new();
        using CancellationTokenSource connectTimeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(ConnectTimeout);
        try
        {
            await client.ConnectAsync("127.0.0.1", AcerProtocol.TelemetryPort, connectTimeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Acer system monitor connection timed out.");
        }

        await using NetworkStream stream = client.GetStream();
        using CancellationTokenSource requestTimeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(RequestTimeout);

        try
        {
            await stream.WriteAsync(_requestPacket, requestTimeout.Token).ConfigureAwait(false);

            byte[] buffer = ArrayPool<byte>.Shared.Rent(MaxResponseBytes);
            try
            {
                int total = 0;
                while (total < MaxResponseBytes)
                {
                    int read = await stream.ReadAsync(
                            buffer.AsMemory(total, MaxResponseBytes - total),
                            requestTimeout.Token)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                    if (TryParseResponse(
                        buffer.AsMemory(0, total),
                        _aesKey,
                        out AcerMonitorTelemetry? telemetry))
                    {
                        return telemetry!;
                    }
                }

                if (total == 0)
                {
                    throw new IOException(
                        "Acer system monitor closed the connection without a response.");
                }

                throw new InvalidDataException(
                    "Acer system monitor returned an incomplete or oversized response.");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Acer system monitor request timed out.");
        }
    }

    internal static bool TryParseResponse(
        ReadOnlyMemory<byte> packet,
        byte[]? aesKey,
        out AcerMonitorTelemetry? telemetry)
    {
        telemetry = null;
        int payloadOffset = AcerPacketCodec.GetPayloadOffset(packet.Span);
        ReadOnlyMemory<byte> payload = packet[payloadOffset..];
        if (aesKey is null)
        {
            try
            {
                telemetry = ParseResponse(TrimTrailingZeros(payload));
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        byte[] plaintext = ArrayPool<byte>.Shared.Rent(payload.Length);
        try
        {
            using Aes aes = Aes.Create();
            aes.Key = aesKey;
            if (!aes.TryDecryptEcb(
                payload.Span,
                plaintext,
                PaddingMode.PKCS7,
                out int plaintextLength))
            {
                return false;
            }

            ReadOnlyMemory<byte> utf8Json =
                TrimTrailingZeros(plaintext.AsMemory(0, plaintextLength));
            telemetry = ParseResponse(utf8Json);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or CryptographicException)
        {
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(plaintext);
        }
    }

    private static ReadOnlyMemory<byte> TrimTrailingZeros(ReadOnlyMemory<byte> value)
    {
        ReadOnlySpan<byte> span = value.Span;
        int length = span.Length;
        while (length > 0 && span[length - 1] == 0)
        {
            length--;
        }

        return value[..length];
    }

    private static int ReadRequiredInt(JsonElement element, string propertyName)
    {
        double? value = ReadDouble(element, propertyName);
        if (!value.HasValue ||
            value.Value < int.MinValue ||
            value.Value > int.MaxValue ||
            value.Value != Math.Truncate(value.Value))
        {
            throw new InvalidDataException(
                $"Acer system monitor response has an invalid {propertyName} value.");
        }

        return (int)value.Value;
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException(
                $"Acer system monitor response has an invalid {propertyName} value.");
        }

        return property.GetString()!;
    }

    private static double? ReadDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        double value = 0;
        bool parsed = property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetDouble(out value),
            JsonValueKind.String => double.TryParse(
                property.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value),
            _ => false
        };
        return parsed && double.IsFinite(value) ? value : null;
    }

    private static int? ValidateInteger(double? value, int minimum, int maximum)
    {
        double? validated = ValidateRange(value, minimum, maximum);
        return validated.HasValue ? (int)Math.Round(validated.Value) : null;
    }

    private static double? ValidateClock(double? value, double? maximum)
    {
        double? validated = ValidateRange(value, 0, 10000);
        if (!validated.HasValue ||
            maximum is > 0 &&
            validated.Value > maximum.Value * 1.25)
        {
            return null;
        }

        return validated;
    }

    private static double? ValidateRange(double? value, double minimum, double maximum) =>
        value is not null && double.IsFinite(value.Value) && value.Value >= minimum && value.Value <= maximum
            ? value
            : null;
}

internal sealed record AcerMonitorTelemetry(
    int? CpuTemperatureC = null,
    int? GpuTemperatureC = null,
    int? CpuFanRpm = null,
    int? GpuFanRpm = null,
    double? CpuLoadPercent = null,
    double? GpuLoadPercent = null,
    double? CpuClockMhz = null,
    double? GpuClockMhz = null,
    double? MemoryUsedGb = null,
    double? MemoryTotalGb = null)
{
    public bool HasPrimaryTelemetry =>
        CpuTemperatureC.HasValue ||
        GpuTemperatureC.HasValue ||
        CpuFanRpm.HasValue ||
        GpuFanRpm.HasValue ||
        CpuLoadPercent.HasValue ||
        GpuLoadPercent.HasValue ||
        CpuClockMhz.HasValue ||
        GpuClockMhz.HasValue;
}
