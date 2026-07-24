using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using PredatorLite.App.Services;
using PredatorLite.App.ViewModels;
using PredatorLite.Core.Services;
using PredatorLite.Platform.Windows;
using PredatorLite.Platform.Windows.SystemIntegration;

namespace PredatorLite.App;

public partial class App : Application
{
    private FileAppLogger? _logger;
    private SingleInstanceService? _singleInstance;
    private MainViewModel? _viewModel;
    private MainWindow? _mainWindow;
    private int _launchStarted;
    private int _exitStarted;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledUiException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledProcessException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (Interlocked.Exchange(ref _launchStarted, 1) != 0)
        {
            return;
        }

        _logger = new FileAppLogger();
        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.IsPrimary)
        {
            await SingleInstanceService.SignalPrimaryAsync();
            await _singleInstance.DisposeAsync();
            _singleInstance = null;
            _logger.Dispose();
            _logger = null;
            Exit();
            return;
        }

        LocalizationService? localization = null;
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                throw new PlatformNotSupportedException("PredatorLite requires Windows 11 or later.");
            }

            DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
            localization = new LocalizationService();
            localization.SetLanguage("zh-CN");
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
                new DeferredFpsSource(() => new EtwFpsSource(_logger)),
                new FanGuardClient(_logger),
                new StartupManager(),
                new ElevatedHelperLauncher(),
                new DiagnosticsExporter(),
                localization,
                interaction,
                new WinUiDispatcher(dispatcher, _logger));

            bool startHidden = Environment.GetCommandLineArgs().Skip(1).Any(argument =>
                string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
            window = new MainWindow(
                _viewModel,
                localization,
                _logger,
                startHidden,
                () => ExitAsync(0));
            _mainWindow = window;
            _singleInstance.StartListening(() => dispatcher.TryEnqueue(window.ShowAndActivate));
            window.Activate();
            if (startHidden)
            {
                window.HideToTray();
            }

            _logger.Info(
                $"Startup tray ready in {stopwatch.ElapsedMilliseconds} ms: hidden={startHidden}.");
            await _viewModel.InitializeAsync();
            if (_viewModel.StartMinimized)
            {
                window.HideToTray();
            }
        }
        catch (Exception exception)
        {
            _logger.Error("PredatorLite startup failed", exception);
            string message = localization?.Get("Status.InitializationFailed") ??
                "PredatorLite could not initialize. Review the logs for details.";
            NativeMethods.ShowError(_mainWindow?.WindowHandle ?? IntPtr.Zero, message, "PredatorLite");
            await ExitAsync(1);
        }
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
            if (_viewModel is not null)
            {
                await _viewModel.DisposeAsync();
            }

            if (_singleInstance is not null)
            {
                await _singleInstance.DisposeAsync();
            }
        }
        catch (Exception exception)
        {
            _logger?.Error("PredatorLite shutdown failed", exception);
        }
        finally
        {
            _mainWindow?.Close();
            _logger?.Dispose();
            Exit();
        }
    }

    private void OnUnhandledUiException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _logger?.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
    }

    private void OnUnhandledProcessException(object? sender, System.UnhandledExceptionEventArgs e) =>
        _logger?.Error("Unhandled process exception", e.ExceptionObject as Exception);
}
