using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PredatorLite.App.Services;
using PredatorLite.App.ViewModels;
using PredatorLite.App.Views;
using Windows.Graphics;

namespace PredatorLite.App;

public sealed partial class OsdWindow : Window, IDisposable
{
    private const int WidthInDips = 356;
    private const int HeightInDips = 122;
    private const int WorkAreaMarginInDips = 18;
    private readonly AppWindow _appWindow;
    private readonly UiMotionService _motion;
    private readonly OsdContent _content;
    private bool _closed;
    private bool _visible;
    private int _visibilityGeneration;

    public OsdWindow(MainViewModel viewModel, UiMotionService motion)
    {
        ViewModel = viewModel;
        _motion = motion;
        InitializeComponent();
        _content = new OsdContent(viewModel);
        OsdRoot.Children.Add(_content);
        Closed += OnClosed;

        IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        ConfigureWindow(windowHandle, windowId);
    }

    public MainViewModel ViewModel { get; }

    public void ShowOverlay()
    {
        if (_closed)
        {
            return;
        }

        int generation = ++_visibilityGeneration;
        _ = ShowOverlayAsync(generation);
    }

    public void HideOverlay()
    {
        if (_closed)
        {
            return;
        }

        int generation = ++_visibilityGeneration;
        _ = HideOverlayAsync(generation);
    }

    public void CloseOverlay()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _visibilityGeneration++;
        Close();
    }

    public void Dispose()
    {
        CloseOverlay();
        GC.SuppressFinalize(this);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        Closed -= OnClosed;
    }

    private void ConfigureWindow(IntPtr windowHandle, WindowId windowId)
    {
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }

        _appWindow.IsShownInSwitchers = false;
        SystemBackdrop = DesktopAcrylicController.IsSupported()
            ? new DesktopAcrylicBackdrop()
            : new MicaBackdrop { Kind = MicaKind.BaseAlt };

        long extendedStyle = NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GwlExStyle).ToInt64();
        extendedStyle |= NativeMethods.WsExTransparent | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(windowHandle, NativeMethods.GwlExStyle, new IntPtr(extendedStyle));

        const int windowCornerPreference = 33;
        const int roundCorner = 2;
        int preference = roundCorner;
        NativeMethods.DwmSetWindowAttribute(windowHandle, windowCornerPreference, ref preference, sizeof(int));

        const int windowBorderColor = 34;
        int noBorderColor = unchecked((int)0xFFFFFFFE);
        NativeMethods.DwmSetWindowAttribute(windowHandle, windowBorderColor, ref noBorderColor, sizeof(int));

        PositionInWorkArea(windowId, windowHandle);
    }

    private async Task ShowOverlayAsync(int generation)
    {
        PositionInWorkArea();
        _appWindow.Show(activateWindow: false);
        _visible = true;
        await _motion.AnimateOsdInAsync(OsdRoot);
        if (_closed || generation != _visibilityGeneration)
        {
            return;
        }

        _visible = true;
    }

    private async Task HideOverlayAsync(int generation)
    {
        if (!_visible)
        {
            _appWindow.Hide();
            return;
        }

        await _motion.AnimateOsdOutAsync(OsdRoot);
        if (_closed || generation != _visibilityGeneration)
        {
            return;
        }

        _appWindow.Hide();
        _visible = false;
    }

    private void PositionInWorkArea()
    {
        IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        PositionInWorkArea(windowId, windowHandle);
    }

    private void PositionInWorkArea(WindowId windowId, IntPtr windowHandle)
    {
        double scale = Math.Max(1, NativeMethods.GetDpiForWindow(windowHandle)) / 96d;
        int width = (int)Math.Ceiling(WidthInDips * scale);
        int height = (int)Math.Ceiling(HeightInDips * scale);
        int margin = (int)Math.Ceiling(WorkAreaMarginInDips * scale);
        DisplayArea displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        RectInt32 workArea = displayArea.WorkArea;
        _appWindow.MoveAndResize(new RectInt32(
            workArea.X + Math.Max(0, workArea.Width - width - margin),
            workArea.Y + margin,
            width,
            height));
    }
}
