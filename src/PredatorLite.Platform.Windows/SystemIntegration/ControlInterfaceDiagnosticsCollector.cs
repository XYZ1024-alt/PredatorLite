using System.Diagnostics;
using System.Management;
using Microsoft.Win32;
using PredatorLite.Core.Models;

namespace PredatorLite.Platform.Windows.SystemIntegration;

internal static class ControlInterfaceDiagnosticsCollector
{
    private static readonly string[] VendorMarkers =
    [
        "Acer",
        "Predator",
        "Sense",
        "XSense"
    ];

    private static readonly HashSet<int> KnownControlPorts =
    [
        46933,
        46753,
        5141
    ];

    public static ControlInterfaceDiagnostics Read(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<string> collectionFailures = [];
        RegistryValueMetadata aesKey = ReadAesKeyMetadata(collectionFailures);
        List<ServiceSnapshot> allServices = ReadServices(collectionFailures, cancellationToken);
        Dictionary<int, ProcessSnapshot> allProcesses =
            ReadProcesses(collectionFailures, cancellationToken);
        List<EndpointSnapshot> allEndpoints = ReadEndpoints(collectionFailures, cancellationToken);

        ServiceSnapshot[] vendorServices = allServices.Where(IsVendorService).ToArray();
        HashSet<int> relevantProcessIds = vendorServices
            .Select(service => service.ProcessId)
            .Where(processId => processId > 0)
            .ToHashSet();
        foreach (ProcessSnapshot process in allProcesses.Values.Where(IsVendorProcess))
        {
            relevantProcessIds.Add(process.ProcessId);
        }

        foreach (EndpointSnapshot endpoint in allEndpoints.Where(endpoint =>
                     IsKnownControlEndpoint(
                         endpoint.ProcessId,
                         endpoint.LocalPort,
                         endpoint.RemotePort)))
        {
            relevantProcessIds.Add(endpoint.ProcessId);
        }

        Dictionary<int, IReadOnlyList<string>> serviceNamesByProcess = vendorServices
            .Where(service => service.ProcessId > 0)
            .GroupBy(service => service.ProcessId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(service => service.Name)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        VendorProcessDiagnostics[] processes = relevantProcessIds
            .Select(processId => allProcesses.TryGetValue(processId, out ProcessSnapshot? process)
                ? ToProcessDiagnostics(
                    process,
                    serviceNamesByProcess.GetValueOrDefault(processId, []))
                : new VendorProcessDiagnostics(
                    processId,
                    "Unknown",
                    null,
                    null,
                    null,
                    null,
                    null,
                    serviceNamesByProcess.GetValueOrDefault(processId, [])))
            .OrderBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(process => process.ProcessId)
            .ToArray();

        VendorServiceDiagnostics[] services = allServices
            .Where(IsVendorService)
            .Select(service => ToServiceDiagnostics(
                service,
                service.ProcessId > 0 && allProcesses.TryGetValue(service.ProcessId, out ProcessSnapshot? process)
                    ? process
                    : ReadFileSnapshot(service.ExecutablePath)))
            .OrderBy(service => service.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        LoopbackEndpointDiagnostics[] endpoints = allEndpoints
            .Where(endpoint =>
                relevantProcessIds.Contains(endpoint.ProcessId) &&
                IsDiagnosticEndpoint(endpoint.LocalAddress, endpoint.RemoteAddress))
            .Select(endpoint => new LoopbackEndpointDiagnostics(
                endpoint.Protocol,
                endpoint.LocalAddress,
                endpoint.LocalPort,
                IsLoopback(endpoint.RemoteAddress) ? endpoint.RemoteAddress : null,
                IsLoopback(endpoint.RemoteAddress) ? endpoint.RemotePort : null,
                endpoint.State,
                endpoint.ProcessId,
                allProcesses.GetValueOrDefault(endpoint.ProcessId)?.Name,
                serviceNamesByProcess.GetValueOrDefault(endpoint.ProcessId, [])))
            .OrderBy(endpoint => endpoint.Protocol, StringComparer.Ordinal)
            .ThenBy(endpoint => endpoint.LocalPort)
            .ThenBy(endpoint => endpoint.ProcessId)
            .ToArray();

        return new ControlInterfaceDiagnostics(
            SchemaVersion: 1,
            DateTimeOffset.UtcNow,
            aesKey,
            collectionFailures,
            processes,
            services,
            endpoints);
    }

    private static RegistryValueMetadata ReadAesKeyMetadata(List<string> collectionFailures)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Acer\XSense");
            if (key is null || !key.GetValueNames().Contains("AESkey", StringComparer.OrdinalIgnoreCase))
            {
                return new RegistryValueMetadata(false, null, null);
            }

            string valueName = key.GetValueNames().First(name =>
                string.Equals(name, "AESkey", StringComparison.OrdinalIgnoreCase));
            RegistryValueKind kind = key.GetValueKind(valueName);
            int? characterLength = key.GetValue(valueName) is string value ? value.Length : null;
            return new RegistryValueMetadata(true, kind.ToString(), characterLength);
        }
        catch
        {
            collectionFailures.Add("XSenseAesKey");
            return new RegistryValueMetadata(false, null, null);
        }
    }

    private static List<ServiceSnapshot> ReadServices(
        List<string> collectionFailures,
        CancellationToken cancellationToken)
    {
        List<ServiceSnapshot> services = [];
        try
        {
            using ManagementObjectSearcher searcher = new(
                "SELECT Name, DisplayName, State, StartMode, ProcessId, PathName FROM Win32_Service");
            WmiOperationOptions.Configure(searcher);
            using ManagementObjectCollection collection = searcher.Get();
            foreach (ManagementObject service in collection.Cast<ManagementObject>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = service["Name"]?.ToString() ?? "Unknown";
                string displayName = service["DisplayName"]?.ToString() ?? name;
                string? executablePath = ExtractExecutablePath(service["PathName"]?.ToString());
                services.Add(new ServiceSnapshot(
                    name,
                    displayName,
                    service["State"]?.ToString() ?? "Unknown",
                    service["StartMode"]?.ToString() ?? "Unknown",
                    ReadInt32(service["ProcessId"]),
                    executablePath));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            collectionFailures.Add("Services");
        }

        return services;
    }

    private static Dictionary<int, ProcessSnapshot> ReadProcesses(
        List<string> collectionFailures,
        CancellationToken cancellationToken)
    {
        Dictionary<int, ProcessSnapshot> processes = [];
        Process[] systemProcesses;
        try
        {
            systemProcesses = Process.GetProcesses();
        }
        catch
        {
            collectionFailures.Add("Processes");
            return processes;
        }

        foreach (Process process in systemProcesses)
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    int processId = process.Id;
                    string processName = process.ProcessName;
                    string? executablePath = null;
                    try
                    {
                        executablePath = process.MainModule?.FileName;
                    }
                    catch
                    {
                    }

                    ProcessSnapshot file = ReadFileSnapshot(executablePath);
                    processes[processId] = file with
                    {
                        ProcessId = processId,
                        Name = processName
                    };
                }
                catch
                {
                }
            }
        }

        return processes;
    }

    private static List<EndpointSnapshot> ReadEndpoints(
        List<string> collectionFailures,
        CancellationToken cancellationToken)
    {
        List<EndpointSnapshot> endpoints = [];
        ReadTcpEndpoints(endpoints, collectionFailures, cancellationToken);
        ReadUdpEndpoints(endpoints, collectionFailures, cancellationToken);
        return endpoints;
    }

    private static void ReadTcpEndpoints(
        List<EndpointSnapshot> endpoints,
        List<string> collectionFailures,
        CancellationToken cancellationToken)
    {
        try
        {
            ManagementScope scope = new(@"\\.\root\StandardCimv2");
            using ManagementObjectSearcher searcher = new(
                scope,
                new ObjectQuery(
                    "SELECT LocalAddress, LocalPort, RemoteAddress, RemotePort, State, OwningProcess " +
                    "FROM MSFT_NetTCPConnection"));
            WmiOperationOptions.Configure(searcher);
            using ManagementObjectCollection collection = searcher.Get();
            foreach (ManagementObject endpoint in collection.Cast<ManagementObject>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                endpoints.Add(new EndpointSnapshot(
                    "TCP",
                    endpoint["LocalAddress"]?.ToString() ?? string.Empty,
                    ReadInt32(endpoint["LocalPort"]),
                    endpoint["RemoteAddress"]?.ToString(),
                    ReadNullableInt32(endpoint["RemotePort"]),
                    ToTcpState(endpoint["State"]),
                    ReadInt32(endpoint["OwningProcess"])));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            collectionFailures.Add("TcpEndpoints");
        }
    }

    private static void ReadUdpEndpoints(
        List<EndpointSnapshot> endpoints,
        List<string> collectionFailures,
        CancellationToken cancellationToken)
    {
        try
        {
            ManagementScope scope = new(@"\\.\root\StandardCimv2");
            using ManagementObjectSearcher searcher = new(
                scope,
                new ObjectQuery(
                    "SELECT LocalAddress, LocalPort, OwningProcess FROM MSFT_NetUDPEndpoint"));
            WmiOperationOptions.Configure(searcher);
            using ManagementObjectCollection collection = searcher.Get();
            foreach (ManagementObject endpoint in collection.Cast<ManagementObject>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                endpoints.Add(new EndpointSnapshot(
                    "UDP",
                    endpoint["LocalAddress"]?.ToString() ?? string.Empty,
                    ReadInt32(endpoint["LocalPort"]),
                    null,
                    null,
                    null,
                    ReadInt32(endpoint["OwningProcess"])));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            collectionFailures.Add("UdpEndpoints");
        }
    }

    private static ProcessSnapshot ReadFileSnapshot(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return new ProcessSnapshot();
        }

        try
        {
            string fullPath = Environment.ExpandEnvironmentVariables(executablePath);
            FileVersionInfo version = FileVersionInfo.GetVersionInfo(fullPath);
            return new ProcessSnapshot
            {
                ExecutableName = Path.GetFileName(fullPath),
                ProductName = Normalize(version.ProductName),
                ProductVersion = Normalize(version.ProductVersion),
                FileVersion = Normalize(version.FileVersion),
                CompanyName = Normalize(version.CompanyName)
            };
        }
        catch
        {
            return new ProcessSnapshot
            {
                ExecutableName = Path.GetFileName(executablePath)
            };
        }
    }

    private static VendorProcessDiagnostics ToProcessDiagnostics(
        ProcessSnapshot process,
        IReadOnlyList<string> serviceNames) =>
        new(
            process.ProcessId,
            process.Name,
            process.ExecutableName,
            process.ProductName,
            process.ProductVersion,
            process.FileVersion,
            process.CompanyName,
            serviceNames);

    private static VendorServiceDiagnostics ToServiceDiagnostics(
        ServiceSnapshot service,
        ProcessSnapshot process) =>
        new(
            service.Name,
            service.DisplayName,
            service.Status,
            service.StartMode,
            service.ProcessId > 0 ? service.ProcessId : null,
            process.ExecutableName,
            process.ProductName,
            process.ProductVersion,
            process.FileVersion,
            process.CompanyName);

    private static bool IsVendorService(ServiceSnapshot service) =>
        ContainsVendorMarker(service.Name) ||
        ContainsVendorMarker(service.DisplayName) ||
        ContainsVendorMarker(service.ExecutablePath);

    private static bool IsVendorProcess(ProcessSnapshot process) =>
        ContainsVendorMarker(process.Name) ||
        ContainsVendorMarker(process.ExecutableName) ||
        ContainsVendorMarker(process.ProductName) ||
        ContainsVendorMarker(process.CompanyName);

    private static bool ContainsVendorMarker(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        VendorMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    internal static bool IsKnownControlEndpoint(int processId, int localPort, int? remotePort) =>
        processId > 0 &&
        (KnownControlPorts.Contains(localPort) ||
            (remotePort is int port && KnownControlPorts.Contains(port)));

    internal static bool IsDiagnosticEndpoint(string? localAddress, string? remoteAddress) =>
        IsLoopback(localAddress) ||
        IsWildcard(localAddress) ||
        IsLoopback(remoteAddress);

    internal static bool IsLoopback(string? address) =>
        address is "127.0.0.1" or "::1" ||
        address?.StartsWith("127.", StringComparison.Ordinal) == true;

    internal static bool IsWildcard(string? address) =>
        address is "0.0.0.0" or "::";

    internal static string? ExtractExecutablePath(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        string trimmed = commandLine.Trim();
        if (trimmed[0] == '"')
        {
            int closingQuote = trimmed.IndexOf('"', 1);
            return closingQuote > 1 ? trimmed[1..closingQuote] : null;
        }

        int extension = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return extension >= 0 ? trimmed[..(extension + 4)] : null;
    }

    private static int ReadInt32(object? value) =>
        value is null ? 0 : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);

    private static int? ReadNullableInt32(object? value) =>
        value is null ? null : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);

    internal static string? ToTcpState(object? value)
    {
        int state = ReadInt32(value);
        return state switch
        {
            1 => "Closed",
            2 => "Listen",
            3 => "SynSent",
            4 => "SynReceived",
            5 => "Established",
            6 => "FinWait1",
            7 => "FinWait2",
            8 => "CloseWait",
            9 => "Closing",
            10 => "LastAck",
            11 => "TimeWait",
            12 => "DeleteTcb",
            100 => "Bound",
            _ => state.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ServiceSnapshot(
        string Name,
        string DisplayName,
        string Status,
        string StartMode,
        int ProcessId,
        string? ExecutablePath);

    private sealed record ProcessSnapshot
    {
        public int ProcessId { get; init; }
        public string Name { get; init; } = "Unknown";
        public string? ExecutableName { get; init; }
        public string? ProductName { get; init; }
        public string? ProductVersion { get; init; }
        public string? FileVersion { get; init; }
        public string? CompanyName { get; init; }
    }

    private sealed record EndpointSnapshot(
        string Protocol,
        string LocalAddress,
        int LocalPort,
        string? RemoteAddress,
        int? RemotePort,
        string? State,
        int ProcessId);
}
