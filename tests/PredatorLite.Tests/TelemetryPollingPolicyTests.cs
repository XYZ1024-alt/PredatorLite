using PredatorLite.Core.Services;

namespace PredatorLite.Tests;

public sealed class TelemetryPollingPolicyTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void ActiveConsumersKeepTwoSecondPolling(
        bool isWindowVisible,
        bool showOsd,
        bool customFanActive)
    {
        TimeSpan interval = TelemetryPollingPolicy.GetInterval(
            isWindowVisible,
            showOsd,
            customFanActive);

        Assert.Equal(TimeSpan.FromSeconds(2), interval);
    }

    [Fact]
    public void HiddenWindowWithoutActiveConsumersUsesBackgroundPolling()
    {
        TimeSpan interval = TelemetryPollingPolicy.GetInterval(
            isWindowVisible: false,
            showOsd: false,
            customFanActive: false);

        Assert.Equal(TimeSpan.FromSeconds(15), interval);
    }

    [Fact]
    public void HiddenMonitorPageDoesNotKeepExtendedTelemetryEnabled()
    {
        bool enabled = TelemetryPollingPolicy.ShouldEnableExtendedTelemetry(
            isWindowVisible: false,
            isMonitorSelected: true,
            showOsd: false,
            customFanActive: false);

        Assert.False(enabled);
    }

    [Theory]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(false, true, false, true)]
    public void ActiveExtendedConsumersKeepExtendedTelemetryEnabled(
        bool isWindowVisible,
        bool isMonitorSelected,
        bool showOsd,
        bool customFanActive)
    {
        bool enabled = TelemetryPollingPolicy.ShouldEnableExtendedTelemetry(
            isWindowVisible,
            isMonitorSelected,
            showOsd,
            customFanActive);

        Assert.True(enabled);
    }
}
