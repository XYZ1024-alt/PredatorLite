using System.Runtime.InteropServices;

namespace PredatorLite.App;

internal static partial class NativeMethods
{
    internal const int GwlExStyle = -20;
    internal const long WsExTransparent = 0x00000020L;
    internal const long WsExToolWindow = 0x00000080L;
    internal const long WsExNoActivate = 0x08000000L;
    internal const int SwHide = 0;
    internal const int SwShowNoActivate = 4;
    internal const uint WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr window, string text, string caption, uint type);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(IntPtr window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(IntPtr window, int command);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(IntPtr window);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static partial IntPtr GetWindowLongPtr(IntPtr window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static partial IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    internal static void ShowError(IntPtr window, string message, string title) =>
        MessageBox(window, message, title, 0x00000010);

    internal static void ApplySizeConstraints(
        IntPtr window,
        IntPtr minMaxInfoPointer,
        int minimumWidth,
        int minimumHeight,
        int maximumWidth,
        int maximumHeight)
    {
        MinMaxInfo info = Marshal.PtrToStructure<MinMaxInfo>(minMaxInfoPointer);
        double scale = Math.Max(1, GetDpiForWindow(window)) / 96d;
        int availableWidth = int.MaxValue;
        int availableHeight = int.MaxValue;
        IntPtr monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        MonitorInfo monitorInfo = new() { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref monitorInfo))
        {
            availableWidth = monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left;
            availableHeight = monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top;
        }

        int minimumTrackWidth = Math.Min(
            availableWidth,
            (int)Math.Ceiling(minimumWidth * scale));
        int minimumTrackHeight = Math.Min(
            availableHeight,
            (int)Math.Ceiling(minimumHeight * scale));
        int maximumTrackWidth = Math.Max(
            minimumTrackWidth,
            Math.Min(availableWidth, (int)Math.Ceiling(maximumWidth * scale)));
        int maximumTrackHeight = Math.Max(
            minimumTrackHeight,
            Math.Min(availableHeight, (int)Math.Ceiling(maximumHeight * scale)));

        info.MinimumTrackSize.X = minimumTrackWidth;
        info.MinimumTrackSize.Y = minimumTrackHeight;
        info.MaximumTrackSize.X = maximumTrackWidth;
        info.MaximumTrackSize.Y = maximumTrackHeight;
        Marshal.StructureToPtr(info, minMaxInfoPointer, false);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaximumSize;
        public Point MaximumPosition;
        public Point MinimumTrackSize;
        public Point MaximumTrackSize;
    }
}

internal sealed class NativeWindowSubclass : IDisposable
{
    private const uint WmNcDestroy = 0x0082;
    private static int _nextId;
    private readonly IntPtr _window;
    private readonly UIntPtr _id;
    private readonly SubclassProcedure _procedure;
    private bool _disposed;

    public NativeWindowSubclass(IntPtr window)
    {
        _window = window;
        _id = (UIntPtr)(uint)Interlocked.Increment(ref _nextId);
        _procedure = WindowProcedure;
        if (!SetWindowSubclass(window, _procedure, _id, UIntPtr.Zero))
        {
            throw new InvalidOperationException("The native window message hook could not be installed.");
        }
    }

    public event EventHandler<NativeWindowMessageEventArgs>? MessageReceived;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RemoveWindowSubclass(_window, _procedure, _id);
    }

    private IntPtr WindowProcedure(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr referenceData)
    {
        NativeWindowMessageEventArgs args = new(message, wParam, lParam);
        MessageReceived?.Invoke(this, args);
        if (message == WmNcDestroy)
        {
            Dispose();
        }

        return args.Handled
            ? args.Result
            : DefSubclassProc(window, message, wParam, lParam);
    }

    private delegate IntPtr SubclassProcedure(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr window,
        SubclassProcedure procedure,
        UIntPtr subclassId,
        UIntPtr referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        IntPtr window,
        SubclassProcedure procedure,
        UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}

internal sealed class NativeWindowMessageEventArgs(uint message, IntPtr wParam, IntPtr lParam) : EventArgs
{
    public uint Message { get; } = message;

    public IntPtr WParam { get; } = wParam;

    public IntPtr LParam { get; } = lParam;

    public bool Handled { get; set; }

    public IntPtr Result { get; set; }
}
