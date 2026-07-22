using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using PredatorLite.Core.Abstractions;

namespace PredatorLite.App.Services;

public sealed partial class GlobalShortcutManager : IDisposable
{
    private const uint WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const int ShowWindowId = 0x504C;
    private const int CycleModeId = 0x504D;
    private readonly IntPtr _window;
    private readonly DispatcherQueue _dispatcher;
    private readonly IAppLogger _logger;
    private NativeWindowSubclass? _windowSubclass;
    private bool _registered;

    public GlobalShortcutManager(IntPtr window, DispatcherQueue dispatcher, IAppLogger logger)
    {
        _window = window;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public event EventHandler? ShowWindowRequested;

    public event EventHandler? CycleModeRequested;

    public void Register()
    {
        if (_registered)
        {
            return;
        }

        _windowSubclass = new NativeWindowSubclass(_window);
        _windowSubclass.MessageReceived += OnWindowMessage;
        if (!RegisterHotKey(_window, ShowWindowId, ModControl | ModAlt, 0x7A))
        {
            _logger.Error("The window shortcut could not be registered.");
        }

        if (!RegisterHotKey(_window, CycleModeId, ModControl | ModAlt, 0x7B))
        {
            _logger.Error("The mode shortcut could not be registered.");
        }

        _registered = true;
    }

    public void Dispose()
    {
        if (!_registered)
        {
            return;
        }

        UnregisterHotKey(_window, ShowWindowId);
        UnregisterHotKey(_window, CycleModeId);
        if (_windowSubclass is not null)
        {
            _windowSubclass.MessageReceived -= OnWindowMessage;
            _windowSubclass.Dispose();
            _windowSubclass = null;
        }

        _registered = false;
    }

    private void OnWindowMessage(object? sender, NativeWindowMessageEventArgs e)
    {
        if (e.Message != WmHotkey)
        {
            return;
        }

        int id = e.WParam.ToInt32();
        if (id == ShowWindowId)
        {
            _dispatcher.TryEnqueue(() => ShowWindowRequested?.Invoke(this, EventArgs.Empty));
            e.Handled = true;
        }
        else if (id == CycleModeId)
        {
            _dispatcher.TryEnqueue(() => CycleModeRequested?.Invoke(this, EventArgs.Empty));
            e.Handled = true;
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr window, int id);
}
