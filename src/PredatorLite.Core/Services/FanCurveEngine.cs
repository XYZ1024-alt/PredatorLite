using PredatorLite.Core.Models;

namespace PredatorLite.Core.Services;

public static class FanCurveEngine
{
    public const int SafetyTemperatureC = 95;

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
            return 100;
        }

        FanCurvePoint[] ordered = points.OrderBy(point => point.TemperatureC).ToArray();
        if (temperatureC <= ordered[0].TemperatureC)
        {
            return ClampSpeed(ordered[0].SpeedPercent, minimumSpeedPercent);
        }

        for (int index = 0; index < ordered.Length - 1; index++)
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

    public static IReadOnlyList<string> Validate(FanCurve curve)
    {
        List<string> errors = [];
        ValidateChannel(curve.Cpu, curve.MinimumSpeedPercent, "CPU", errors);
        ValidateChannel(curve.Gpu, curve.MinimumSpeedPercent, "GPU", errors);
        return errors;
    }

    private static void ValidateChannel(
        IReadOnlyList<FanCurvePoint> points,
        int minimumSpeedPercent,
        string channel,
        ICollection<string> errors)
    {
        if (points.Count < 2)
        {
            errors.Add($"{channel} curve must have at least two points.");
            return;
        }

        int previousTemperature = int.MinValue;
        int previousSpeed = minimumSpeedPercent;
        foreach (FanCurvePoint point in points)
        {
            if (point.TemperatureC <= previousTemperature)
            {
                errors.Add($"{channel} temperatures must increase.");
            }

            if (point.SpeedPercent < minimumSpeedPercent || point.SpeedPercent > 100)
            {
                errors.Add($"{channel} speed must be between {minimumSpeedPercent}% and 100%.");
            }

            if (point.SpeedPercent < previousSpeed)
            {
                errors.Add($"{channel} speed must not decrease as temperature rises.");
            }

            previousTemperature = point.TemperatureC;
            previousSpeed = point.SpeedPercent;
        }

        if (points[^1].TemperatureC < SafetyTemperatureC || points[^1].SpeedPercent != 100)
        {
            errors.Add($"{channel} curve must reach 100% at {SafetyTemperatureC}C.");
        }
    }

    private static int ClampSpeed(int speed, int minimumSpeedPercent) =>
        Math.Clamp(speed, Math.Clamp(minimumSpeedPercent, 0, 100), 100);
}
