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

`PredatorLite.Core` has no WinUI or Acer implementation dependency. The application talks to hardware only through `IPredatorPlatform`, which keeps protocol code and UI state separate. `PredatorLite.App` is an unpackaged, framework-dependent Windows App SDK 2.3 application targeting Windows 11 x64.

## Startup sequence

1. Acquire the per-session single-instance mutex.
2. Load versioned JSON settings and select the language resource dictionary.
3. Probe identity, AcerService, Acer system monitor, Acer WMI, supported display rates and individual device capabilities.
4. Read the initial hardware snapshot and service state.
5. Start lightweight Acer/power polling, tray integration and optional input listeners.

No hardware setter is called in this sequence. Saved modes, fan settings, lighting and MUX state are not replayed at startup.

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
5. Report success only after verification; otherwise preserve the visible previous state.

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

Settings are written to `%LocalAppData%\PredatorLite\settings.json` through a temporary file followed by atomic replacement. The previous valid file is kept as `.bak`; invalid JSON is moved aside.

Logs retain seven days and redact the current user profile path. Diagnostic ZIP files include identity, capabilities, one telemetry snapshot, service state, settings and up to three redacted logs. They do not include protocol secrets or the AcerService AES registry value.
