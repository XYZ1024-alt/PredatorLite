using System.Text;
using System.Threading.Channels;
using PredatorLite.Core.Abstractions;

namespace PredatorLite.Core.Services;

public sealed class FileAppLogger : IAppLogger
{
    private const int DefaultQueueCapacity = 1024;
    private const int DefaultMaxEntryChars = 32 * 1024;
    private const int DefaultMaxRetainedFiles = 14;
    private const long DefaultMaxFileBytes = 5L * 1024 * 1024;
    private const long DefaultMaxTotalBytes = 20L * 1024 * 1024;
    private static readonly TimeSpan WriterShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly Channel<LogEntry> _entries;
    private readonly object _lifetimeSync = new();
    private readonly string _userProfile =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private readonly int _maxEntryChars;
    private readonly int _maxRetainedFiles;
    private readonly long _maxFileBytes;
    private readonly long _maxTotalBytes;
    private readonly Task? _writerStartBarrier;

    private Task? _writerTask;
    private long _droppedEntries;
    private int _disposed;

    public FileAppLogger(string? directory = null)
        : this(
            directory,
            DefaultQueueCapacity,
            DefaultMaxEntryChars,
            DefaultMaxRetainedFiles,
            DefaultMaxFileBytes,
            DefaultMaxTotalBytes,
            writerStartBarrier: null)
    {
    }

    internal FileAppLogger(
        string? directory,
        int queueCapacity,
        int maxEntryChars,
        int maxRetainedFiles,
        long maxFileBytes,
        long maxTotalBytes,
        Task? writerStartBarrier = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(queueCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntryChars);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRetainedFiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTotalBytes);
        if (maxFileBytes > maxTotalBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxFileBytes),
                "The per-file limit cannot exceed the total log storage limit.");
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        LogDirectory = directory ?? Path.Combine(appData, "PredatorLite", "Logs");
        _maxEntryChars = maxEntryChars;
        _maxRetainedFiles = maxRetainedFiles;
        _maxFileBytes = maxFileBytes;
        _maxTotalBytes = maxTotalBytes;
        _writerStartBarrier = writerStartBarrier;
        _entries = Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(queueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
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

        if (writerTask is not null)
        {
            try
            {
                writerTask.Wait(WriterShutdownTimeout);
            }
            catch
            {
            }
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
            if (!_entries.Writer.TryWrite(
                new LogEntry(DateTimeOffset.Now, level, message, exception)))
            {
                Interlocked.Increment(ref _droppedEntries);
            }
        }
    }

    private async Task ProcessEntriesAsync()
    {
        LogFileWriter writer = new(this);
        try
        {
            if (_writerStartBarrier is not null)
            {
                await _writerStartBarrier.ConfigureAwait(false);
            }

            Directory.CreateDirectory(LogDirectory);
            DeleteExpiredLogs();
            PruneLogs();
            await foreach (LogEntry entry in _entries.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    await writer.WriteAsync(entry).ConfigureAwait(false);
                    while (_entries.Reader.TryRead(out LogEntry queued))
                    {
                        await writer.WriteAsync(queued).ConfigureAwait(false);
                    }

                    long droppedEntries = Interlocked.Exchange(ref _droppedEntries, 0);
                    if (droppedEntries > 0)
                    {
                        await writer.WriteAsync(new LogEntry(
                            DateTimeOffset.Now,
                            "WARN",
                            $"Dropped {droppedEntries} log entries because the bounded queue was full.",
                            Exception: null)).ConfigureAwait(false);
                    }

                    await writer.FlushAsync().ConfigureAwait(false);
                }
                catch
                {
                    await writer.ResetAsync().ConfigureAwait(false);
                }
            }
        }
        catch
        {
        }
        finally
        {
            await writer.DisposeAsync().ConfigureAwait(false);
        }
    }

    private string Format(LogEntry entry)
    {
        string message = entry.Exception is null
            ? entry.Message
            : $"{entry.Message}: {entry.Exception}";
        string sanitized = Sanitize(message);
        if (sanitized.Length > _maxEntryChars)
        {
            sanitized = string.Concat(
                sanitized.AsSpan(0, _maxEntryChars),
                "... [truncated]");
        }

        return $"{entry.Timestamp:O} [{entry.Level}] {sanitized}";
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

    private void PruneLogs(string? activePath = null)
    {
        try
        {
            FileInfo[] files = Directory
                .EnumerateFiles(LogDirectory, "PredatorLite-*.log")
                .Select(path => new FileInfo(path))
                .OrderBy(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.Name, StringComparer.Ordinal)
                .ToArray();
            int fileCount = files.Length;
            long totalBytes = files.Sum(file => file.Length);
            foreach (FileInfo file in files)
            {
                if (fileCount <= _maxRetainedFiles && totalBytes <= _maxTotalBytes)
                {
                    break;
                }

                if (string.Equals(file.FullName, activePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    long length = file.Length;
                    file.Delete();
                    fileCount--;
                    totalBytes -= length;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private StreamWriter CreateWriter(
        DateOnly date,
        int minimumSequence,
        out int sequence,
        out long length,
        out string path)
    {
        sequence = minimumSequence;
        while (true)
        {
            path = GetLogPath(date, sequence);
            length = File.Exists(path) ? new FileInfo(path).Length : 0;
            if (length < _maxFileBytes)
            {
                break;
            }

            sequence++;
        }

        FileStream stream = new(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\n"
        };
        PruneLogs(path);
        return writer;
    }

    private string GetLogPath(DateOnly date, int sequence)
    {
        string suffix = sequence == 0 ? string.Empty : $"-{sequence:D3}";
        return Path.Combine(LogDirectory, $"PredatorLite-{date:yyyyMMdd}{suffix}.log");
    }

    private readonly record struct LogEntry(
        DateTimeOffset Timestamp,
        string Level,
        string Message,
        Exception? Exception);

    private sealed class LogFileWriter(FileAppLogger owner) : IAsyncDisposable
    {
        private StreamWriter? _writer;
        private DateOnly _writerDate;
        private int _writerSequence;
        private long _writerLength;
        private string? _writerPath;

        public async Task WriteAsync(LogEntry entry)
        {
            string line = owner.Format(entry);
            long lineBytes = Encoding.UTF8.GetByteCount(line) + 1;
            DateOnly entryDate = DateOnly.FromDateTime(entry.Timestamp.UtcDateTime);
            if (_writer is null || entryDate != _writerDate)
            {
                await ResetAsync().ConfigureAwait(false);
                _writer = owner.CreateWriter(
                    entryDate,
                    minimumSequence: 0,
                    out _writerSequence,
                    out _writerLength,
                    out _writerPath);
                _writerDate = entryDate;
            }
            else if (_writerLength > 0 && _writerLength + lineBytes > owner._maxFileBytes)
            {
                await ResetAsync().ConfigureAwait(false);
                _writer = owner.CreateWriter(
                    entryDate,
                    _writerSequence + 1,
                    out _writerSequence,
                    out _writerLength,
                    out _writerPath);
                _writerDate = entryDate;
            }

            await _writer.WriteLineAsync(line).ConfigureAwait(false);
            _writerLength += lineBytes;
        }

        public async Task FlushAsync()
        {
            if (_writer is null)
            {
                return;
            }

            await _writer.FlushAsync().ConfigureAwait(false);
            owner.PruneLogs(_writerPath);
        }

        public async Task ResetAsync()
        {
            if (_writer is null)
            {
                return;
            }

            await _writer.DisposeAsync().ConfigureAwait(false);
            _writer = null;
            _writerLength = 0;
            _writerPath = null;
        }

        public async ValueTask DisposeAsync()
        {
            await ResetAsync().ConfigureAwait(false);
        }
    }
}
