using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using PredatorLite.App.Services;
using PredatorLite.App.ViewModels;
using PredatorLite.Core.Abstractions;
using Forms = System.Windows.Forms;

namespace PredatorLite.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IAppLogger _logger;
    private readonly Forms.NotifyIcon _trayIcon;
    private GlobalShortcutManager? _shortcuts;
    private OsdWindow? _osdWindow;
    private bool _startHidden;
    private bool _allowClose;
    private bool _loaded;

    public MainWindow(MainViewModel viewModel, IAppLogger logger)
    {
        _viewModel = viewModel;
        _logger = logger;
        DataContext = viewModel;
        InitializeComponent();

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "PredatorLite",
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowAndActivate();
        RebuildTrayMenu();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    public void SetStartHidden(bool hidden) => _startHidden = hidden;

    public void ShowAndActivate()
    {
        ShowInTaskbar = true;
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await _viewModel.InitializeAsync();
        ConfigureShortcuts();
        UpdateOsdVisibility();
        if (_startHidden || _viewModel.StartMinimized)
        {
            ShowInTaskbar = false;
            Hide();
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        int enabled = 1;
        DwmSetWindowAttribute(handle, 20, ref enabled, Marshal.SizeOf<int>());
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            _trayIcon.Visible = false;
            _shortcuts?.Dispose();
            _osdWindow?.Close();
            return;
        }

        e.Cancel = true;
        ShowInTaskbar = false;
        Hide();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        ShowInTaskbar = false;
        Hide();
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void ConfigureShortcuts()
    {
        _shortcuts?.Dispose();
        _shortcuts = null;
        if (!_viewModel.EnableGlobalHotkeys || PresentationSource.FromVisual(this) is not HwndSource)
        {
            return;
        }

        _shortcuts = new GlobalShortcutManager(this, _logger);
        _shortcuts.ShowWindowRequested += (_, _) => Dispatcher.BeginInvoke(ShowAndActivate);
        _shortcuts.CycleModeRequested += (_, _) => Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await _viewModel.CycleModeFromShortcutAsync();
            }
            catch (Exception exception)
            {
                _logger.Error("Global mode shortcut failed", exception);
            }
        });
        _shortcuts.Register();
    }

    private void UpdateOsdVisibility()
    {
        bool shouldShow = _viewModel.ShowOsd || _viewModel.ShowFps;
        if (!shouldShow)
        {
            _osdWindow?.Hide();
            return;
        }

        _osdWindow ??= new OsdWindow(_viewModel);
        if (!_osdWindow.IsVisible)
        {
            _osdWindow.Show();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
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
            RebuildTrayMenu();
        }
    }

    private void RebuildTrayMenu()
    {
        Forms.ContextMenuStrip menu = new();
        Forms.ToolStripMenuItem open = new(
            System.Windows.Application.Current.TryFindResource("Action.Open")?.ToString() ?? "Open PredatorLite");
        open.Click += (_, _) => Dispatcher.BeginInvoke(ShowAndActivate);
        Forms.ToolStripMenuItem exit = new(
            System.Windows.Application.Current.TryFindResource("Action.Exit")?.ToString() ?? "Exit");
        exit.Click += (_, _) => Dispatcher.BeginInvoke(ExitApplication);
        menu.Items.Add(open);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exit);

        Forms.ContextMenuStrip? previous = _trayIcon.ContextMenuStrip;
        _trayIcon.ContextMenuStrip = menu;
        previous?.Dispose();
    }

    private void ExitApplication()
    {
        _allowClose = true;
        _trayIcon.Visible = false;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
