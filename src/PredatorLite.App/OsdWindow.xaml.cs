using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PredatorLite.App.ViewModels;
using Windows.Graphics;

namespace PredatorLite.App;

public sealed partial class OsdWindow : Window
{
    private const int WidthInDips = 356;
    private const int HeightInDips = 122;
    private const int WorkAreaMarginInDips = 18;
    private readonly AppWindow _appWindow;
    private bool _closed;

    public OsdWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        if (Content is FrameworkElement root)
        {
            root.DataContext = viewModel;
        }

        IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        ConfigureWindow(windowHandle, windowId);
    }

    public void ShowOverlay()
    {
        if (_closed)
        {
            return;
        }

        PositionInWorkArea();
        _appWindow.Show(activateWindow: false);
    }

    public void HideOverlay()
    {
        if (!_closed)
        {
            _appWindow.Hide();
        }
    }

    public void CloseOverlay()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        Close();
    }

    public void RebuildContent()
    {
        if (!_closed)
        {
            CpuFanLabel.Text = Application.Current.Resources["Metric.CpuFan"]?.ToString() ?? "CPU fan";
        }
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
        SystemBackdrop = new DesktopAcrylicBackdrop();

        long extendedStyle = NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GwlExStyle).ToInt64();
        extendedStyle |= NativeMethods.WsExTransparent | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(windowHandle, NativeMethods.GwlExStyle, new IntPtr(extendedStyle));

        const int windowCornerPreference = 33;
        const int roundCorner = 2;
        int preference = roundCorner;
        NativeMethods.DwmSetWindowAttribute(windowHandle, windowCornerPreference, ref preference, sizeof(int));

        PositionInWorkArea(windowId, windowHandle);
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
