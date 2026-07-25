using PredatorLite.Core.Services;

namespace PredatorLite.Tests;

public sealed class FileAppLoggerTests
{
    [Fact]
    public void NoEntriesDoNotCreateLogDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"PredatorLite-logger-{Guid.NewGuid():N}");

        FileAppLogger logger = new(directory);
        logger.Dispose();
        logger.Dispose();

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task ConcurrentFirstEntriesAreWrittenOnce()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"PredatorLite-logger-{Guid.NewGuid():N}");
        try
        {
            FileAppLogger logger = new(directory);
            Task[] writes = Enumerable.Range(0, 64)
                .Select(index => Task.Run(() => logger.Info($"concurrent-{index:D2}")))
                .ToArray();

            await Task.WhenAll(writes);
            logger.Dispose();

            string logPath = Assert.Single(Directory.EnumerateFiles(directory, "PredatorLite-*.log"));
            string[] lines = await File.ReadAllLinesAsync(logPath);
            Assert.Equal(64, lines.Length);
            for (int index = 0; index < 64; index++)
            {
                Assert.Single(lines, line => line.Contains($"concurrent-{index:D2}", StringComparison.Ordinal));
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

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
