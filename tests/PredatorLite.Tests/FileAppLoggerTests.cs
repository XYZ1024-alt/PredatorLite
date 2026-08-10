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

    [Fact]
    public void FullQueueDropsExcessEntriesAndWritesSummary()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"PredatorLite-logger-{Guid.NewGuid():N}");
        TaskCompletionSource writerStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            FileAppLogger logger = new(
                directory,
                queueCapacity: 2,
                maxEntryChars: 1024,
                maxRetainedFiles: 4,
                maxFileBytes: 1024 * 1024,
                maxTotalBytes: 4 * 1024 * 1024,
                writerStartBarrier: writerStart.Task);
            for (int index = 0; index < 10; index++)
            {
                logger.Info($"queued-{index}");
            }

            writerStart.SetResult();
            logger.Dispose();

            string logPath = Assert.Single(Directory.EnumerateFiles(directory, "PredatorLite-*.log"));
            string contents = File.ReadAllText(logPath);
            Assert.Contains("[INFO] queued-0", contents, StringComparison.Ordinal);
            Assert.Contains("[INFO] queued-1", contents, StringComparison.Ordinal);
            Assert.Contains(
                "[WARN] Dropped 8 log entries because the bounded queue was full.",
                contents,
                StringComparison.Ordinal);
        }
        finally
        {
            writerStart.TrySetResult();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void RotationBoundsFileCountSizeAndTotalStorage()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"PredatorLite-logger-{Guid.NewGuid():N}");
        try
        {
            FileAppLogger logger = new(
                directory,
                queueCapacity: 64,
                maxEntryChars: 80,
                maxRetainedFiles: 3,
                maxFileBytes: 256,
                maxTotalBytes: 300);
            for (int index = 0; index < 12; index++)
            {
                logger.Info($"entry-{index:D2}-{new string('x', 100)}");
            }

            logger.Dispose();

            FileInfo[] files = Directory
                .EnumerateFiles(directory, "PredatorLite-*.log")
                .Select(path => new FileInfo(path))
                .ToArray();
            Assert.InRange(files.Length, 1, 3);
            Assert.All(files, file => Assert.InRange(file.Length, 1, 256));
            Assert.InRange(files.Sum(file => file.Length), 1, 300);
            Assert.Contains(
                "entry-11",
                string.Concat(files.Select(file => File.ReadAllText(file.FullName))),
                StringComparison.Ordinal);
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
    public void StartupDeletesExpiredLogFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"PredatorLite-logger-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            string expiredPath = Path.Combine(directory, "PredatorLite-20000101.log");
            File.WriteAllText(expiredPath, "expired");
            File.SetLastWriteTimeUtc(expiredPath, DateTime.UtcNow.AddDays(-8));

            FileAppLogger logger = new(directory);
            logger.Info("current");
            logger.Dispose();

            Assert.False(File.Exists(expiredPath));
            Assert.Single(Directory.EnumerateFiles(directory, "PredatorLite-*.log"));
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
