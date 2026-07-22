using PredatorLite.Core.Models;
using PredatorLite.Core.Services;

namespace PredatorLite.Tests;

public sealed class FanCurveEngineTests
{
    [Fact]
    public void DefaultCurveIsValid()
    {
        Assert.Empty(FanCurveEngine.Validate(FanCurve.CreateDefault()));
    }

    [Theory]
    [InlineData(40, 25)]
    [InlineData(55, 35)]
    [InlineData(75, 63)]
    [InlineData(95, 100)]
    [InlineData(105, 100)]
    public void EvaluateInterpolatesAndEnforcesThermalSafety(double temperature, int expected)
    {
        FanCurve curve = FanCurve.CreateDefault();

        int speed = FanCurveEngine.Evaluate(curve.Cpu, temperature, curve.MinimumSpeedPercent);

        Assert.Equal(expected, speed);
    }

    [Fact]
    public void MissingSafetyEndpointIsRejected()
    {
        FanCurve curve = FanCurve.CreateDefault();
        curve.Cpu[^1] = new FanCurvePoint(92, 96);

        IReadOnlyList<string> errors = FanCurveEngine.Validate(curve);

        Assert.Contains(errors, error => error.Contains("95", StringComparison.Ordinal));
    }

    [Fact]
    public void DecreasingSpeedIsRejected()
    {
        FanCurve curve = FanCurve.CreateDefault();
        curve.Gpu[3] = new FanCurvePoint(70, 30);

        IReadOnlyList<string> errors = FanCurveEngine.Validate(curve);

        Assert.Contains(errors, error => error.Contains("must not decrease", StringComparison.Ordinal));
    }

    [Fact]
    public void MinimumSpeedIsAlwaysApplied()
    {
        FanCurve curve = FanCurve.CreateDefault();

        int speed = FanCurveEngine.Evaluate(curve.Cpu, 20, 40);

        Assert.Equal(40, speed);
    }
}
