using PredatorLite.Platform.Windows;

namespace PredatorLite.Tests;

public sealed class PredatorPlatformServiceRefreshTests
{
    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 10)]
    [InlineData(2, 30)]
    [InlineData(3, 120)]
    [InlineData(4, 300)]
    [InlineData(100, 300)]
    public void ConsecutiveFailuresUseBoundedBackoff(int failures, int expectedSeconds)
    {
        TimeSpan delay = PredatorPlatform.GetServiceStateRefreshBackoff(failures);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }
}
