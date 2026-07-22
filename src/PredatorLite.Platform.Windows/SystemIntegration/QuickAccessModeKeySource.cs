using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PredatorLite.Core.Abstractions;

namespace PredatorLite.Platform.Windows.SystemIntegration;

public sealed class QuickAccessModeKeySource : IModeKeySource
{
    private static readonly Uri Endpoint = new("wss://localhost:5141/");
    private const string HandshakeQuestion = "AcerQuickAccessFunctionalityTests_HandshakingQuestion";
    private const string ClientKey = "A6052DC8A6E44210";
    private const string ServerKey = "AB252AB73BED1CDB";

    private readonly IAppLogger _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _worker;
    private DateTimeOffset _lastEvent = DateTimeOffset.MinValue;

    public QuickAccessModeKeySource(IAppLogger logger)
    {
        _logger = logger;
    }

    public event EventHandler? ModeKeyPressed;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _worker ??= Task.Run(() => RunAsync(_lifetime.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_worker is not null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _lifetime.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using ClientWebSocket socket = new();
                socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                await socket.ConnectAsync(Endpoint, cancellationToken).ConfigureAwait(false);
                await AuthenticateAsync(socket, cancellationToken).ConfigureAwait(false);
                string handshake = await ReceiveAsync(socket, cancellationToken).ConfigureAwait(false);
                if (!ValidateHandshake(handshake))
                {
                    throw new InvalidDataException("Quick Access rejected the local handshake.");
                }

                await SendAsync(socket, new
                {
                    PacketType = 2,
                    Version = 1,
                    Session = Guid.NewGuid().ToString(),
                    Command = "FunctionQuery"
                }, cancellationToken).ConfigureAwait(false);

                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    string message = await ReceiveAsync(socket, cancellationToken).ConfigureAwait(false);
                    HandleMessage(message);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.Error("Quick Access mode-key connection failed", exception);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task AuthenticateAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        string data = EncryptToBase64(
            JsonSerializer.Serialize(new { Question = HandshakeQuestion, Key = ServerKey }),
            ClientKey);
        await SendAsync(socket, new
        {
            PacketType = 1,
            Version = 1,
            Session = Guid.NewGuid().ToString(),
            Data = data
        }, cancellationToken).ConfigureAwait(false);
    }

    private static bool ValidateHandshake(string message)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(message);
            JsonElement root = document.RootElement;
            return root.TryGetProperty("PacketType", out JsonElement packetType) &&
                packetType.GetInt32() == 1 &&
                root.TryGetProperty("Data", out JsonElement data) &&
                string.Equals(
                    data.GetString(),
                    EncryptToBase64(HandshakeQuestion, ServerKey),
                    StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private void HandleMessage(string message)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(message);
            JsonElement root = document.RootElement;
            string? action = root.TryGetProperty("Action", out JsonElement actionElement)
                ? actionElement.GetString()
                : null;
            string? command = root.TryGetProperty("Command", out JsonElement commandElement)
                ? commandElement.GetString()
                : null;
            if (!string.Equals(action, "Notify", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(command, "KeyEvent", StringComparison.OrdinalIgnoreCase) ||
                DateTimeOffset.Now - _lastEvent < TimeSpan.FromMilliseconds(250))
            {
                return;
            }

            _lastEvent = DateTimeOffset.Now;
            ModeKeyPressed?.Invoke(this, EventArgs.Empty);
        }
        catch (JsonException exception)
        {
            _logger.Error("Quick Access returned invalid JSON", exception);
        }
    }

    private static async Task SendAsync(
        ClientWebSocket socket,
        object payload,
        CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReceiveAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[8192];
        using MemoryStream message = new();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new EndOfStreamException("Quick Access closed the websocket.");
            }

            message.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(message.ToArray());
    }

    private static string EncryptToBase64(string value, string key)
    {
        using Aes aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(key);
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        using ICryptoTransform transform = aes.CreateEncryptor();
        byte[] plain = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(transform.TransformFinalBlock(plain, 0, plain.Length));
    }
}
