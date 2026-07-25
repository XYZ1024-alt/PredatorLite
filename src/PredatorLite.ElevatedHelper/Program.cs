using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace PredatorLite.ElevatedHelper;

internal static class Program
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceChangeConfig = 0x0002;
    private const uint ServiceNoChange = 0xFFFFFFFF;

    private static readonly string[] ManagedServices =
    [
        "AcerCCAgentSvis",
        "AcerDIAgentSvis",
        "AcerDeviceEnablingServiceV2",
        "PredatorService"
    ];

    private static int Main(string[] args)
    {
        if (!IsAdministrator() || args.Length != 2)
        {
            return 2;
        }

        string command = args[0].ToLowerInvariant();
        if (!IsSupportedCommand(command))
        {
            return 4;
        }

        string backupPath;
        try
        {
            backupPath = ValidateBackupPath(args[1]);
        }
        catch
        {
            return 3;
        }

        try
        {
            return command switch
            {
                "disable" => DisableConflicts(backupPath),
                "restore" => RestoreConflicts(backupPath),
                _ => 4
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static bool IsSupportedCommand(string command) =>
        command is "disable" or "restore";

    internal static bool IsManagedService(string serviceName) =>
        ManagedServices.Contains(serviceName, StringComparer.OrdinalIgnoreCase);

    private static int DisableConflicts(string backupPath)
    {
        ServiceBackup? existing = ReadBackup(backupPath);
        if (File.Exists(backupPath) &&
            (existing is null || !IsValidBackup(existing)))
        {
            return 6;
        }

        ServiceBackup backup = existing is { IsApplied: true }
            ? existing
            : new ServiceBackup
            {
                CreatedUtc = DateTimeOffset.UtcNow,
                Services = ManagedServices.Select(ReadServiceState).Where(state => state is not null).Cast<ServiceBackupItem>().ToList(),
                PredatorTaskWasEnabled = IsPredatorTaskEnabled()
            };
        backup.IsApplied = true;
        WriteBackup(backupPath, backup);

        foreach (ServiceBackupItem service in backup.Services)
        {
            StopService(service.Name);
            SetStartMode(service.Name, 4);
        }

        SetPredatorTaskEnabled(false);
        return 0;
    }

    private static int RestoreConflicts(string backupPath)
    {
        if (!File.Exists(backupPath))
        {
            return 5;
        }

        ServiceBackup? backup = ReadBackup(backupPath);
        if (backup is null || !IsValidRestoreBackup(backup))
        {
            return 6;
        }

        foreach (ServiceBackupItem service in backup.Services.Where(item => IsManagedService(item.Name)))
        {
            SetStartMode(service.Name, checked((uint)service.StartValue));
            if (service.WasRunning)
            {
                StartService(service.Name);
            }
        }

        if (backup.PredatorTaskWasEnabled)
        {
            SetPredatorTaskEnabled(true);
        }

        backup.IsApplied = false;
        WriteBackup(backupPath, backup);

        return 0;
    }

    private static ServiceBackup? ReadBackup(string backupPath)
    {
        try
        {
            return File.Exists(backupPath)
                ? JsonSerializer.Deserialize(
                    File.ReadAllText(backupPath),
                    ElevatedHelperJsonContext.Default.ServiceBackup)
                : null;
        }
        catch
        {
            return null;
        }
    }

    internal static bool IsValidBackup(ServiceBackup? backup)
    {
        if (backup?.Services is null)
        {
            return false;
        }

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        return backup.Services.All(item =>
            item is not null &&
            !string.IsNullOrWhiteSpace(item.Name) &&
            IsManagedService(item.Name) &&
            names.Add(item.Name) &&
            item.StartValue is >= 0 and <= 4);
    }

    internal static bool IsValidRestoreBackup(ServiceBackup? backup) =>
        backup?.IsApplied == true && IsValidBackup(backup);

    private static void WriteBackup(string backupPath, ServiceBackup backup)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        string temporary = backupPath + ".tmp";
        FileInfo temporaryFile = new(temporary);
        if (temporaryFile.Exists && temporaryFile.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new UnauthorizedAccessException();
        }

        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(backup, ElevatedHelperJsonContext.Default.ServiceBackup));
        File.Move(temporary, backupPath, true);
    }

    private static ServiceBackupItem? ReadServiceState(string name)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
            if (key?.GetValue("Start") is not int startValue)
            {
                return null;
            }

            using ServiceController controller = new(name);
            return new ServiceBackupItem
            {
                Name = name,
                StartValue = startValue,
                WasRunning = controller.Status == ServiceControllerStatus.Running
            };
        }
        catch
        {
            return null;
        }
    }

    private static void StopService(string name)
    {
        try
        {
            using ServiceController controller = new(name);
            if (controller.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
            {
                controller.Stop();
                controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
            }
        }
        catch
        {
        }
    }

    private static void StartService(string name)
    {
        try
        {
            using ServiceController controller = new(name);
            if (controller.Status == ServiceControllerStatus.Stopped)
            {
                controller.Start();
                controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
            }
        }
        catch
        {
        }
    }

    private static void SetStartMode(string serviceName, uint startMode)
    {
        IntPtr manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            IntPtr service = OpenService(manager, serviceName, ServiceChangeConfig);
            if (service == IntPtr.Zero)
            {
                return;
            }

            try
            {
                if (!ChangeServiceConfig(
                        service,
                        ServiceNoChange,
                        startMode,
                        ServiceNoChange,
                        null,
                        null,
                        IntPtr.Zero,
                        null,
                        null,
                        null,
                        null))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    private static bool IsPredatorTaskEnabled()
    {
        ProcessResult result = RunSchtasks("/Query /TN \\PredatorSenseLauncher /FO LIST /V");
        return result.ExitCode == 0 && !result.Output.Contains("Disabled", StringComparison.OrdinalIgnoreCase);
    }

    private static void SetPredatorTaskEnabled(bool enabled)
    {
        RunSchtasks($"/Change /TN \\PredatorSenseLauncher /{(enabled ? "ENABLE" : "DISABLE")}");
    }

    private static ProcessResult RunSchtasks(string arguments)
    {
        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10_000);
        return new ProcessResult(process.ExitCode, output);
    }

    private static string ValidateBackupPath(string suppliedPath)
    {
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string allowedDirectory = Path.GetFullPath(Path.Combine(programData, "PredatorLite")) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(suppliedPath);
        if (!fullPath.StartsWith(allowedDirectory, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(fullPath), "service-backup.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException();
        }

        DirectoryInfo directory = new(Path.GetDirectoryName(fullPath)!);
        if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new UnauthorizedAccessException();
        }

        FileInfo file = new(fullPath);
        if (file.Exists && file.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new UnauthorizedAccessException();
        }

        return fullPath;
    }

    private static bool IsAdministrator()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr serviceManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ChangeServiceConfig(
        IntPtr service,
        uint serviceType,
        uint startType,
        uint errorControl,
        string? binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password,
        string? displayName);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}

internal sealed class ServiceBackup
{
    public DateTimeOffset CreatedUtc { get; set; }
    public List<ServiceBackupItem> Services { get; set; } = [];
    public bool PredatorTaskWasEnabled { get; set; }
    public bool IsApplied { get; set; }
}

internal sealed class ServiceBackupItem
{
    public string Name { get; set; } = string.Empty;
    public int StartValue { get; set; }
    public bool WasRunning { get; set; }
}

internal sealed record ProcessResult(int ExitCode, string Output);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ServiceBackup))]
internal sealed partial class ElevatedHelperJsonContext : JsonSerializerContext
{
}
