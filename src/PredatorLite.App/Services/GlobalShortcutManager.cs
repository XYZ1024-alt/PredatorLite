using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PredatorLite.Core.Abstractions;

namespace PredatorLite.App.Services;

public sealed class GlobalShortcutManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const int ShowWindowId = 0x504C;
    private const int CycleModeId = 0x504D;
    private readonly Window _window;
    private readonly IAppLogger _logger;
    private HwndSource? _source;
    private IntPtr _handle;

    public GlobalShortcutManager(Window window, IAppLogger logger)
    {
        _window = window;
        _logger = logger;
    }

    public event EventHandler? ShowWindowRequested;

    public event EventHandler? CycleModeRequested;

    public void Register()
    {
        if (_handle != IntPtr.Zero)
        {
            return;
        }

        _handle = new WindowInteropHelper(_window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WindowProcedure);
        if (!RegisterHotKey(_handle, ShowWindowId, ModControl | ModAlt, 0x7A))
        {
            _logger.Error("The window shortcut could not be registered.");
        }

        if (!RegisterHotKey(_handle, CycleModeId, ModControl | ModAlt, 0x7B))
        {
            _logger.Error("The mode shortcut could not be registered.");
        }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        UnregisterHotKey(_handle, ShowWindowId);
        UnregisterHotKey(_handle, CycleModeId);
        _source?.RemoveHook(WindowProcedure);
        _source = null;
        _handle = IntPtr.Zero;
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotkey)
        {
            return IntPtr.Zero;
        }

        int id = wParam.ToInt32();
        if (id == ShowWindowId)
        {
            ShowWindowRequested?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        else if (id == CycleModeId)
        {
            CycleModeRequested?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
