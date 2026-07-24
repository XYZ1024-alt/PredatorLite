using PredatorLite.Core.Models;

namespace PredatorLite.Platform.Windows.Acer;

internal static class AcerControlStateParser
{
    public static bool TryReadOperatingMode(AcerResponse? response, out OperatingMode mode)
    {
        mode = default;
        if (!TryReadRawMode(response, out int value) || value is < byte.MinValue or > byte.MaxValue)
        {
            return false;
        }

        OperatingMode candidate = (OperatingMode)(byte)value;
        if (!Enum.IsDefined(candidate))
        {
            return false;
        }

        mode = candidate;
        return true;
    }

    public static bool TryReadFanMode(AcerResponse? response, out FanMode mode)
    {
        mode = default;
        if (!TryReadRawMode(response, out int value) || !Enum.IsDefined(typeof(FanMode), value))
        {
            return false;
        }

        mode = (FanMode)value;
        return true;
    }

    public static bool TryReadGpuMuxMode(AcerResponse? response, out GpuMuxMode mode)
    {
        mode = default;
        if (!TryReadRawMode(response, out int value) || !Enum.IsDefined(typeof(GpuMuxMode), value))
        {
            return false;
        }

        mode = (GpuMuxMode)value;
        return true;
    }

    private static bool TryReadRawMode(AcerResponse? response, out int value)
    {
        value = 0;
        return response?.IsSuccess == true && response.TryGetInt("mode", out value);
    }
}
