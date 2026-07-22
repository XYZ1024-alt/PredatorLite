using Microsoft.Win32;

namespace PredatorLite.App.Services;

public sealed class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PredatorLite";

    public bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string value &&
            value.Contains("PredatorLite", StringComparison.OrdinalIgnoreCase);
    }

    public bool SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            string? executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                return false;
            }

            key.SetValue(ValueName, $"\"{executable}\" --background", RegistryValueKind.String);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
