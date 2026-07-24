using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using PredatorLite.Core.Abstractions;

namespace PredatorLite.Platform.Windows.SystemIntegration;

public sealed class EtwFpsSource : IFpsSource
{
    private static readonly Guid DxgKrnlProvider = new("802ec45a-1e99-4b83-9920-87c98277ba9d");
    private const ulong PresentKeywords = 0x4000000000000007;

    private readonly ConcurrentQueue<DateTime> _presents = new();
    private readonly IAppLogger _logger;
    private TraceEventSession? _session;
    private Task? _worker;

    public EtwFpsSource(IAppLogger logger)
    {
        _logger = logger;
    }

    public bool IsRunning => _session is not null;

    public double? FramesPerSecond
    {
        get
        {
            if (!IsRunning)
            {
                return null;
            }

            DateTime threshold = DateTime.UtcNow.AddSeconds(-1);
            while (_presents.TryPeek(out DateTime timestamp) && timestamp < threshold)
            {
                _presents.TryDequeue(out _);
            }

            return _presents.Count;
        }
    }

    public Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return Task.FromResult(true);
        }

        try
        {
            string sessionName = $"PredatorLite-Present-{Environment.ProcessId}";
            _session = new TraceEventSession(sessionName) { StopOnDispose = true };
            _session.Source.Dynamic.All += OnTraceEvent;
            _session.EnableProvider(DxgKrnlProvider, TraceEventLevel.Informational, PresentKeywords);
            _worker = Task.Run(() => _session.Source.Process(), CancellationToken.None);
            return Task.FromResult(true);
        }
        catch (Exception exception)
        {
            _logger.LogError("FPS ETW session could not start", exception);
            _session?.Dispose();
            _session = null;
            return Task.FromResult(false);
        }
    }

    public async Task StopAsync()
    {
        TraceEventSession? session = Interlocked.Exchange(ref _session, null);
        if (session is null)
        {
            return;
        }

        session.Dispose();
        if (_worker is not null)
        {
            try
            {
                await _worker.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        _worker = null;
        while (_presents.TryDequeue(out _))
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private void OnTraceEvent(TraceEvent traceEvent)
    {
        if (!traceEvent.EventName.Contains("Present", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        uint foregroundProcess = GetForegroundProcessId();
        if (foregroundProcess != 0 && traceEvent.ProcessID != foregroundProcess)
        {
            return;
        }

        _presents.Enqueue(DateTime.UtcNow);
    }

    private static uint GetForegroundProcessId()
    {
        IntPtr window = GetForegroundWindow();
        return window == IntPtr.Zero ? 0 : GetWindowThreadProcessId(window, out uint processId) == 0 ? 0 : processId;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
