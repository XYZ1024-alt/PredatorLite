using System.ComponentModel;
using System.Runtime.InteropServices;
using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PredatorLite.App.Behaviors;
using PredatorLite.App.Services;
using PredatorLite.App.ViewModels;
using PredatorLite.App.Views;
using PredatorLite.Core.Abstractions;
using PredatorLite.Platform.Windows.SystemIntegration;
using Windows.Graphics;

namespace PredatorLite.App;

public sealed partial class MainWindow : Window, IDisposable
{
    private const int InitialWidthInDips = 600;
    private const int InitialHeightInDips = 840;
    private const int MinimumWidthInDips = 560;
    private const int MinimumHeightInDips = 640;
    private const int MaximumWidthInDips = 640;
    private const int MaximumHeightInDips = 900;
    private const int WorkAreaMarginInDips = 12;
    private readonly MainViewModel _viewModel;
    private readonly LocalizationService _localization;
    private readonly IAppLogger _logger;
    private readonly Func<int, Task> _exitRequested;
    private readonly UiMotionService _motion;
    private readonly AppWindow _appWindow;
    private readonly InputNonClientPointerSource _nonClientPointerSource;
    private readonly NativeWindowSubclass _windowSubclass;
    private readonly PredatorKeySource _predatorKeySource;
    private readonly TaskCompletionSource _trayReady = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<MainShell> _shellLayers = [];
    private MainShell? _shell;
    private XamlRoot? _shellXamlRoot;
    private TrayIconView? _trayIcon;
    private GlobalShortcutManager? _shortcuts;
    private OsdWindow? _osdWindow;
    private bool _allowClose;
    private bool _disposed;
    private bool _shellReadyReported;
    private readonly bool _startHidden;
    private int _shellRebuildGeneration;

    public MainWindow(
        MainViewModel viewModel,
        LocalizationService localization,
        IAppLogger logger,
        bool startHidden,
        Func<int, Task> exitRequested)
    {
        _viewModel = viewModel;
        _localization = localization;
        _logger = logger;
        _startHidden = startHidden;
        _exitRequested = exitRequested;
        _predatorKeySource = new PredatorKeySource(logger);
        InitializeComponent();
        Title = "PredatorLite";
        _motion = new UiMotionService(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());

        WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _windowSubclass = new NativeWindowSubclass(WindowHandle);
        _windowSubclass.MessageReceived += OnWindowMessage;
        WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WindowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _nonClientPointerSource = InputNonClientPointerSource.GetForWindowId(windowId);
        ConfigureWindow(windowId);
        Activated += OnActivated;
        if (!startHidden)
        {
            EnsureShell();
        }

        _localization.LanguageChanged += OnLanguageChanged;

        Closed += OnClosed;
        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        _predatorKeySource.ActivationRequested += OnPredatorKeyActivationRequested;
        if (startHidden)
        {
            CreateTrayIconAndSignal();
        }
    }

    public IntPtr WindowHandle { get; }

    public Task TrayReady => _trayReady.Task;

    public void ShowAndActivate()
    {
        try
        {
            EnsureShell();
        }
        catch (Exception exception)
        {
            _logger.LogError("UI shell creation failed during activation", exception);
            NativeMethods.ShowError(
                WindowHandle,
                _localization.Get("Status.InitializationFailed"),
                "PredatorLite");
            _ = _exitRequested(1);
            return;
        }

        _shell?.ResetCaptionButtonVisualStates();
        if (_appWindow.Presenter is OverlappedPresenter
            {
                State: OverlappedPresenterState.Minimized
            } presenter)
        {
            presenter.Restore(activateWindow: false);
        }

        PositionAtBottomRight(useInitialSize: false);
        this.Show();
        TrayIconView.SetWindowVisible(true);
        Activate();
        if (!NativeMethods.SetForegroundWindow(WindowHandle))
        {
            _logger.Info("Windows denied a request to foreground the main window.");
        }
    }

    public void HideToTray()
    {
        _shell?.ResetCaptionButtonVisualStates();
        this.Hide(enableEfficiencyMode: true);
        TrayIconView.SetWindowVisible(false);
    }

    public void PrepareForExit() => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _allowClose = true;
        TryCleanup(() => Activated -= OnActivated, "window activation handler");
        TryCleanup(
            () => _viewModel.PropertyChanged -= ViewModelOnPropertyChanged,
            "view-model handler");
        TryCleanup(
            () => _localization.LanguageChanged -= OnLanguageChanged,
            "localization handler");
        TryCleanup(
            () => _predatorKeySource.ActivationRequested -= OnPredatorKeyActivationRequested,
            "Predator key handler");
        TryCleanup(_predatorKeySource.Dispose, "Predator key source");
        TryCleanup(() => _shortcuts?.Dispose(), "global shortcuts");
        _shortcuts = null;
        TryCleanup(ReleaseTitleBarInput, "title-bar input regions");
        TryCleanup(_windowSubclass.Dispose, "window subclass");
        TryCleanup(() => _osdWindow?.Dispose(), "OSD window");
        _osdWindow = null;
        TryCleanup(() => _trayIcon?.Dispose(), "tray icon");
        _trayIcon = null;
        TryCleanup(() => CompositionTarget.Rendering -= OnFirstShellRendering, "startup rendering handler");
        TryCleanup(_motion.Dispose, "motion service");
        GC.SuppressFinalize(this);
    }

    private void TryCleanup(Action cleanup, string component)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            _logger.LogError($"Failed to release {component}", exception);
        }
    }

    private void ReleaseTitleBarInput()
    {
        foreach (MainShell shell in _shellLayers)
        {
            shell.Loaded -= OnShellLoaded;
            shell.SizeChanged -= OnShellSizeChanged;
        }

        if (_shellXamlRoot is not null)
        {
            _shellXamlRoot.Changed -= OnShellXamlRootChanged;
            _shellXamlRoot = null;
        }

        _nonClientPointerSource.ClearRegionRects(NonClientRegionKind.Passthrough);
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args) =>
        WindowActivationSurface.SetWindowActive(
            args.WindowActivationState != WindowActivationState.Deactivated);

    private void ConfigureWindow(WindowId windowId)
    {
        ExtendsContentIntoTitleBar = true;
        _appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
        }

        PositionAtBottomRight(windowId, useInitialSize: true);
    }

    private void ApplyDeferredWindowChrome()
    {
        SystemBackdrop = DesktopAcrylicController.IsSupported()
            ? new DesktopAcrylicBackdrop()
            : new MicaBackdrop { Kind = MicaKind.BaseAlt };
        WindowHost.Background = null;

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "PredatorLiteFluent.ico");
        if (File.Exists(iconPath))
        {
            _appWindow.SetIcon(iconPath);
        }
    }

    private void OnWindowMessage(object? sender, NativeWindowMessageEventArgs e)
    {
        if (e.Message != NativeMethods.WmGetMinMaxInfo)
        {
            return;
        }

        NativeMethods.ApplySizeConstraints(
            WindowHandle,
            e.LParam,
            MinimumWidthInDips,
            MinimumHeightInDips,
            MaximumWidthInDips,
            MaximumHeightInDips);
        e.Handled = true;
    }

    private void PositionAtBottomRight(bool useInitialSize) =>
        PositionAtBottomRight(_appWindow.Id, useInitialSize);

    private void PositionAtBottomRight(WindowId windowId, bool useInitialSize)
    {
        DisplayArea displayArea = GetTargetDisplayArea(windowId);
        RectInt32 workArea = displayArea.WorkArea;
        if (workArea.Width <= 0 || workArea.Height <= 0)
        {
            return;
        }

        PointInt32 dpiProbe = new(
            workArea.X + (workArea.Width / 2),
            workArea.Y + (workArea.Height / 2));
        double targetScale = NativeMethods.GetDpiForPoint(dpiProbe, WindowHandle) / 96d;
        int width;
        int height;
        if (useInitialSize)
        {
            width = (int)Math.Ceiling(InitialWidthInDips * targetScale);
            height = (int)Math.Ceiling(InitialHeightInDips * targetScale);
        }
        else
        {
            uint currentDpi = NativeMethods.GetDpiForWindow(WindowHandle);
            double currentScale = (currentDpi == 0 ? 96u : currentDpi) / 96d;
            SizeInt32 currentSize = _appWindow.Size;
            width = (int)Math.Round(currentSize.Width / currentScale * targetScale);
            height = (int)Math.Round(currentSize.Height / currentScale * targetScale);
        }

        width = Math.Clamp(width, 1, workArea.Width);
        height = Math.Clamp(height, 1, workArea.Height);
        int margin = (int)Math.Ceiling(WorkAreaMarginInDips * targetScale);
        int x = Math.Max(workArea.X, workArea.X + workArea.Width - width - margin);
        int y = Math.Max(workArea.Y, workArea.Y + workArea.Height - height - margin);
        _appWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private static DisplayArea GetTargetDisplayArea(WindowId windowId)
    {
        if (NativeMethods.TryGetCursorPosition(out PointInt32 cursorPosition))
        {
            return DisplayArea.GetFromPoint(cursorPosition, DisplayAreaFallback.Nearest);
        }

        return DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
    }

    private void OnPredatorKeyActivationRequested(object? sender, EventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        if (DispatcherQueue.HasThreadAccess)
        {
            ToggleWindowFromPredatorKey();
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_allowClose)
            {
                ToggleWindowFromPredatorKey();
            }
        });
    }

    private void ToggleWindowFromPredatorKey()
    {
        if (_allowClose)
        {
            return;
        }

        bool isForeground = NativeMethods.IsWindowVisible(WindowHandle) &&
            NativeMethods.GetForegroundWindow() == WindowHandle;
        if (isForeground)
        {
            HideToTray();
            return;
        }

        ShowAndActivate();
    }

    private void MinimizeWindow()
    {
        _shell?.ResetCaptionButtonVisualStates();
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Minimize(activateWindow: false);
        }
    }

    private void EnsureShell()
    {
        if (_shell is null)
        {
            ReplaceShell(out _);
        }
    }

    private async void RebuildShell(bool animate)
    {
        try
        {
            MainShell replacement = ReplaceShell(out int generation);
            if (animate)
            {
                await _motion.AnimateShellInAsync(replacement);
            }

            if (generation != _shellRebuildGeneration)
            {
                return;
            }
        }
        catch (Exception exception)
        {
            _logger.LogError("UI shell rebuild failed", exception);
        }
    }

    private MainShell ReplaceShell(out int generation)
    {
        generation = ++_shellRebuildGeneration;
        MainShell replacement = new(
            _viewModel,
            _motion,
            _logger,
            MinimizeWindow,
            HideToTray,
            deferHomeContent: !_shellReadyReported);
        foreach (MainShell staleShell in _shellLayers.ToArray())
        {
            staleShell.Loaded -= OnShellLoaded;
            staleShell.SizeChanged -= OnShellSizeChanged;
            WindowHost.Children.Remove(staleShell);
            _shellLayers.Remove(staleShell);
        }

        replacement.Loaded += OnShellLoaded;
        replacement.SizeChanged += OnShellSizeChanged;
        int insertionIndex = _trayIcon is null
            ? WindowHost.Children.Count
            : Math.Max(0, WindowHost.Children.Count - 1);
        WindowHost.Children.Insert(insertionIndex, replacement);
        _shellLayers.Add(replacement);
        _shell = replacement;
        SetTitleBar(replacement.TitleBarDragRegion);
        replacement.ResetCaptionButtonVisualStates();
        _logger.Info($"UI shell created: section={_viewModel.SelectedSection}.");
        return replacement;
    }

    private void OnShellLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainShell shell)
        {
            return;
        }

        shell.Loaded -= OnShellLoaded;
        if (!ReferenceEquals(shell, _shell) || shell.XamlRoot is not XamlRoot xamlRoot)
        {
            return;
        }

        if (!ReferenceEquals(_shellXamlRoot, xamlRoot))
        {
            if (_shellXamlRoot is not null)
            {
                _shellXamlRoot.Changed -= OnShellXamlRootChanged;
            }

            _shellXamlRoot = xamlRoot;
            _shellXamlRoot.Changed += OnShellXamlRootChanged;
        }

        UpdateCaptionButtonInputRegion(shell);
        if (!_shellReadyReported)
        {
            CompositionTarget.Rendering += OnFirstShellRendering;
        }
    }

    private void OnFirstShellRendering(object? sender, object args)
    {
        CompositionTarget.Rendering -= OnFirstShellRendering;
        if (_shellReadyReported)
        {
            return;
        }

        _shellReadyReported = true;
        StartupTelemetry.Mark("shell-ready");
        if (!DispatcherQueue.TryEnqueue(CompleteVisibleStartup))
        {
            CompleteVisibleStartup();
        }
    }

    private void CompleteVisibleStartup()
    {
        CreateTrayIconAndSignal();
        try
        {
            _shell?.LoadDeferredHomeContent();
        }
        catch (Exception exception)
        {
            _logger.LogError("Deferred Home content initialization failed", exception);
        }
    }

    private void OnShellSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is MainShell shell && ReferenceEquals(shell, _shell))
        {
            UpdateCaptionButtonInputRegion(shell);
        }
    }

    private void OnShellXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (ReferenceEquals(sender, _shellXamlRoot) && _shell is not null)
        {
            UpdateCaptionButtonInputRegion(_shell);
        }
    }

    private void UpdateCaptionButtonInputRegion(MainShell shell)
    {
        FrameworkElement region = shell.CaptionButtonInputRegion;
        XamlRoot? xamlRoot = shell.XamlRoot;
        if (xamlRoot is null ||
            region.ActualWidth <= 0 ||
            region.ActualHeight <= 0 ||
            xamlRoot.RasterizationScale <= 0)
        {
            return;
        }

        GeneralTransform transform = region.TransformToVisual(WindowHost);
        Windows.Foundation.Rect bounds = transform.TransformBounds(
            new Windows.Foundation.Rect(0, 0, region.ActualWidth, region.ActualHeight));
        double scale = xamlRoot.RasterizationScale;
        int left = (int)Math.Floor(bounds.X * scale);
        int top = (int)Math.Floor(bounds.Y * scale);
        int right = (int)Math.Ceiling((bounds.X + bounds.Width) * scale);
        int bottom = (int)Math.Ceiling((bounds.Y + bounds.Height) * scale);
        if (right <= left || bottom <= top)
        {
            return;
        }

        _nonClientPointerSource.SetRegionRects(
            NonClientRegionKind.Passthrough,
            [new RectInt32(left, top, right - left, bottom - top)]);
    }

    private void CreateTrayIconAndSignal()
    {
        if (_trayReady.Task.IsCompleted)
        {
            return;
        }

        try
        {
            CreateTrayIcon();
            ApplyDeferredWindowChrome();
            _predatorKeySource.Start();
            _trayReady.TrySetResult();
        }
        catch (Exception exception)
        {
            _trayReady.TrySetException(exception);
        }
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new TrayIconView();
        _trayIcon.Configure(ShowAndActivate, () => _ = _exitRequested(0));
        _trayIcon.RebuildMenu(_localization);
        WindowHost.Children.Add(_trayIcon);
        _trayIcon.ForceCreate();
    }

    private void ConfigureShortcuts()
    {
        _shortcuts?.Dispose();
        _shortcuts = null;
        if (!_viewModel.EnableGlobalHotkeys)
        {
            return;
        }

        _shortcuts = new GlobalShortcutManager(WindowHandle, DispatcherQueue, _logger);
        _shortcuts.ShowWindowRequested += (_, _) => ShowAndActivate();
        _shortcuts.CycleModeRequested += (_, _) => _ = _viewModel.CycleModeFromShortcutAsync();
        _shortcuts.Register();
    }

    private void UpdateOsdVisibility()
    {
        bool visible = _viewModel.ShowOsd;
        if (!visible)
        {
            _osdWindow?.HideOverlay();
            return;
        }

        _osdWindow ??= new OsdWindow(_viewModel, _motion);
        _osdWindow.ShowOverlay();
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ShowOsd))
        {
            if (_viewModel.IntegrationsReady)
            {
                UpdateOsdVisibility();
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.EnableGlobalHotkeys))
        {
            if (_viewModel.IntegrationsReady)
            {
                ConfigureShortcuts();
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.CurrentLanguage))
        {
            if (_shell is not null)
            {
                RebuildShell(animate: true);
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.IsInitialized) && _viewModel.IsInitialized)
        {
            if (_startHidden || _viewModel.StartMinimized)
            {
                HideToTray();
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.IntegrationsReady) && _viewModel.IntegrationsReady)
        {
            ConfigureShortcuts();
            UpdateOsdVisibility();
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        _trayIcon?.RebuildMenu(_localization);

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Handled = true;
        HideToTray();
    }
}
