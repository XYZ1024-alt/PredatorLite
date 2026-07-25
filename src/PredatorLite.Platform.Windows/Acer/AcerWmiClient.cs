using System.ComponentModel;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using PredatorLite.Core.Abstractions;
using PredatorLite.Core.Models;

namespace PredatorLite.Platform.Windows.Acer;

public sealed class AcerWmiClient
{
    private readonly object _sync = new();
    private readonly IAppLogger _logger;

    public AcerWmiClient(IAppLogger logger)
    {
        _logger = logger;
    }

    public static Task<bool> IsGamingInterfaceAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => HasInstance(AcerProtocol.GamingClass), cancellationToken);

    public static Task<bool> IsBatteryInterfaceAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => HasInstance(AcerProtocol.BatteryClass), cancellationToken);

    public Task<int?> ReadSensorAsync(ulong sensorId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            (bool success, ulong output) = InvokeGaming(
                AcerProtocol.GetSystemInfo,
                0x0001ul | (sensorId << 8));
            return success ? (int?)((output >> 8) & 0xFFFF) : null;
        }, cancellationToken);

    public Task<OperatingMode?> ReadOperatingModeAsync(CancellationToken cancellationToken = default) =>
        Task.Run<OperatingMode?>(() =>
        {
            (bool success, ulong output) = InvokeGaming(AcerProtocol.GetMiscSetting, 0x0B);
            if (!success)
            {
                return null;
            }

            OperatingMode mode = (OperatingMode)((output >> 8) & 0xFF);
            return Enum.IsDefined(mode) ? mode : null;
        }, cancellationToken);

    public Task<bool> SetOperatingModeAsync(OperatingMode mode, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            return Task.FromResult(false);
        }

        return Task.Run(() =>
        {
            ulong input = 0x0Bul | ((ulong)(byte)mode << 8);
            return InvokeGaming(AcerProtocol.SetMiscSetting, input).success;
        }, cancellationToken);
    }

    public Task<bool> SetFanModeAsync(
        FanMode mode,
        int cpuSpeedPercent,
        int gpuSpeedPercent,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            return Task.FromResult(false);
        }

        return Task.Run(() =>
        {
            ulong behavior = mode switch
            {
                FanMode.Auto => 0x410009ul,
                FanMode.Max => 0x820009ul,
                FanMode.Custom => 0xC30009ul,
                _ => 0x410009ul
            };

            if (!InvokeGaming(AcerProtocol.SetFanBehavior, behavior).success)
            {
                return false;
            }

            if (mode != FanMode.Custom)
            {
                return true;
            }

            return SetFanSpeed(channel: 1, cpuSpeedPercent) && SetFanSpeed(channel: 4, gpuSpeedPercent);
        }, cancellationToken);
    }

    public Task<bool> SetChargeLimitAsync(bool limitTo80Percent, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            lock (_sync)
            {
                try
                {
                    using ManagementObject? battery = FindFirst(AcerProtocol.BatteryClass);
                    if (battery is null)
                    {
                        return false;
                    }

                    using ManagementBaseObject input = battery.GetMethodParameters(AcerProtocol.SetBatteryHealth);
                    input["uBatteryNo"] = (byte)1;
                    input["uFunctionMask"] = (byte)1;
                    input["uFunctionStatus"] = (byte)(limitTo80Percent ? 1 : 0);
                    input["uReservedIn"] = new byte[] { 0, 0, 0, 0, 0 };
                    using ManagementBaseObject? _ = battery.InvokeMethod(AcerProtocol.SetBatteryHealth, input, null);
                    return true;
                }
                catch (Exception exception)
                {
                    _logger.LogError("Battery charge limit WMI request failed", exception);
                    return false;
                }
            }
        }, cancellationToken);

    public Task<bool?> ReadChargeLimitAsync(CancellationToken cancellationToken = default) =>
        Task.Run<bool?>(() =>
        {
            lock (_sync)
            {
                try
                {
                    using ManagementObject? battery = FindFirst(AcerProtocol.BatteryClass);
                    if (battery is null)
                    {
                        return null;
                    }

                    using ManagementBaseObject input = battery.GetMethodParameters(AcerProtocol.GetBatteryHealth);
                    input["uBatteryNo"] = (byte)1;
                    input["uFunctionQuery"] = (byte)1;
                    input["uReserved"] = new byte[] { 0, 0 };
                    using ManagementBaseObject? output = battery.InvokeMethod(AcerProtocol.GetBatteryHealth, input, null);
                    if (output?["uFunctionStatus"] is byte[] bytes && bytes.Length > 0)
                    {
                        return bytes[0] == 1;
                    }

                    object? status = output?["uFunctionStatus"];
                    return status is null
                        ? null
                        : Convert.ToInt32(status, CultureInfo.InvariantCulture) == 1;
                }
                catch (Exception exception)
                {
                    _logger.LogError("Battery charge limit query failed", exception);
                    return null;
                }
            }
        }, cancellationToken);

    public Task<bool?> ReadKeyboardTimeoutAsync(CancellationToken cancellationToken = default) =>
        Task.Run<bool?>(() =>
        {
            ulong output = InvokeApge(AcerProtocol.ApgeGetFunction, 0x88401ul);
            return output == 0 ? null : output == 0x1E0000080000ul;
        }, cancellationToken);

    public Task<bool> SetKeyboardTimeoutAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            ulong input = enabled ? 0x1E0000088402ul : 0x88402ul;
            return InvokeApge(AcerProtocol.ApgeSetFunction, input) != 0;
        }, cancellationToken);

    public Task<bool> SetLcdOverdriveAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Task.Run(() => InvokeGaming(AcerProtocol.SetProfile, enabled ? 0x1000000000010ul : 0x10ul).success,
            cancellationToken);

    public void ApplyWindowsPowerOverlay(OperatingMode mode)
    {
        Guid scheme = mode switch
        {
            OperatingMode.Silent or OperatingMode.Eco => AcerProtocol.PowerEfficiency,
            OperatingMode.Performance or OperatingMode.Turbo => AcerProtocol.PowerPerformance,
            _ => AcerProtocol.PowerBalanced
        };

        try
        {
            uint result = PowerSetActiveOverlayScheme(scheme);
            if (result != 0)
            {
                _logger.LogError(
                    "Windows power overlay update failed",
                    new Win32Exception(unchecked((int)result)));
            }
        }
        catch (Exception exception)
        {
            _logger.LogError("Windows power overlay update failed", exception);
        }
    }

    private static bool HasInstance(string className)
    {
        try
        {
            using ManagementObject? instance = FindFirst(className);
            return instance is not null;
        }
        catch
        {
            return false;
        }
    }

    private static ManagementObject? FindFirst(string className)
    {
        using ManagementObjectSearcher searcher = new(
            AcerProtocol.WmiNamespace,
            $"SELECT * FROM {className}");
        using ManagementObjectCollection collection = searcher.Get();
        return collection.Cast<ManagementObject>().FirstOrDefault();
    }

    private (bool success, ulong output) InvokeGaming(string method, ulong inputValue)
    {
        lock (_sync)
        {
            try
            {
                using ManagementObject? gaming = FindFirst(AcerProtocol.GamingClass);
                if (gaming is null)
                {
                    return (false, 0);
                }

                using ManagementBaseObject input = gaming.GetMethodParameters(method);
                PropertyData? inputProperty = input.Properties.Cast<PropertyData>()
                    .FirstOrDefault(property => !property.Name.StartsWith("__", StringComparison.Ordinal));
                if (inputProperty is null)
                {
                    return (false, 0);
                }

                input[inputProperty.Name] = ConvertForCimType(inputValue, inputProperty.Type);
                using ManagementBaseObject? output = gaming.InvokeMethod(method, input, null);
                PropertyData? outputProperty = output?.Properties.Cast<PropertyData>()
                    .FirstOrDefault(property =>
                        property.Name != "ReturnValue" &&
                        !property.Name.StartsWith("__", StringComparison.Ordinal));
                if (outputProperty?.Value is null)
                {
                    return (false, 0);
                }

                ulong result = Convert.ToUInt64(outputProperty.Value, CultureInfo.InvariantCulture);
                return ((result & 0xFF) == 0, result);
            }
            catch (Exception exception)
            {
                _logger.LogError($"Acer WMI method {method} failed", exception);
                return (false, 0);
            }
        }
    }

    private ulong InvokeApge(string method, ulong inputValue)
    {
        lock (_sync)
        {
            try
            {
                using ManagementObject? apge = FindFirst(AcerProtocol.ApgeClass);
                if (apge is null)
                {
                    return 0;
                }

                using ManagementBaseObject input = apge.GetMethodParameters(method);
                PropertyData? inputProperty = input.Properties.Cast<PropertyData>().FirstOrDefault();
                if (inputProperty is null)
                {
                    return 0;
                }

                input[inputProperty.Name] = ConvertForCimType(inputValue, inputProperty.Type);
                using ManagementBaseObject? output = apge.InvokeMethod(method, input, null);
                PropertyData? outputProperty = output?.Properties.Cast<PropertyData>()
                    .FirstOrDefault(property => property.Name != "ReturnValue");
                return outputProperty?.Value is null
                    ? 0
                    : Convert.ToUInt64(outputProperty.Value, CultureInfo.InvariantCulture);
            }
            catch (Exception exception)
            {
                _logger.LogError($"APGe WMI method {method} failed", exception);
                return 0;
            }
        }
    }

    private bool SetFanSpeed(int channel, int speedPercent)
    {
        int normalized = Math.Clamp(speedPercent, 0, 100);
        ulong value = (ulong)((((normalized * 25600) / 100) & 0xFF00) + channel);
        return InvokeGaming(AcerProtocol.SetFanSpeed, value).success;
    }

    private static object ConvertForCimType(ulong value, CimType type) => type switch
    {
        CimType.UInt16 => Convert.ToUInt16(value),
        CimType.UInt32 => Convert.ToUInt32(value),
        CimType.SInt32 => Convert.ToInt32(value),
        CimType.SInt64 => Convert.ToInt64(value),
        _ => value
    };

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);
}
