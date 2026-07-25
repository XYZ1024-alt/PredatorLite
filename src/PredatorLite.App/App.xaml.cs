using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using PredatorLite.App.Services;
using PredatorLite.App.ViewModels;
using PredatorLite.Core.Services;
using PredatorLite.Platform.Windows;
using PredatorLite.Platform.Windows.SystemIntegration;

namespace PredatorLite.App;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WinUI owns the Application lifetime; ExitAsync releases all owned resources.")]
public partial class App : Application
{
    private FileAppLogger? _logger;
    private MainViewModel? _viewModel;
    private MainWindow? _mainWindow;
    private int _launchStarted;
    private int _redirectedActivationPending;
    private int _exitStarted;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledUiException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledProcessException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (Interlocked.Exchange(ref _launchStarted, 1) != 0)
        {
            return;
        }

        _logger = new FileAppLogger();

        LocalizationService? localization = null;
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100))
            {
                throw new PlatformNotSupportedException("PredatorLite requires Windows 11 24H2 or later.");
            }

            DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
            localization = new LocalizationService();
            localization.SetLanguage("zh-CN");
            StartupTelemetry.Mark("localization-ready");
            MainWindow? window = null;
            DesktopUserInteraction interaction = new(
                () => window?.Content is FrameworkElement element ? element.XamlRoot : null,
                () => window?.WindowHandle ?? IntPtr.Zero,
                localization,
                _logger);

            _viewModel = new MainViewModel(
                new PredatorPlatform(_logger),
                new JsonSettingsStore(),
                _logger,
                new QuickAccessModeKeySource(_logger),
                new FanGuardClient(_logger),
                localization,
                interaction,
                new WinUiDispatcher(dispatcher, _logger));

            string[] commandLineArguments = Environment.GetCommandLineArgs();
            bool startHidden = commandLineArguments.Skip(1).Any(argument =>
                string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
            bool startupTrayOnly = commandLineArguments.Skip(1).Any(argument =>
                string.Equals(argument, "--startup-tray-only", StringComparison.OrdinalIgnoreCase));
            window = new MainWindow(
                _viewModel,
                localization,
                _logger,
                startHidden,
                ExitAsync);
            _mainWindow = window;
            StartupTelemetry.Mark("window-created");
            window.Activate();
            if (startHidden)
            {
                window.HideToTray();
            }

            if (Interlocked.Exchange(ref _redirectedActivationPending, 0) != 0)
            {
                ShowForRedirectedActivation();
            }

            await window.TrayReady;
            StartupTelemetry.Mark("tray-ready");
            _logger.Info(
                $"Startup tray ready in {StartupTelemetry.ElapsedMilliseconds} ms: hidden={startHidden}.");
            if (startupTrayOnly)
            {
                _logger.Info("Startup measurement stopped before hardware initialization.");
                return;
            }

            await _viewModel.InitializeAsync();
            if (_viewModel.StartMinimized)
            {
                window.HideToTray();
            }
        }
        catch (Exception exception)
        {
            _logger.LogError("PredatorLite startup failed", exception);
            string message = localization?.Get("Status.InitializationFailed") ??
                "PredatorLite could not initialize. Review the logs for details.";
            NativeMethods.ShowError(_mainWindow?.WindowHandle ?? IntPtr.Zero, message, "PredatorLite");
            await ExitAsync(1);
        }
    }

    internal void ShowForRedirectedActivation()
    {
        if (Volatile.Read(ref _exitStarted) != 0)
        {
            return;
        }

        MainWindow? window = _mainWindow;
        if (window is null)
        {
            Interlocked.Exchange(ref _redirectedActivationPending, 1);
            return;
        }

        window.ShowAndActivate();
        StartupTelemetry.Mark("redirect-activated");
    }

    private async Task ExitAsync(int exitCode)
    {
        if (Interlocked.Exchange(ref _exitStarted, 1) != 0)
        {
            return;
        }

        Environment.ExitCode = exitCode;
        try
        {
            _mainWindow?.PrepareForExit();
        }
        catch (Exception exception)
        {
            _logger?.LogError("Window shutdown preparation failed", exception);
        }

        try
        {
            if (_viewModel is not null)
            {
                await _viewModel.DisposeAsync();
            }
        }
        catch (Exception exception)
        {
            _logger?.LogError("Application service shutdown failed", exception);
        }

        try
        {
            _mainWindow?.Close();
        }
        catch (Exception exception)
        {
            _logger?.LogError("Window close failed", exception);
        }

        try
        {
            _logger?.Dispose();
        }
        finally
        {
            Exit();
        }
    }

    private void OnUnhandledUiException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _logger?.LogError("Unhandled UI exception", e.Exception);
        e.Handled = true;
    }

    private void OnUnhandledProcessException(object? sender, System.UnhandledExceptionEventArgs e) =>
        _logger?.LogError("Unhandled process exception", e.ExceptionObject as Exception);
}
