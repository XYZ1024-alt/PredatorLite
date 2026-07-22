using System.ComponentModel;
using System.Runtime.InteropServices;
using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PredatorLite.App.Services;
using PredatorLite.App.ViewModels;
using PredatorLite.App.Views;
using PredatorLite.Core.Abstractions;
using Windows.Graphics;

namespace PredatorLite.App;

public sealed partial class MainWindow : Window
{
    private const int InitialWidthInDips = 1180;
    private const int InitialHeightInDips = 780;
    private readonly MainViewModel _viewModel;
    private readonly LocalizationService _localization;
    private readonly IAppLogger _logger;
    private readonly Func<Task> _exitRequested;
    private readonly AppWindow _appWindow;
    private readonly NativeWindowSubclass _windowSubclass;
    private MainShell? _shell;
    private TrayIconView? _trayIcon;
    private GlobalShortcutManager? _shortcuts;
    private OsdWindow? _osdWindow;
    private bool _allowClose;
    private bool _startHidden;

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

        WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _windowSubclass = new NativeWindowSubclass(WindowHandle);
        _windowSubclass.MessageReceived += OnWindowMessage;
        WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WindowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        ConfigureWindow(windowId);
        RebuildShell();
        CreateTrayIcon();

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
        _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        _shortcuts?.Dispose();
        _shortcuts = null;
        _windowSubclass.Dispose();
        _osdWindow?.CloseOverlay();
        _osdWindow = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    private void ConfigureWindow(WindowId windowId)
    {
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        DisplayArea displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        RectInt32 workArea = displayArea.WorkArea;
        double scale = Math.Max(1, NativeMethods.GetDpiForWindow(WindowHandle)) / 96d;
        int width = Math.Min(workArea.Width, (int)Math.Ceiling(InitialWidthInDips * scale));
        int height = Math.Min(workArea.Height, (int)Math.Ceiling(InitialHeightInDips * scale));
        _appWindow.Resize(new SizeInt32(width, height));
        _appWindow.Move(new PointInt32(
            workArea.X + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - height) / 2)));

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "PredatorLite.ico");
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

        NativeMethods.ApplyMinimumSize(WindowHandle, e.LParam, 980, 660);
        e.Handled = true;
    }

    private void RebuildShell()
    {
        MainShell replacement = new(_viewModel);
        WindowHost.Children.Insert(0, replacement);
        if (_shell is not null)
        {
            WindowHost.Children.Remove(_shell);
        }

        _shell = replacement;
        SetTitleBar(replacement.TitleBarDragRegion);
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new TrayIconView();
        _trayIcon.Configure(_localization, ShowAndActivate, () => _ = _exitRequested());
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

        _osdWindow ??= new OsdWindow(_viewModel);
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
            RebuildShell();
            _trayIcon?.RebuildMenu(_localization);
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
