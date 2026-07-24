using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using PredatorLite.Core.Abstractions;

namespace PredatorLite.App.Services;

public sealed class FanGuardClient : IAsyncDisposable
{
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Process? _process;
    private CancellationTokenSource? _heartbeatCancellation;
    private Task? _heartbeatTask;

    public FanGuardClient(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsActive => _pipe?.IsConnected == true && _process?.HasExited == false;

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsActive)
            {
                return true;
            }

            await StopCoreAsync(sendStop: false).ConfigureAwait(false);
            string? executable = CompanionExecutableLocator.Find("PredatorLite.FanGuard.exe");
            if (executable is null)
            {
                _logger.LogError("FanGuard executable was not found.");
                return false;
            }

            string pipeName = $"PredatorLite-FanGuard-{Environment.ProcessId}-{Guid.NewGuid():N}";
            _process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"--pipe \"{pipeName}\" --parent {Environment.ProcessId}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (_process is null)
            {
                return false;
            }

            _pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(7));
            await _pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);

            _reader = new StreamReader(_pipe, leaveOpen: true);
            _writer = new StreamWriter(_pipe, leaveOpen: true) { AutoFlush = true };
            string? ready = await _reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (!string.Equals(ready, "READY", StringComparison.Ordinal))
            {
                throw new InvalidDataException("FanGuard did not complete its handshake.");
            }

            _heartbeatCancellation = new CancellationTokenSource();
            _heartbeatTask = RunHeartbeatAsync(_heartbeatCancellation.Token);
            _logger.Info("FanGuard started.");
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError("FanGuard could not start", exception);
            await StopCoreAsync(sendStop: false).ConfigureAwait(false);
            return false;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync(sendStop: true).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifecycleGate.Dispose();
        _sendGate.Dispose();
    }

    private async Task RunHeartbeatAsync(CancellationToken cancellationToken)
    {
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await SendAsync("PING", cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError("FanGuard heartbeat failed", exception);
        }
    }

    private async Task SendAsync(string message, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_writer is not null && _pipe?.IsConnected == true)
            {
                await _writer.WriteLineAsync(message.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task StopCoreAsync(bool sendStop)
    {
        CancellationTokenSource? heartbeatCancellation = _heartbeatCancellation;
        Task? heartbeatTask = _heartbeatTask;
        _heartbeatCancellation = null;
        _heartbeatTask = null;
        heartbeatCancellation?.Cancel();
        if (heartbeatTask is not null)
        {
            try
            {
                await heartbeatTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        heartbeatCancellation?.Dispose();
        if (sendStop)
        {
            try
            {
                await SendAsync("STOP", CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        _writer?.Dispose();
        _reader?.Dispose();
        _pipe?.Dispose();
        _writer = null;
        _reader = null;
        _pipe = null;

        if (_process is not null)
        {
            try
            {
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(4)).ConfigureAwait(false);
            }
            catch
            {
            }

            _process.Dispose();
            _process = null;
        }
    }
}
