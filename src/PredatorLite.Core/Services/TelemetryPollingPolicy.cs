namespace PredatorLite.Core.Services;

public static class TelemetryPollingPolicy
{
    public static readonly TimeSpan ActiveInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan BackgroundInterval = TimeSpan.FromSeconds(15);

    public static TimeSpan GetInterval(
        bool isWindowVisible,
        bool showOsd,
        bool customFanActive) =>
        isWindowVisible || showOsd || customFanActive
            ? ActiveInterval
            : BackgroundInterval;

    public static bool ShouldEnableExtendedTelemetry(
        bool isWindowVisible,
        bool isMonitorSelected,
        bool showOsd,
        bool customFanActive) =>
        isWindowVisible && isMonitorSelected ||
        showOsd ||
        customFanActive;
}
