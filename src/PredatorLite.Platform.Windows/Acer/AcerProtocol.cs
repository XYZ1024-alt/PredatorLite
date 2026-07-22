namespace PredatorLite.Platform.Windows.Acer;

public static class AcerProtocol
{
    public const string WmiNamespace = @"root\WMI";
    public const string GamingClass = "AcerGamingFunction";
    public const string ApgeClass = "APGeAction";
    public const string BatteryClass = "BatteryControl";

    public const string GetMiscSetting = "GetGamingMiscSetting";
    public const string SetMiscSetting = "SetGamingMiscSetting";
    public const string GetSystemInfo = "GetGamingSysInfo";
    public const string GetFanSpeed = "GetGamingFanSpeed";
    public const string SetFanSpeed = "SetGamingFanSpeed";
    public const string SetFanBehavior = "SetGamingFanBehavior";
    public const string SetProfile = "SetGamingProfile";

    public const string ApgeGetFunction = "GetFunction";
    public const string ApgeSetFunction = "SetFunction";
    public const string GetBatteryHealth = "GetBatteryHealthControlStatus";
    public const string SetBatteryHealth = "SetBatteryHealthControl";

    public const int CommandPort = 46933;
    public const uint InitializationPacket = 0;
    public const uint QueryPacket = 20;
    public const uint SetPacket = 100;

    public const string Lighting = "LIGHTING";
    public const string OperatingMode = "OPERATING_MODE";
    public const string FanControl = "FAN_CONTROL";
    public const string SoundMode = "SOUND_MODE";
    public const string WindowsKey = "WIN_KEY";
    public const string StickyKeys = "STICKY_KEY";
    public const string BootSound = "BOOT_SOUND";
    public const string LcdOverdrive = "LCD_OVERDRIVE";
    public const string GpuMode = "GPU_MODE";
    public const string PanelDfrMode = "PANEL_DFR_MODE";
    public const string AdaptorStatus = "ADAPTOR_STATUS";
    public const string BatteryBoost = "BATTERY_BOOST";

    public const ulong CpuTemperatureSensor = 0x01;
    public const ulong CpuFanRpmSensor = 0x02;
    public const ulong GpuFanRpmSensor = 0x06;
    public const ulong GpuTemperatureSensor = 0x0A;

    public static readonly Guid PowerEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid PowerBalanced = Guid.Empty;
    public static readonly Guid PowerPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");
}
