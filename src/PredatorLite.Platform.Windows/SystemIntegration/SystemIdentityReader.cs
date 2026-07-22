using System.Management;
using PredatorLite.Core.Models;

namespace PredatorLite.Platform.Windows.SystemIntegration;

internal static class SystemIdentityReader
{
    public static DeviceIdentity Read()
    {
        string manufacturer = ReadProperty("Win32_ComputerSystem", "Manufacturer") ?? "Unknown";
        string model = ReadProperty("Win32_ComputerSystem", "Model") ?? "Unknown";
        string bios = ReadProperty("Win32_BIOS", "SMBIOSBIOSVersion") ?? "Unknown";
        return new DeviceIdentity(
            manufacturer.Trim(),
            model.Trim(),
            bios.Trim(),
            Environment.OSVersion.VersionString);
    }

    private static string? ReadProperty(string className, string propertyName)
    {
        try
        {
            using ManagementObjectSearcher searcher = new($"SELECT {propertyName} FROM {className}");
            using ManagementObjectCollection collection = searcher.Get();
            return collection.Cast<ManagementObject>().FirstOrDefault()?[propertyName]?.ToString();
        }
        catch
        {
            return null;
        }
    }
}
