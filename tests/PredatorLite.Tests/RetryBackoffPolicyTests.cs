using PredatorLite.Platform.Windows.SystemIntegration;

namespace PredatorLite.Tests;

public sealed class RetryBackoffPolicyTests
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
        TimeSpan delay = RetryBackoffPolicy.GetDelay(failures);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    [Fact]
    public void FailureStateLogsOnceUntilRecovery()
    {
        RetryBackoffState state = new();

        RetryBackoffDecision first = state.RegisterFailure();
        RetryBackoffDecision second = state.RegisterFailure();
        RetryBackoffDecision third = state.RegisterFailure();

        Assert.True(first.ShouldLog);
        Assert.Equal(TimeSpan.FromSeconds(10), first.Delay);
        Assert.False(second.ShouldLog);
        Assert.Equal(TimeSpan.FromSeconds(30), second.Delay);
        Assert.False(third.ShouldLog);
        Assert.Equal(TimeSpan.FromMinutes(2), third.Delay);
        Assert.True(state.Reset());
        Assert.False(state.Reset());

        RetryBackoffDecision afterRecovery = state.RegisterFailure();

        Assert.True(afterRecovery.ShouldLog);
        Assert.Equal(TimeSpan.FromSeconds(10), afterRecovery.Delay);
    }
}
