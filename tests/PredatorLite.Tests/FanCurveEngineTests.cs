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

        IReadOnlyList<FanCurveValidationIssue> issues = FanCurveEngine.Validate(curve);

        FanCurveValidationIssue issue = Assert.Single(
            issues,
            issue => issue.Channel == FanCurveChannel.Cpu &&
                issue.Code == FanCurveValidationCode.InvalidSafetyEndpoint);
        Assert.Equal(7, issue.PointIndex);
        Assert.Equal(FanCurveEngine.SafetyTemperatureC, issue.MinimumTemperatureC);
        Assert.Equal(100, issue.MinimumSpeedPercent);
    }

    [Fact]
    public void DecreasingSpeedIsRejected()
    {
        FanCurve curve = FanCurve.CreateDefault();
        curve.Gpu[3] = new FanCurvePoint(70, 30);

        IReadOnlyList<FanCurveValidationIssue> issues = FanCurveEngine.Validate(curve);

        Assert.Contains(
            issues,
            issue => issue.Channel == FanCurveChannel.Gpu &&
                issue.Code == FanCurveValidationCode.SpeedDecreases &&
                issue.PointIndex == 3 &&
                issue.MinimumSpeedPercent == 40);
    }

    [Fact]
    public void TemperatureOutsideEditableRangeIsRejectedWithLimits()
    {
        FanCurve curve = FanCurve.CreateDefault();
        curve.Cpu[0] = new FanCurvePoint(10, 25);

        IReadOnlyList<FanCurveValidationIssue> issues = FanCurveEngine.Validate(curve);

        Assert.Contains(
            issues,
            issue => issue.Channel == FanCurveChannel.Cpu &&
                issue.Code == FanCurveValidationCode.TemperatureOutOfRange &&
                issue.PointIndex == 0 &&
                issue.MinimumTemperatureC == FanCurveEngine.MinimumTemperatureC &&
                issue.MaximumTemperatureC == FanCurveEngine.SafetyTemperatureC);
    }

    [Fact]
    public void MinimumSpeedIsAlwaysApplied()
    {
        FanCurve curve = FanCurve.CreateDefault();

        int speed = FanCurveEngine.Evaluate(curve.Cpu, 20, 40);

        Assert.Equal(40, speed);
    }
}
