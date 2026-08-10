using PredatorLite.Core.Abstractions;

namespace PredatorLite.Platform.Windows.SystemIntegration;

internal sealed class HardwareMonitorSession
{
    private readonly object _sync = new();
    private readonly IHardwareMonitorReader _reader;
    private readonly IAppLogger _logger;
    private readonly TimeSpan _readTimeout;

    private Task<ExtraTelemetry>? _readTask;
    private int _readTimedOut;
    private bool _disposed;

    public HardwareMonitorSession(
        IHardwareMonitorReader reader,
        IAppLogger logger,
        TimeSpan readTimeout)
    {
        _reader = reader;
        _logger = logger;
        _readTimeout = readTimeout;
    }

    public Task<ExtraTelemetry> ReadAsync(CancellationToken cancellationToken)
    {
        Task<ExtraTelemetry> readTask;
        bool newlyStarted;
        lock (_sync)
        {
            if (_disposed)
            {
                return Task.FromResult(new ExtraTelemetry());
            }

            if (_readTask is null)
            {
                readTask = Task.Run(_reader.Read, CancellationToken.None);
                _readTask = readTask;
                newlyStarted = true;
            }
            else if (!_readTask.IsCompleted)
            {
                return Task.FromResult(new ExtraTelemetry());
            }
            else
            {
                readTask = _readTask;
                _readTask = null;
                newlyStarted = false;
            }
        }

        return AwaitReadAsync(readTask, newlyStarted, cancellationToken);
    }

    public Task DisposeAsync()
    {
        Task<ExtraTelemetry>? readTask;
        lock (_sync)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            _disposed = true;
            readTask = _readTask;
            _readTask = null;
        }

        return Task.Run(() => DisposeCoreAsync(readTask));
    }

    private async Task<ExtraTelemetry> AwaitReadAsync(
        Task<ExtraTelemetry> readTask,
        bool newlyStarted,
        CancellationToken cancellationToken)
    {
        try
        {
            ExtraTelemetry telemetry = newlyStarted
                ? await readTask.WaitAsync(_readTimeout, cancellationToken).ConfigureAwait(false)
                : await readTask.ConfigureAwait(false);
            lock (_sync)
            {
                if (ReferenceEquals(_readTask, readTask))
                {
                    _readTask = null;
                }
            }

            if (Interlocked.Exchange(ref _readTimedOut, 0) != 0)
            {
                _logger.Info("LibreHardwareMonitor telemetry recovered after a timeout.");
            }

            return telemetry;
        }
        catch (TimeoutException)
        {
            if (Interlocked.Exchange(ref _readTimedOut, 1) == 0)
            {
                _logger.LogError(
                    "LibreHardwareMonitor telemetry exceeded the bounded timeout.");
            }

            return new ExtraTelemetry();
        }
    }

    private async Task DisposeCoreAsync(Task<ExtraTelemetry>? readTask)
    {
        try
        {
            if (readTask is not null)
            {
                await readTask.ConfigureAwait(false);
            }
        }
        catch
        {
        }

        try
        {
            _reader.Dispose();
        }
        catch (Exception exception)
        {
            _logger.LogError("LibreHardwareMonitor cleanup failed", exception);
        }
    }
}
