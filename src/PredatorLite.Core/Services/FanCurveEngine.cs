using PredatorLite.Core.Models;

namespace PredatorLite.Core.Services;

public static class FanCurveEngine
{
    public const int MinimumTemperatureC = 20;

    public const int SafetyTemperatureC = 95;

    public const int SafetySpeedPercent = 100;

    public static int Evaluate(
        IReadOnlyList<FanCurvePoint> points,
        double temperatureC,
        int minimumSpeedPercent)
    {
        if (points.Count < 2)
        {
            throw new ArgumentException("A fan curve requires at least two points.", nameof(points));
        }

        if (temperatureC >= SafetyTemperatureC)
        {
            return SafetySpeedPercent;
        }

        IReadOnlyList<FanCurvePoint> ordered = points;
        for (int index = 1; index < points.Count; index++)
        {
            if (points[index - 1].TemperatureC <= points[index].TemperatureC)
            {
                continue;
            }

            ordered = points.OrderBy(point => point.TemperatureC).ToArray();
            break;
        }

        if (temperatureC <= ordered[0].TemperatureC)
        {
            return ClampSpeed(ordered[0].SpeedPercent, minimumSpeedPercent);
        }

        for (int index = 0; index < ordered.Count - 1; index++)
        {
            FanCurvePoint lower = ordered[index];
            FanCurvePoint upper = ordered[index + 1];
            if (temperatureC > upper.TemperatureC)
            {
                continue;
            }

            double range = upper.TemperatureC - lower.TemperatureC;
            double progress = range <= 0 ? 0 : (temperatureC - lower.TemperatureC) / range;
            int speed = (int)Math.Round(
                lower.SpeedPercent + ((upper.SpeedPercent - lower.SpeedPercent) * progress),
                MidpointRounding.AwayFromZero);
            return ClampSpeed(speed, minimumSpeedPercent);
        }

        return ClampSpeed(ordered[^1].SpeedPercent, minimumSpeedPercent);
    }

    public static IReadOnlyList<FanCurveValidationIssue> Validate(FanCurve curve)
    {
        List<FanCurveValidationIssue> issues = [];
        ValidateChannel(curve.Cpu, curve.MinimumSpeedPercent, FanCurveChannel.Cpu, issues);
        ValidateChannel(curve.Gpu, curve.MinimumSpeedPercent, FanCurveChannel.Gpu, issues);
        return issues;
    }

    private static void ValidateChannel(
        List<FanCurvePoint> points,
        int minimumSpeedPercent,
        FanCurveChannel channel,
        List<FanCurveValidationIssue> issues)
    {
        if (points.Count < 2)
        {
            issues.Add(new FanCurveValidationIssue(
                channel,
                FanCurveValidationCode.TooFewPoints,
                MinimumPointCount: 2));
            return;
        }

        int previousTemperature = int.MinValue;
        int previousSpeed = minimumSpeedPercent;
        for (int index = 0; index < points.Count; index++)
        {
            FanCurvePoint point = points[index];
            if (point.TemperatureC is < MinimumTemperatureC or > SafetyTemperatureC)
            {
                issues.Add(new FanCurveValidationIssue(
                    channel,
                    FanCurveValidationCode.TemperatureOutOfRange,
                    index,
                    MinimumTemperatureC,
                    SafetyTemperatureC));
            }

            if (point.TemperatureC <= previousTemperature)
            {
                int minimumTemperature = previousTemperature == int.MaxValue
                    ? int.MaxValue
                    : previousTemperature + 1;
                issues.Add(new FanCurveValidationIssue(
                    channel,
                    FanCurveValidationCode.TemperatureNotIncreasing,
                    index,
                    minimumTemperature,
                    SafetyTemperatureC));
            }

            if (point.SpeedPercent < minimumSpeedPercent || point.SpeedPercent > SafetySpeedPercent)
            {
                issues.Add(new FanCurveValidationIssue(
                    channel,
                    FanCurveValidationCode.SpeedOutOfRange,
                    index,
                    MinimumSpeedPercent: minimumSpeedPercent,
                    MaximumSpeedPercent: SafetySpeedPercent));
            }

            if (point.SpeedPercent < previousSpeed)
            {
                issues.Add(new FanCurveValidationIssue(
                    channel,
                    FanCurveValidationCode.SpeedDecreases,
                    index,
                    MinimumSpeedPercent: previousSpeed,
                    MaximumSpeedPercent: SafetySpeedPercent));
            }

            previousTemperature = point.TemperatureC;
            previousSpeed = point.SpeedPercent;
        }

        if (points[^1].TemperatureC != SafetyTemperatureC ||
            points[^1].SpeedPercent != SafetySpeedPercent)
        {
            issues.Add(new FanCurveValidationIssue(
                channel,
                FanCurveValidationCode.InvalidSafetyEndpoint,
                points.Count - 1,
                SafetyTemperatureC,
                SafetyTemperatureC,
                SafetySpeedPercent,
                SafetySpeedPercent));
        }
    }

    private static int ClampSpeed(int speed, int minimumSpeedPercent) =>
        Math.Clamp(
            speed,
            Math.Clamp(minimumSpeedPercent, 0, SafetySpeedPercent),
            SafetySpeedPercent);
}
