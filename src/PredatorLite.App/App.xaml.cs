using System.Windows;
using System.Windows.Threading;
using PredatorLite.App.Services;
using PredatorLite.App.ViewModels;
using PredatorLite.Core.Abstractions;
using PredatorLite.Core.Services;
using PredatorLite.Platform.Windows;
using PredatorLite.Platform.Windows.SystemIntegration;

namespace PredatorLite.App;

public partial class App : System.Windows.Application
{
    private FileAppLogger? _logger;
    private SingleInstanceService? _singleInstance;
    private MainViewModel? _viewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _logger = new FileAppLogger();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.IsPrimary)
        {
            await SingleInstanceService.SignalPrimaryAsync();
            await _singleInstance.DisposeAsync();
            _singleInstance = null;
            _logger.Dispose();
            _logger = null;
            Shutdown();
            return;
        }

        try
        {
            LocalizationService localization = new();
            JsonSettingsStore settings = new();
            PredatorPlatform platform = new(_logger);
            QuickAccessModeKeySource modeKey = new(_logger);
            DeferredFpsSource fps = new(() => new EtwFpsSource(_logger));
            FanGuardClient fanGuard = new(_logger);
            _viewModel = new MainViewModel(
                platform,
                settings,
                _logger,
                modeKey,
                fps,
                fanGuard,
                new StartupManager(),
                new ElevatedHelperLauncher(),
                new DiagnosticsExporter(),
                localization,
                new DesktopUserInteraction());

            await _viewModel.InitializeAsync();
            MainWindow window = new(_viewModel, _logger);
            MainWindow = window;
            bool startHidden = e.Args.Any(argument =>
                string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
            window.SetStartHidden(startHidden);
            _singleInstance.StartListening(() => Dispatcher.BeginInvoke(window.ShowAndActivate));

            if (startHidden || _viewModel.StartMinimized)
            {
                window.ShowActivated = false;
                window.ShowInTaskbar = false;
                window.WindowState = WindowState.Minimized;
            }

            window.Show();
        }
        catch (Exception exception)
        {
            _logger.Error("PredatorLite startup failed", exception);
            System.Windows.MessageBox.Show(
                exception.Message,
                "PredatorLite",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _viewModel?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _singleInstance?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _logger?.Error("PredatorLite shutdown failed", exception);
        }
        finally
        {
            _logger?.Dispose();
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
        System.Windows.MessageBox.Show(
            e.Exception.Message,
            "PredatorLite",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        _logger?.Error("Unhandled process exception", e.ExceptionObject as Exception);
    }
}
