using PredatorLite.Core.Services;

namespace PredatorLite.Tests;

public sealed class FileAppLoggerTests
{
    [Fact]
    public void DisposeFlushesQueuedEntries()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"PredatorLite-logger-{Guid.NewGuid():N}");
        try
        {
            FileAppLogger logger = new(directory);
            logger.Info("queued information");
            logger.LogError("queued failure", new InvalidOperationException("details"));

            logger.Dispose();
            logger.Dispose();

            string logPath = Assert.Single(Directory.EnumerateFiles(directory, "PredatorLite-*.log"));
            string contents = File.ReadAllText(logPath);
            Assert.Contains("[INFO] queued information", contents, StringComparison.Ordinal);
            Assert.Contains("[ERROR] queued failure: System.InvalidOperationException: details", contents, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
