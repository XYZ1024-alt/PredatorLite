using PredatorLite.Core.Abstractions;

namespace PredatorLite.Core.Services;

public sealed class FileAppLogger : IAppLogger
{
    private readonly object _sync = new();
    private readonly string _userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private bool _disposed;

    public FileAppLogger(string? directory = null)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        LogDirectory = directory ?? Path.Combine(appData, "PredatorLite", "Logs");
        Directory.CreateDirectory(LogDirectory);
        DeleteExpiredLogs();
    }

    public string LogDirectory { get; }

    public void Info(string message) => Write("INFO", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    public void Dispose()
    {
        _disposed = true;
    }

    private void Write(string level, string message, Exception? exception)
    {
        if (_disposed)
        {
            return;
        }

        string sanitized = Sanitize(exception is null ? message : $"{message}: {exception.Message}");
        string line = $"{DateTimeOffset.Now:O} [{level}] {sanitized}{Environment.NewLine}";
        string path = Path.Combine(LogDirectory, $"PredatorLite-{DateTime.UtcNow:yyyyMMdd}.log");

        try
        {
            lock (_sync)
            {
                File.AppendAllText(path, line);
            }
        }
        catch
        {
        }
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
}
