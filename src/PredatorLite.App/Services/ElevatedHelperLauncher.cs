using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using PredatorLite.Core.Models;

namespace PredatorLite.App.Services;

public sealed class ElevatedHelperLauncher
{
    public async Task<ApplyResult> SetConflictingServicesDisabledAsync(bool disabled)
    {
        string? executable = CompanionExecutableLocator.Find("PredatorLite.ElevatedHelper.exe");
        if (executable is null)
        {
            return ApplyResult.Failure("The elevated helper executable was not found.");
        }

        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string backupPath = Path.Combine(programData, "PredatorLite", "service-backup.json");
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"{(disabled ? "disable" : "restore")} \"{backupPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process is null)
            {
                return ApplyResult.Failure("The elevated helper could not start.");
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode == 0
                ? ApplyResult.Success(disabled
                    ? "Conflicting Acer services were disabled."
                    : "Acer service start modes were restored.")
                : ApplyResult.Failure($"The elevated helper returned code {process.ExitCode}.");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return new ApplyResult(ApplyStatus.RequiresElevation, "Administrator approval was cancelled.");
        }
        catch (Exception exception)
        {
            return ApplyResult.Failure(exception.Message);
        }
    }
}
