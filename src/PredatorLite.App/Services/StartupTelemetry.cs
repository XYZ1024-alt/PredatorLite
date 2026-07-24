using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO.Pipes;
using System.Threading.Channels;

namespace PredatorLite.App.Services;

internal static class StartupTelemetry
{
    private static long _startedTimestamp;
    private static StartupPipeReporter? _pipeReporter;
    private static int _started;

    public static long ElapsedMilliseconds =>
        (long)Stopwatch.GetElapsedTime(Volatile.Read(ref _startedTimestamp)).TotalMilliseconds;

    public static void Start(string[] arguments)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _startedTimestamp, Stopwatch.GetTimestamp());
        _pipeReporter = StartupPipeReporter.TryCreate(arguments);
        Mark("process-start");
    }

    public static void Mark(string name)
    {
        long timestamp = Stopwatch.GetTimestamp();
        if (StartupEventSource.Log.IsEnabled())
        {
            StartupEventSource.Log.Milestone(
                name,
                (long)Stopwatch.GetElapsedTime(Volatile.Read(ref _startedTimestamp), timestamp)
                    .TotalMilliseconds);
        }

        _pipeReporter?.Report(name, timestamp);
    }
}

[EventSource(Name = "PredatorLite-Startup")]
internal sealed class StartupEventSource : EventSource
{
    public static readonly StartupEventSource Log = new();

    [Event(1, Level = EventLevel.Informational, Message = "{0} at {1} ms")]
    public void Milestone(string name, long elapsedMilliseconds) =>
        WriteEvent(1, name, elapsedMilliseconds);
}

internal sealed class StartupPipeReporter
{
    private const string ArgumentPrefix = "--startup-pipe=";

    private readonly string _pipeName;
    private readonly Channel<string> _messages = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    private StartupPipeReporter(string pipeName)
    {
        _pipeName = pipeName;
        _ = Task.Run(WriteMessagesAsync);
    }

    public static StartupPipeReporter? TryCreate(string[] arguments)
    {
        string? pipeName = arguments
            .FirstOrDefault(argument => argument.StartsWith(ArgumentPrefix, StringComparison.OrdinalIgnoreCase))?
            [ArgumentPrefix.Length..];
        return string.IsNullOrWhiteSpace(pipeName) ? null : new StartupPipeReporter(pipeName);
    }

    public void Report(string name, long timestamp)
    {
        _messages.Writer.TryWrite(string.Create(
            CultureInfo.InvariantCulture,
            $"{name}\t{timestamp}\t{Stopwatch.Frequency}\t{Environment.ProcessId}\t{Environment.Version}"));
    }

    private async Task WriteMessagesAsync()
    {
        try
        {
            await using NamedPipeClientStream pipe = new(
                ".",
                _pipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await using StreamWriter writer = new(pipe) { AutoFlush = true };
            await foreach (string message in _messages.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                await writer.WriteLineAsync(message).ConfigureAwait(false);
            }
        }
        catch
        {
        }
    }
}
