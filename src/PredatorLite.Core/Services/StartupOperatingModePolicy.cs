using PredatorLite.Core.Models;

namespace PredatorLite.Core.Services;

public static class StartupOperatingModePolicy
{
    public static OperatingMode? Resolve(
        OperatingMode savedMode,
        bool autoEcoOnBattery,
        bool? isOnAcPower)
    {
        if (autoEcoOnBattery)
        {
            if (isOnAcPower == false)
            {
                return OperatingMode.Eco;
            }

            if (!isOnAcPower.HasValue)
            {
                return null;
            }
        }

        return NormalizeSavedMode(savedMode);
    }

    public static OperatingMode NormalizeSavedMode(OperatingMode mode) => mode switch
    {
        OperatingMode.Silent => mode,
        OperatingMode.Balanced => mode,
        OperatingMode.Performance => mode,
        OperatingMode.Turbo => mode,
        _ => OperatingMode.Balanced
    };

    public static bool ShouldRemember(OperatingMode mode) => mode is
        OperatingMode.Silent or
        OperatingMode.Balanced or
        OperatingMode.Performance or
        OperatingMode.Turbo;
}
