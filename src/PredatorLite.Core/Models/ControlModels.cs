namespace PredatorLite.Core.Models;

public enum OperatingMode : byte
{
    Silent = 0x00,
    Balanced = 0x01,
    Performance = 0x04,
    Turbo = 0x05,
    Eco = 0x06
}

public enum FanMode
{
    Auto = 0,
    Max = 1,
    Custom = 2
}

public enum GpuMuxMode
{
    Discrete = 1,
    Hybrid = 2
}

public enum LightingEffect
{
    Static,
    Breathing,
    Neon,
    Wave,
    Ripple,
    Zoom,
    Snake,
    Disco
}

public enum DeviceSettingId
{
    WindowsKey,
    StickyKeys,
    BootSound,
    LcdOverdrive,
    PanelDynamicRefresh,
    SoundMode,
    KeyboardBacklightTimeout,
    BatteryBoost
}

public enum ApplyStatus
{
    Success,
    Failed,
    NotSupported,
    RequiresElevation
}

public enum FanCurveChannel
{
    Cpu,
    Gpu
}

public enum FanCurveValidationCode
{
    TooFewPoints,
    TemperatureOutOfRange,
    TemperatureNotIncreasing,
    SpeedOutOfRange,
    SpeedDecreases,
    InvalidSafetyEndpoint
}

public sealed record FanCurveValidationIssue(
    FanCurveChannel Channel,
    FanCurveValidationCode Code,
    int? PointIndex = null,
    int? MinimumTemperatureC = null,
    int? MaximumTemperatureC = null,
    int? MinimumSpeedPercent = null,
    int? MaximumSpeedPercent = null,
    int? MinimumPointCount = null);

public sealed record ApplyResult(
    ApplyStatus Status,
    string Message,
    bool RequiresReboot = false)
{
    public bool IsSuccess => Status == ApplyStatus.Success;

    public static ApplyResult Success(string message, bool requiresReboot = false) =>
        new(ApplyStatus.Success, message, requiresReboot);

    public static ApplyResult Failure(string message) =>
        new(ApplyStatus.Failed, message);

    public static ApplyResult Unsupported(string message) =>
        new(ApplyStatus.NotSupported, message);
}

public sealed record FanCurvePoint(int TemperatureC, int SpeedPercent);

public sealed class FanCurve
{
    public List<FanCurvePoint> Cpu { get; set; } = CreateDefaultPoints();

    public List<FanCurvePoint> Gpu { get; set; } = CreateDefaultPoints();

    public int MinimumSpeedPercent { get; set; } = 20;

    public static FanCurve CreateDefault() => new();

    private static List<FanCurvePoint> CreateDefaultPoints() =>
    [
        new(40, 25),
        new(50, 30),
        new(60, 40),
        new(70, 55),
        new(80, 70),
        new(85, 82),
        new(90, 92),
        new(95, 100)
    ];
}

public sealed class LightingProfile
{
    public LightingEffect Effect { get; set; } = LightingEffect.Static;

    public int Brightness { get; set; } = 5;

    public int Speed { get; set; } = 3;

    public int Direction { get; set; } = 1;

    public string PrimaryColor { get; set; } = "#00A8E8";

    public List<string> ZoneColors { get; set; } =
    [
        "#00A8E8",
        "#00A8E8",
        "#00A8E8",
        "#00A8E8"
    ];

    public bool LogoEnabled { get; set; } = true;
}
