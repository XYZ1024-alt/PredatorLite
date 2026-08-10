namespace PredatorLite.Platform.Windows.SystemIntegration;

internal static class RetryBackoffPolicy
{
    public static TimeSpan GetDelay(int consecutiveFailures) =>
        consecutiveFailures switch
        {
            <= 1 => TimeSpan.FromSeconds(10),
            2 => TimeSpan.FromSeconds(30),
            3 => TimeSpan.FromMinutes(2),
            _ => TimeSpan.FromMinutes(5)
        };
}

internal sealed class RetryBackoffState
{
    private int _consecutiveFailures;
    private bool _failureReported;

    public RetryBackoffDecision RegisterFailure()
    {
        _consecutiveFailures++;
        bool shouldLog = !_failureReported;
        _failureReported = true;
        return new RetryBackoffDecision(
            RetryBackoffPolicy.GetDelay(_consecutiveFailures),
            shouldLog);
    }

    public bool Reset()
    {
        bool shouldLogRecovery = _failureReported;
        _consecutiveFailures = 0;
        _failureReported = false;
        return shouldLogRecovery;
    }
}

internal readonly record struct RetryBackoffDecision(
    TimeSpan Delay,
    bool ShouldLog);
