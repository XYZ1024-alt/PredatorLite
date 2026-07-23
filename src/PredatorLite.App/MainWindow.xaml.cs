using System.ComponentModel;
using System.Runtime.InteropServices;
using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PredatorLite.App.Behaviors;
using PredatorLite.App.Services;
using PredatorLite.App.ViewModels;
using PredatorLite.App.Views;
using PredatorLite.Core.Abstractions;
using Windows.Graphics;

namespace PredatorLite.App;

public sealed partial class MainWindow : Window
{
    private const int InitialWidthInDips = 600;
    private const int InitialHeightInDips = 840;
    private const int MinimumWidthInDips = 560;
    private const int MinimumHeightInDips = 640;
    private const int MaximumWidthInDips = 640;
    private const int MaximumHeightInDips = 900;
    private readonly MainViewModel _viewModel;
    private readonly LocalizationService _localization;
    private readonly IAppLogger _logger;
    private readonly Func<Task> _exitRequested;
    private readonly UiMotionService _motion;
    private readonly AppWindow _appWindow;
    private readonly NativeWindowSubclass _windowSubclass;
    private readonly List<MainShell> _shellLayers = [];
    private MainShell? _shell;
    private TrayIconView? _trayIcon;
    private GlobalShortcutManager? _shortcuts;
    private OsdWindow? _osdWindow;
    private bool _allowClose;
    private bool _startHidden;
    private int _shellRebuildGeneration;

    public MainWindow(
        MainViewModel viewModel,
        LocalizationService localization,
        IAppLogger logger,
        Func<Task> exitRequested)
    {
        _viewModel = viewModel;
        _localization = localization;
        _logger = logger;
        _exitRequested = exitRequested;
        InitializeComponent();
        Title = "PredatorLite";
        _motion = new UiMotionService(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());

        WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _windowSubclass = new NativeWindowSubclass(WindowHandle);
        _windowSubclass.MessageReceived += OnWindowMessage;
        WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WindowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        ConfigureWindow(windowId);
        Activated += OnActivated;
        RebuildShell(animate: false);
        CreateTrayIcon();
        _localization.LanguageChanged += OnLanguageChanged;

        Closed += OnClosed;
        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
    }

    public IntPtr WindowHandle { get; }

    public void SetStartHidden(bool hidden) => _startHidden = hidden;

    public void ShowAndActivate()
    {
        this.Show();
        _trayIcon?.SetWindowVisible(true);
        Activate();
        NativeMethods.SetForegroundWindow(WindowHandle);
    }

    public void HideToTray()
    {
        this.Hide(enableEfficiencyMode: true);
        _trayIcon?.SetWindowVisible(false);
    }

    public void PrepareForExit()
    {
        _allowClose = true;
        Activated -= OnActivated;
        _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
        _shortcuts?.Dispose();
        _shortcuts = null;
        _windowSubclass.Dispose();
        _osdWindow?.CloseOverlay();
        _osdWindow = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _motion.Dispose();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args) =>
        WindowActivationSurface.SetWindowActive(
            args.WindowActivationState != WindowActivationState.Deactivated);

    private void ConfigureWindow(WindowId windowId)
    {
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = DesktopAcrylicController.IsSupported()
            ? new DesktopAcrylicBackdrop()
            : new MicaBackdrop { Kind = MicaKind.BaseAlt };
        DisplayArea displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        RectInt32 workArea = displayArea.WorkArea;
        double scale = Math.Max(1, NativeMethods.GetDpiForWindow(WindowHandle)) / 96d;
        int width = Math.Min(workArea.Width, (int)Math.Ceiling(InitialWidthInDips * scale));
        int height = Math.Min(workArea.Height, (int)Math.Ceiling(InitialHeightInDips * scale));
        _appWindow.Resize(new SizeInt32(width, height));
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
        }

        _appWindow.Move(new PointInt32(
            workArea.X + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - height) / 2)));

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

    private async void RebuildShell(bool animate)
    {
        try
        {
            await RebuildShellAsync(animate);
        }
        catch (Exception exception)
        {
            _logger.Error($"UI shell rebuild failed: {exception}");
        }
    }

    private async Task RebuildShellAsync(bool animate)
    {
        int generation = ++_shellRebuildGeneration;
        MainShell replacement = new(_viewModel, _motion);
        foreach (MainShell staleShell in _shellLayers.ToArray())
        {
            WindowHost.Children.Remove(staleShell);
            _shellLayers.Remove(staleShell);
        }

        int insertionIndex = _trayIcon is null
            ? WindowHost.Children.Count
            : Math.Max(0, WindowHost.Children.Count - 1);
        WindowHost.Children.Insert(insertionIndex, replacement);
        _shellLayers.Add(replacement);
        _shell = replacement;
        SetTitleBar(replacement.TitleBarDragRegion);

        if (animate)
        {
            await _motion.AnimateShellInAsync(replacement);
        }
        if (generation != _shellRebuildGeneration)
        {
            return;
        }
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new TrayIconView();
        _trayIcon.Configure(ShowAndActivate, () => _ = _exitRequested());
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
        bool visible = _viewModel.ShowOsd || _viewModel.ShowFps;
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
        if (e.PropertyName is nameof(MainViewModel.ShowOsd) or nameof(MainViewModel.ShowFps))
        {
            UpdateOsdVisibility();
        }
        else if (e.PropertyName == nameof(MainViewModel.EnableGlobalHotkeys))
        {
            ConfigureShortcuts();
        }
        else if (e.PropertyName == nameof(MainViewModel.CurrentLanguage))
        {
            RebuildShell(animate: true);
            _osdWindow?.RebuildContent();
        }
        else if (e.PropertyName == nameof(MainViewModel.IsInitialized) && _viewModel.IsInitialized)
        {
            ConfigureShortcuts();
            UpdateOsdVisibility();
            if (_startHidden || _viewModel.StartMinimized)
            {
                HideToTray();
            }
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
