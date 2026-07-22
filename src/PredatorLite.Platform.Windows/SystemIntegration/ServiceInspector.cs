using System.Management;
using PredatorLite.Core.Models;

namespace PredatorLite.Platform.Windows.SystemIntegration;

internal static class ServiceInspector
{
    private static readonly HashSet<string> Required = new(StringComparer.OrdinalIgnoreCase)
    {
        "AcerServiceSvc",
        "AcerLightingService",
        "AcerQAAgentSvis",
        "ASMSvc"
    };

    private static readonly HashSet<string> ManagedConflicts = new(StringComparer.OrdinalIgnoreCase)
    {
        "AcerCCAgentSvis",
        "AcerDIAgentSvis",
        "AcerDeviceEnablingServiceV2",
        "PredatorService"
    };

    public static IReadOnlyList<ManagedServiceInfo> Read()
    {
        List<ManagedServiceInfo> services = [];
        string[] allNames = Required.Concat(ManagedConflicts).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        try
        {
            using ManagementObjectSearcher searcher = new(
                "SELECT Name, DisplayName, State, StartMode FROM Win32_Service");
            using ManagementObjectCollection collection = searcher.Get();
            Dictionary<string, ManagementObject> byName = collection
                .Cast<ManagementObject>()
                .Where(service => service["Name"] is not null)
                .ToDictionary(
                    service => service["Name"]!.ToString()!,
                    StringComparer.OrdinalIgnoreCase);

            foreach (string name in allNames)
            {
                if (byName.TryGetValue(name, out ManagementObject? service))
                {
                    services.Add(new ManagedServiceInfo(
                        name,
                        service["DisplayName"]?.ToString() ?? name,
                        service["State"]?.ToString() ?? "Unknown",
                        service["StartMode"]?.ToString() ?? "Unknown",
                        Required.Contains(name),
                        ManagedConflicts.Contains(name)));
                }
                else
                {
                    services.Add(new ManagedServiceInfo(
                        name,
                        name,
                        "Not installed",
                        "Unknown",
                        Required.Contains(name),
                        ManagedConflicts.Contains(name)));
                }
            }
        }
        catch
        {
            return [];
        }

        return services;
    }
}
