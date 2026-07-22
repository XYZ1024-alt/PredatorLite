using System.IO;
using System.IO.Pipes;
using System.Text;

namespace PredatorLite.App.Services;

public sealed class SingleInstanceService : IAsyncDisposable
{
    private const string MutexName = @"Local\PredatorLite-App";
    private const string PipeName = "PredatorLite-App-Activation";
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _listener;

    public SingleInstanceService()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        IsPrimary = createdNew;
    }

    public bool IsPrimary { get; }

    public void StartListening(Action activationRequested)
    {
        if (!IsPrimary || _listener is not null)
        {
            return;
        }

        _listener = Task.Run(() => ListenAsync(activationRequested, _lifetime.Token));
    }

    public static async Task SignalPrimaryAsync()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                await using NamedPipeClientStream pipe = new(
                    ".",
                    PipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(500));
                await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
                byte[] message = Encoding.UTF8.GetBytes("SHOW\n");
                await pipe.WriteAsync(message, timeout.Token).ConfigureAwait(false);
                await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
                return;
            }
            catch
            {
                await Task.Delay(150).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (IsPrimary)
        {
            // Named mutex ownership is thread-affine, so release before the first await.
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
        if (_listener is not null)
        {
            try
            {
                await _listener.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _lifetime.Dispose();
    }

    private static async Task ListenAsync(Action activationRequested, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using NamedPipeServerStream server = new(
                PipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using StreamReader reader = new(server, Encoding.UTF8, leaveOpen: true);
                string? message = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.Equals(message, "SHOW", StringComparison.Ordinal))
                {
                    activationRequested();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
