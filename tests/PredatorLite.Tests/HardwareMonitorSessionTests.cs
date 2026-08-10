using PredatorLite.Core.Abstractions;
using PredatorLite.Platform.Windows.SystemIntegration;

namespace PredatorLite.Tests;

public sealed class HardwareMonitorSessionTests
{
    [Fact]
    public async Task TimedOutReadDoesNotStartConcurrentReadAndCleanupWaitsForCompletion()
    {
        using ManualResetEventSlim releaseRead = new();
        BlockingHardwareMonitorReader reader = new(releaseRead);
        RecordingLogger logger = new();
        HardwareMonitorSession session = new(
            reader,
            logger,
            TimeSpan.FromMilliseconds(50));
        Task? cleanupTask = null;

        try
        {
            ExtraTelemetry timedOut = await session.ReadAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(1));

            Assert.Equal(new ExtraTelemetry(), timedOut);
            Assert.Equal(1, reader.ReadCount);
            Assert.Equal(1, logger.ErrorCount);

            Task<ExtraTelemetry> overlappingRead = session.ReadAsync(CancellationToken.None);

            Assert.True(overlappingRead.IsCompletedSuccessfully);
            Assert.Equal(new ExtraTelemetry(), await overlappingRead);
            Assert.Equal(1, reader.ReadCount);

            cleanupTask = session.DisposeAsync();
            await Task.Delay(20);

            Assert.False(cleanupTask.IsCompleted);
            Assert.False(reader.IsDisposed);

            releaseRead.Set();
            await cleanupTask.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.True(reader.IsDisposed);
        }
        finally
        {
            releaseRead.Set();
            if (cleanupTask is not null)
            {
                await cleanupTask.WaitAsync(TimeSpan.FromSeconds(1));
            }
        }
    }

    private sealed class BlockingHardwareMonitorReader(
        ManualResetEventSlim releaseRead) : IHardwareMonitorReader
    {
        private int _readCount;

        public int ReadCount => Volatile.Read(ref _readCount);

        public bool IsDisposed { get; private set; }

        public ExtraTelemetry Read()
        {
            Interlocked.Increment(ref _readCount);
            releaseRead.Wait();
            return new ExtraTelemetry(GpuTemperatureC: 55);
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class RecordingLogger : IAppLogger
    {
        private int _errorCount;

        public string LogDirectory => string.Empty;

        public int ErrorCount => Volatile.Read(ref _errorCount);

        public void Info(string message)
        {
        }

        public void LogError(string message, Exception? exception = null)
        {
            Interlocked.Increment(ref _errorCount);
        }

        public void Dispose()
        {
        }
    }
}
