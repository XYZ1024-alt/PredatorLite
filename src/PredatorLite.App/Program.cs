using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using PredatorLite.App.Services;

namespace PredatorLite.App;

internal static class Program
{
    private const string InstanceKey = "PredatorLite.PrimaryInstance";
    private const string MeasurementInstanceKeyPrefix = "--startup-instance-key=";
    private const uint Infinite = 0xFFFFFFFF;

    private static readonly object ActivationSync = new();
    private static App? _app;
    private static DispatcherQueue? _dispatcherQueue;
    private static bool _activationPending;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            StartupTelemetry.Start(args);
            WinRT.ComWrappersSupport.InitializeComWrappers();
            if (RedirectToPrimaryInstance(GetInstanceKey(args)))
            {
                return 0;
            }

            Application.Start(_ =>
            {
                DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherQueueSynchronizationContext(dispatcher));
                StartupTelemetry.Mark("xaml-start");

                App app = new();
                lock (ActivationSync)
                {
                    _dispatcherQueue = dispatcher;
                    _app = app;
                }

                DispatchPendingActivation();
            });
            return Environment.ExitCode;
        }
        catch (Exception exception)
        {
            NativeMethods.ShowError(
                IntPtr.Zero,
                $"PredatorLite could not start.\n\n{exception.Message}",
                "PredatorLite");
            return 1;
        }
    }

    private static bool RedirectToPrimaryInstance(string instanceKey)
    {
        AppActivationArguments activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        AppInstance instance = AppInstance.FindOrRegisterForKey(instanceKey);
        if (instance.IsCurrent)
        {
            instance.Activated += OnActivated;
            StartupTelemetry.Mark("primary-instance");
            return false;
        }

        StartupTelemetry.Mark("redirect-start");
        RedirectActivation(activation, instance);
        StartupTelemetry.Mark("redirect-complete");
        return true;
    }

    private static string GetInstanceKey(string[] arguments)
    {
        bool trayOnly = arguments.Any(argument =>
            string.Equals(argument, "--startup-tray-only", StringComparison.OrdinalIgnoreCase));
        bool hasMeasurementPipe = arguments.Any(argument =>
            argument.StartsWith("--startup-pipe=", StringComparison.OrdinalIgnoreCase));
        string? requestedKey = arguments
            .FirstOrDefault(argument => argument.StartsWith(
                MeasurementInstanceKeyPrefix,
                StringComparison.OrdinalIgnoreCase))?
            [MeasurementInstanceKeyPrefix.Length..];
        return trayOnly &&
            hasMeasurementPipe &&
            Guid.TryParse(requestedKey, out Guid measurementKey)
                ? $"{InstanceKey}.StartupMeasurement.{measurementKey:N}"
                : trayOnly && hasMeasurementPipe
                    ? $"{InstanceKey}.StartupMeasurement.{Environment.ProcessId}"
                    : InstanceKey;
    }

    private static void RedirectActivation(
        AppActivationArguments activation,
        AppInstance primaryInstance)
    {
        using EventWaitHandle completed = new(false, EventResetMode.ManualReset);
        Task redirectTask = Task.Run(async () =>
        {
            try
            {
                await primaryInstance.RedirectActivationToAsync(activation);
            }
            finally
            {
                completed.Set();
            }
        });

        IntPtr[] handles = [completed.SafeWaitHandle.DangerousGetHandle()];
        int result = CoWaitForMultipleObjects(
            flags: 0,
            timeoutMilliseconds: Infinite,
            handleCount: 1,
            handles,
            out _);
        GC.KeepAlive(completed);
        Marshal.ThrowExceptionForHR(result);
        redirectTask.GetAwaiter().GetResult();
    }

    private static void OnActivated(object? sender, AppActivationArguments args)
    {
        DispatcherQueue? dispatcher;
        lock (ActivationSync)
        {
            _activationPending = true;
            dispatcher = _dispatcherQueue;
        }

        if (dispatcher is not null)
        {
            _ = dispatcher.TryEnqueue(DispatchPendingActivation);
        }
    }

    private static void DispatchPendingActivation()
    {
        App? app;
        lock (ActivationSync)
        {
            if (!_activationPending || _app is null)
            {
                return;
            }

            _activationPending = false;
            app = _app;
        }

        app.ShowForRedirectedActivation();
    }

    [DllImport("ole32.dll")]
    private static extern int CoWaitForMultipleObjects(
        uint flags,
        uint timeoutMilliseconds,
        uint handleCount,
        IntPtr[] handles,
        out uint index);
}
