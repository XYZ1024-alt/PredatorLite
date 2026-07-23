using System.ComponentModel;
using System.Runtime.InteropServices;
using PredatorLite.Core.Abstractions;

namespace PredatorLite.Platform.Windows.SystemIntegration;

public sealed class PredatorKeySource : IDisposable
{
    private const int WhKeyboardLowLevel = 13;
    private readonly IAppLogger _logger;
    private readonly PredatorKeyState _state = new();
    private readonly LowLevelKeyboardProcedure _procedure;
    private IntPtr _hook;
    private bool _disposed;

    public PredatorKeySource(IAppLogger logger)
    {
        _logger = logger;
        _procedure = OnKeyboardMessage;
    }

    public event EventHandler? ActivationRequested;

    public bool Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hook != IntPtr.Zero)
        {
            return true;
        }

        _hook = SetWindowsHookEx(
            WhKeyboardLowLevel,
            _procedure,
            GetModuleHandle(null),
            0);
        if (_hook != IntPtr.Zero)
        {
            return true;
        }

        int error = Marshal.GetLastWin32Error();
        _logger.Error(
            "The Predator key hook could not be installed.",
            new Win32Exception(error));
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_hook == IntPtr.Zero)
        {
            _disposed = true;
            return;
        }

        if (!UnhookWindowsHookEx(_hook))
        {
            int error = Marshal.GetLastWin32Error();
            _logger.Error(
                "The Predator key hook could not be removed cleanly.",
                new Win32Exception(error));
            return;
        }

        _hook = IntPtr.Zero;
        _disposed = true;
    }

    private IntPtr OnKeyboardMessage(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0)
        {
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        LowLevelKeyboardInput input = Marshal.PtrToStructure<LowLevelKeyboardInput>(lParam);
        PredatorKeyDecision decision = _state.Handle(
            unchecked((uint)wParam.ToInt64()),
            input.VirtualKey,
            input.ScanCode,
            input.Flags);
        if (!decision.ShouldSuppress)
        {
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        if (decision.ShouldActivate)
        {
            try
            {
                ActivationRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                _logger.Error("The Predator key activation callback failed.", exception);
            }
        }

        return new IntPtr(1);
    }

    private delegate IntPtr LowLevelKeyboardProcedure(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelKeyboardInput
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        LowLevelKeyboardProcedure procedure,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}

internal sealed class PredatorKeyState
{
    internal const uint KeyDownMessage = 0x0100;
    internal const uint KeyUpMessage = 0x0101;
    internal const uint SystemKeyDownMessage = 0x0104;
    internal const uint SystemKeyUpMessage = 0x0105;
    internal const uint PredatorScanCode = 0x75;
    internal const uint PacketVirtualKey = 0xE7;
    internal const uint LowerIntegrityInjectedFlag = 0x02;
    internal const uint InjectedFlag = 0x10;

    private bool _isPressed;

    public PredatorKeyDecision Handle(uint message, uint virtualKey, uint scanCode, uint flags)
    {
        const uint injectedFlags = LowerIntegrityInjectedFlag | InjectedFlag;
        if (scanCode != PredatorScanCode ||
            virtualKey == PacketVirtualKey ||
            (flags & injectedFlags) != 0)
        {
            return default;
        }

        if (message is KeyDownMessage or SystemKeyDownMessage)
        {
            _isPressed = true;
            return new PredatorKeyDecision(ShouldSuppress: true, ShouldActivate: false);
        }

        if (message is not (KeyUpMessage or SystemKeyUpMessage))
        {
            return default;
        }

        bool shouldActivate = _isPressed;
        _isPressed = false;
        return new PredatorKeyDecision(ShouldSuppress: true, ShouldActivate: shouldActivate);
    }
}

internal readonly record struct PredatorKeyDecision(bool ShouldSuppress, bool ShouldActivate);
