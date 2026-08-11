using Microsoft.Win32;
using PredatorLite.Core.Abstractions;
using Windows.ApplicationModel;

namespace PredatorLite.App.Services;

public interface IStartupRegistration
{
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);

    Task<bool> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}

public sealed class RegistryStartupRegistration(IAppLogger logger) : IStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PredatorLite";

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            bool enabled = key?.GetValue(ValueName) is string value &&
                value.Contains("PredatorLite", StringComparison.OrdinalIgnoreCase);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(enabled);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError("Reading startup registration failed", exception);
            return Task.FromResult(false);
        }
    }

    public Task<bool> SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(true);
            }

            string? executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                return Task.FromResult(false);
            }

            key.SetValue(ValueName, $"\"{executable}\" --background", RegistryValueKind.String);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError("Updating startup registration failed", exception);
            return Task.FromResult(false);
        }
    }
}

public sealed class PackagedStartupRegistration(IAppLogger logger) : IStartupRegistration
{
    private const string TaskId = "PredatorLiteStartup";

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            StartupTask startupTask = await StartupTask.GetAsync(TaskId);
            cancellationToken.ThrowIfCancellationRequested();
            return startupTask.State == StartupTaskState.Enabled;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError("Reading packaged startup registration failed", exception);
            return false;
        }
    }

    public async Task<bool> SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            StartupTask startupTask = await StartupTask.GetAsync(TaskId);
            cancellationToken.ThrowIfCancellationRequested();
            if (!enabled)
            {
                if (startupTask.State == StartupTaskState.Enabled)
                {
                    startupTask.Disable();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return true;
            }

            if (startupTask.State == StartupTaskState.Enabled)
            {
                return true;
            }

            if (startupTask.State != StartupTaskState.Disabled)
            {
                return false;
            }

            StartupTaskState state = await startupTask.RequestEnableAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return state == StartupTaskState.Enabled;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError("Updating packaged startup registration failed", exception);
            return false;
        }
    }
}
