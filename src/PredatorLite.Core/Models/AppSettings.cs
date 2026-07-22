namespace PredatorLite.Core.Models;

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Language { get; set; } = "zh-CN";

    public bool RunAtStartup { get; set; }

    public bool AutoEcoOnBattery { get; set; }

    public bool AutoRefreshRate { get; set; }

    public bool StartMinimized { get; set; }

    public bool EnableGlobalHotkeys { get; set; } = true;

    public bool ShowOsd { get; set; }

    public bool ShowFps { get; set; }

    public bool ChargeLimit80Percent { get; set; }

    public int? PreferredRefreshRate { get; set; }

    public OperatingMode LastAcMode { get; set; } = OperatingMode.Balanced;

    public FanMode FanMode { get; set; } = FanMode.Auto;

    public FanCurve FanCurve { get; set; } = FanCurve.CreateDefault();

    public LightingProfile Lighting { get; set; } = new();

    public Dictionary<DeviceSettingId, bool> DeviceSettings { get; set; } = [];
}
