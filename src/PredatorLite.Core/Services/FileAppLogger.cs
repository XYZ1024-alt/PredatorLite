using System.Threading.Channels;
using PredatorLite.Core.Abstractions;

namespace PredatorLite.Core.Services;

public sealed class FileAppLogger : IAppLogger
{
    private readonly Channel<LogEntry> _entries = Channel.CreateUnbounded<LogEntry>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private readonly object _lifetimeSync = new();
    private Task? _writerTask;
    private readonly string _userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private int _disposed;

    public FileAppLogger(string? directory = null)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        LogDirectory = directory ?? Path.Combine(appData, "PredatorLite", "Logs");
    }

    public string LogDirectory { get; }

    public void Info(string message) => Enqueue("INFO", message, null);

    public void LogError(string message, Exception? exception = null) =>
        Enqueue("ERROR", message, exception);

    public void Dispose()
    {
        Task? writerTask;
        lock (_lifetimeSync)
        {
            if (_disposed != 0)
            {
                return;
            }

            _disposed = 1;
            _entries.Writer.TryComplete();
            writerTask = _writerTask;
        }

        if (writerTask is null)
        {
            GC.SuppressFinalize(this);
            return;
        }

        try
        {
            writerTask.GetAwaiter().GetResult();
        }
        catch
        {
        }

        GC.SuppressFinalize(this);
    }

    private void Enqueue(string level, string message, Exception? exception)
    {
        lock (_lifetimeSync)
        {
            if (_disposed != 0)
            {
                return;
            }

            _writerTask ??= Task.Run(ProcessEntriesAsync);
            _entries.Writer.TryWrite(new LogEntry(DateTimeOffset.Now, level, message, exception));
        }
    }

    private async Task ProcessEntriesAsync()
    {
        StreamWriter? writer = null;
        DateOnly writerDate = default;
        try
        {
            Directory.CreateDirectory(LogDirectory);
            DeleteExpiredLogs();
            await foreach (LogEntry entry in _entries.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    DateOnly entryDate = DateOnly.FromDateTime(entry.Timestamp.UtcDateTime);
                    if (writer is null || entryDate != writerDate)
                    {
                        if (writer is not null)
                        {
                            await writer.DisposeAsync().ConfigureAwait(false);
                        }

                        writer = CreateWriter(entryDate);
                        writerDate = entryDate;
                    }

                    await writer.WriteLineAsync(Format(entry)).ConfigureAwait(false);
                    while (_entries.Reader.TryRead(out LogEntry queued))
                    {
                        DateOnly queuedDate = DateOnly.FromDateTime(queued.Timestamp.UtcDateTime);
                        if (queuedDate != writerDate)
                        {
                            await writer.DisposeAsync().ConfigureAwait(false);
                            writer = CreateWriter(queuedDate);
                            writerDate = queuedDate;
                        }

                        await writer.WriteLineAsync(Format(queued)).ConfigureAwait(false);
                    }

                    await writer.FlushAsync().ConfigureAwait(false);
                }
                catch
                {
                    if (writer is not null)
                    {
                        await writer.DisposeAsync().ConfigureAwait(false);
                        writer = null;
                    }
                }
            }
        }
        catch
        {
        }
        finally
        {
            if (writer is not null)
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private StreamWriter CreateWriter(DateOnly date)
    {
        string path = Path.Combine(LogDirectory, $"PredatorLite-{date:yyyyMMdd}.log");
        FileStream stream = new(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return new StreamWriter(stream);
    }

    private string Format(LogEntry entry)
    {
        string message = entry.Exception is null
            ? entry.Message
            : $"{entry.Message}: {entry.Exception}";
        return $"{entry.Timestamp:O} [{entry.Level}] {Sanitize(message)}";
    }

    private string Sanitize(string value) =>
        string.IsNullOrWhiteSpace(_userProfile)
            ? value
            : value.Replace(_userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);

    private void DeleteExpiredLogs()
    {
        try
        {
            DateTime threshold = DateTime.UtcNow.AddDays(-7);
            foreach (string file in Directory.EnumerateFiles(LogDirectory, "PredatorLite-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < threshold)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
        }
    }

    private readonly record struct LogEntry(
        DateTimeOffset Timestamp,
        string Level,
        string Message,
        Exception? Exception);
}
