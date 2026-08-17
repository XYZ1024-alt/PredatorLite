using System.Diagnostics;
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
        "ASMSvc",
        "AASSvc"
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
                "SELECT Name, DisplayName, State, StartMode, PathName FROM Win32_Service");
            WmiOperationOptions.Configure(searcher);
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
                    string? pathName = service["PathName"]?.ToString();
                    string? fileVersion = ResolveFileVersion(pathName);
                    services.Add(new ManagedServiceInfo(
                        name,
                        service["DisplayName"]?.ToString() ?? name,
                        service["State"]?.ToString() ?? "Unknown",
                        service["StartMode"]?.ToString() ?? "Unknown",
                        Required.Contains(name),
                        ManagedConflicts.Contains(name),
                        fileVersion,
                        pathName));
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

    private static string? ResolveFileVersion(string? pathName)
    {
        if (string.IsNullOrWhiteSpace(pathName))
        {
            return null;
        }

        string path = pathName.Trim('"');
        if (path.Contains(' ', StringComparison.Ordinal) && !path.StartsWith('"'))
        {
            path = path[..path.IndexOf(' ', StringComparison.Ordinal)];
        }

        try
        {
            return File.Exists(path)
                ? FileVersionInfo.GetVersionInfo(path).FileVersion
                : null;
        }
        catch
        {
            return null;
        }
    }
}
