using System.Runtime.InteropServices;

namespace PredatorLite.Platform.Windows.SystemIntegration;

internal sealed class DisplayController
{
    private const int EnumCurrentSettings = -1;
    private const int DmDisplayFrequency = 0x00400000;
    private const int CdsUpdateRegistry = 0x00000001;
    private const int DispChangeSuccessful = 0;

    public IReadOnlyList<int> GetSupportedRefreshRates()
    {
        DevMode current = CreateDevMode();
        if (!EnumDisplaySettings(null, EnumCurrentSettings, ref current))
        {
            return [];
        }

        HashSet<int> rates = [];
        for (int modeIndex = 0; ; modeIndex++)
        {
            DevMode mode = CreateDevMode();
            if (!EnumDisplaySettings(null, modeIndex, ref mode))
            {
                break;
            }

            if (mode.PelsWidth == current.PelsWidth &&
                mode.PelsHeight == current.PelsHeight &&
                mode.DisplayFrequency is >= 30 and <= 1000)
            {
                rates.Add(mode.DisplayFrequency);
            }
        }

        return rates.OrderBy(rate => rate).ToArray();
    }

    public int? GetCurrentRefreshRate()
    {
        DevMode current = CreateDevMode();
        return EnumDisplaySettings(null, EnumCurrentSettings, ref current)
            ? current.DisplayFrequency
            : null;
    }

    public bool SetRefreshRate(int refreshRate)
    {
        if (!GetSupportedRefreshRates().Contains(refreshRate))
        {
            return false;
        }

        DevMode current = CreateDevMode();
        if (!EnumDisplaySettings(null, EnumCurrentSettings, ref current))
        {
            return false;
        }

        current.DisplayFrequency = refreshRate;
        current.Fields = DmDisplayFrequency;
        return ChangeDisplaySettingsEx(null, ref current, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero) ==
            DispChangeSuccessful;
    }

    private static DevMode CreateDevMode() => new()
    {
        DeviceName = string.Empty,
        FormName = string.Empty,
        Size = (short)Marshal.SizeOf<DevMode>()
    };

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNumber, ref DevMode devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(
        string? deviceName,
        ref DevMode devMode,
        IntPtr window,
        int flags,
        IntPtr parameters);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public short SpecVersion;
        public short DriverVersion;
        public short Size;
        public short DriverExtra;
        public int Fields;
        public int PositionX;
        public int PositionY;
        public int DisplayOrientation;
        public int DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TtOption;
        public short Collate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;
        public short LogPixels;
        public int BitsPerPel;
        public int PelsWidth;
        public int PelsHeight;
        public int DisplayFlags;
        public int DisplayFrequency;
        public int IcmMethod;
        public int IcmIntent;
        public int MediaType;
        public int DitherType;
        public int Reserved1;
        public int Reserved2;
        public int PanningWidth;
        public int PanningHeight;
    }
}
