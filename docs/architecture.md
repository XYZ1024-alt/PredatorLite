# PredatorLite Architecture

## Runtime boundaries

```text
WinUI 3 UI / tray / OSD
        |
        v
MainViewModel ---- JSON settings / redacted logs / diagnostics ZIP
        |
        v
IPredatorPlatform
   |                |             |                |
   v                v             v                v
AcerService   AcerSysMonitor   Acer WMI     Windows read-only telemetry
TCP 46933     TCP 46753        provider     display, power, LHM, ETW FPS

MainViewModel -- named-pipe heartbeat --> FanGuard
Settings UI  -- explicit UAC action --> ElevatedHelper
```

`PredatorLite.Core` has no WinUI or Acer implementation dependency. The application talks to hardware only through `IPredatorPlatform`, which keeps protocol code and UI state separate. All Windows projects target Windows 11 24H2 (`10.0.26100+`) x64 on .NET 10. `PredatorLite.App` is an unpackaged, framework-dependent Windows App SDK 2.3.1 application; the release script publishes RID-specific ReadyToRun assemblies without embedding the .NET or Windows App Runtime.

## Startup sequence

Startup is split into a control-critical phase and deferred initialization:

1. Before XAML starts, the synchronous STA entry point initializes COM wrappers and registers `Microsoft.Windows.AppLifecycle.AppInstance`. A secondary process redirects activation with a COM-aware wait and exits; the primary marshals redirected activation to its UI dispatcher without rerunning initialization.
2. Start WinUI with `DispatcherQueueSynchronizationContext`, load the compiled language XBF, and create the lightweight window and tray. A `--background` launch does not create the shell or any page.
3. Start versioned, source-generated JSON settings loading and the read-only platform startup probe concurrently. Apply settings and the selected compiled resource dictionary on the UI thread.
4. Read identity/BIOS, power state, the AcerService operating-mode state and an operational Acer WMI mode fallback.
5. After the exact model/BIOS gate succeeds, restore the saved non-Eco mode, or Eco on battery when that automation is enabled. A freshly verified matching mode only reapplies the Windows power overlay and sends no Acer write.
6. Mark the critical path ready, then load full capabilities, the initial telemetry snapshot, optional listeners and the service inventory asynchronously.

A validated machine whose control backend is still starting receives read-only retries within one cancellation-aware ten-second deadline. Failure leaves the app usable and read-only; a backend first discovered by the deferred probe gets at most one pending startup restore attempt.

Only the operating mode is restored at startup. Fan settings, lighting, GPU routing, charge limits and device settings are never replayed. Hidden startup defers the complete `MainShell`; visible startup creates pages only when first navigated to.

The ordinary-user `AcerSysMonitorService` endpoint on `127.0.0.1:46753` supplies CPU/GPU temperature,
frequency, load and fan speed on every telemetry cycle. Acer WMI remains a temperature/fan fallback,
and Windows processor counters remain a CPU load/frequency fallback.

LibreHardwareMonitor is created only while the Monitor tab, OSD/FPS, or an explicitly enabled Custom
fan curve needs GPU power, VRAM, memory or other extended telemetry. Its CPU backend is always disabled
so PredatorLite does not initialize PawnIO or a privileged MSR path. ETW is loaded only after FPS is enabled.

## Hardware command sequence

Every control action follows the same boundary:

1. Confirm that model and BIOS match the exact write whitelist.
2. Serialize commands so AcerService/WMI operations cannot overlap.
3. Send only the command associated with the selected UI control.
4. Query the resulting state when the transport supports verification.
5. Report success from verified read-back or an explicit successful transport response; preserve the visible previous state on rejection or failure.

There is no BIOS fallback for GPU routing and no generic arbitrary command endpoint exposed to the UI.

## Fan ownership and failure recovery

Auto fan mode does not require a watchdog. Before Max or Custom is applied, the app launches `PredatorLite.FanGuard.exe` and completes a current-user-only named-pipe handshake.

The app sends a heartbeat every two seconds. FanGuard restores Auto when:

- no heartbeat arrives for five seconds;
- the parent process exits;
- the pipe disconnects;
- the app sends `STOP` during normal shutdown or an explicit Auto selection.

FanGuard first uses AcerService and falls back to the documented Acer WMI method. `PredatorPlatform` tracks fan ownership, so closing PredatorLite does not alter a Max or Custom state that existed before this process started.

Custom fan control runs from an immutable copy of the last validated curve. Editing sliders changes only the draft; runtime targets change after the user applies and validates the curve again.

## Privilege boundary

The WinUI process uses an `asInvoker` manifest. `PredatorLite.ElevatedHelper.exe` accepts exactly two commands, `disable` and `restore`, and only a fixed service list. Its backup path is restricted to `%ProgramData%\PredatorLite\service-backup.json`. No hardware protocol is available through the elevated helper.

## Persistence and diagnostics

Settings are written to `%LocalAppData%\PredatorLite\settings.json` through a temporary file followed by atomic replacement. The previous valid file is kept as `.bak`; invalid JSON is moved aside. `LastAcMode` retains the last successfully selected non-Eco operating mode and remains compatible with schema version 1.

Logs retain seven days and redact the current user profile path. Directory creation, retention cleanup, and buffered file writes run on one background writer; disposal drains queued entries. Diagnostic ZIP files and settings use source-generated JSON metadata. Diagnostic ZIP files include identity, capabilities, one telemetry snapshot, service state, settings and up to three redacted logs. They do not include protocol secrets or the AcerService AES registry value.

Startup and deployment measurements, the ReadyToRun decision, regression thresholds, and Native AOT audit blockers are documented in [`performance.md`](performance.md).
