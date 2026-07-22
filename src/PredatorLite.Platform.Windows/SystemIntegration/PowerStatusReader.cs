using System.Runtime.InteropServices;

namespace PredatorLite.Platform.Windows.SystemIntegration;

internal static class PowerStatusReader
{
    public static (bool? onAc, int? batteryPercent) Read()
    {
        if (!GetSystemPowerStatus(out SystemPowerStatus status))
        {
            return (null, null);
        }

        bool? onAc = status.AcLineStatus switch
        {
            0 => false,
            1 => true,
            _ => null
        };
        int? percentage = status.BatteryLifePercent == byte.MaxValue
            ? null
            : status.BatteryLifePercent;
        return (onAc, percentage);
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}
