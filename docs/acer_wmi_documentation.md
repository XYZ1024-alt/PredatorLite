# Acer Platform Interfaces

This document describes the AcerService, Acer system-monitor, WMI, and input interfaces currently used by PredatorLite. Hardware writes remain constrained by [`hardware-safety.md`](hardware-safety.md); interoperability provenance is recorded in [`protocol-provenance.md`](protocol-provenance.md).

## Code references

Protocol identifiers are centralized in:

- `src/PredatorLite.Platform.Windows/Acer/AcerProtocol.cs`
- `src/PredatorLite.Platform.Windows/Acer/AcerServiceClient.cs`
- `src/PredatorLite.Platform.Windows/Acer/AcerSystemMonitorClient.cs`
- `src/PredatorLite.Platform.Windows/Acer/AcerWmiClient.cs`
- `src/PredatorLite.Platform.Windows/Acer/LightingPayloadFactory.cs`

Do not scatter raw packet IDs, service function names, WMI methods, sensor IDs, or power-overlay GUIDs through UI code.

## Installed Acer components

| Component | PredatorLite use |
| --- | --- |
| `AcerServiceSvc` | AcerService state queries and validated hardware commands |
| `AcerLightingService` | Keyboard/logo lighting and related service routing |
| `ASMSvc` | Ordinary-user primary telemetry on TCP 46753 |
| `AcerQAAgentSvis` | Optional physical performance Mode-key notification channel |
| `AcerApplicationBaseDriver_Device` | Acer ACPI/WMI bridge used by installed services and WMI fallbacks |

The fixed conflict list and explicit administrator workflow are documented in [`service_dependencies.md`](service_dependencies.md). No service or scheduled task is changed during normal startup.

## AcerService TCP

Host: `127.0.0.1`

| Port | Purpose |
| --- | --- |
| `46933` | Command socket |
| `46753` | Acer system-monitor telemetry socket |

Command packets use this frame:

```text
0..3   ASCII "ACER"
4..7   uint32 little-endian packet ID
8..n   JSON payload, optionally AES-ECB encrypted
```

The optional AES value is read from `HKCU\Software\Acer\XSense\AESkey` at runtime. The value is machine-local and is never stored in the repository or diagnostic exports.

Packet IDs used by PredatorLite:

| ID | Purpose |
| ---: | --- |
| `0` | Initialization/handshake |
| `10` | `GET_MONITOR_DATA` telemetry |
| `20` | Query current state |
| `100` | Apply a validated device setting |

Set functions used by the platform layer:

`LIGHTING`, `OPERATING_MODE`, `FAN_CONTROL`, `WIN_KEY`, `STICKY_KEY`, `BOOT_SOUND`, `LCD_OVERDRIVE`, `GPU_MODE`, `PANEL_DFR_MODE`, `SOUND_MODE`

Query functions include:

`OPERATING_MODE`, `FAN_CONTROL`, `LIGHTING`, `GPU_MODE`, `WIN_KEY`, `STICKY_KEY`, `BOOT_SOUND`, `LCD_OVERDRIVE`, `PANEL_DFR_MODE`, `SOUND_MODE`, `BATTERY_BOOST`

Every write first passes the exact model/BIOS capability gate. Supported service operations use read-back verification where the endpoint exposes a query.

## Acer WMI

Namespace: `root\WMI`

| Class | PredatorLite use |
| --- | --- |
| `AcerGamingFunction` | Operating-mode fallback, fan behavior/speed fallback, sensor reads, and LCD-overdrive fallback |
| `APGeAction` | Keyboard-backlight timeout query/write |
| `BatteryControl` | Battery charge-limit query/write |

Primary methods are defined in `AcerProtocol` and invoked through `AcerWmiClient`. WMI access can be denied by the installed driver ACL; that failure leaves the corresponding capability unavailable and does not cause PredatorLite to request elevation.

## Operating modes

| Mode | Acer value |
| --- | ---: |
| Silent | `0x00` |
| Balanced | `0x01` |
| Performance | `0x04` |
| Turbo | `0x05` |
| Eco | `0x06` |

AcerService is attempted first. `AcerGamingFunction.SetGamingMiscSetting` is the validated fallback. Successful changes also select the matching Windows efficiency, balanced, or performance overlay.

## GPU routing

| UI mode | Acer value | Notes |
| --- | ---: | --- |
| Hybrid | `2` | Optimus/hybrid path |
| Discrete | `1` | Direct discrete path; requires restart |

PredatorLite does not expose iGPU-only/Endurance routing, disable a Windows display adapter, or perform NVAPI writes.

## Persistence

Application settings are stored in `%LocalAppData%\PredatorLite\settings.json` using temporary-file replacement and a `.bak` backup. Saved hardware selections are not replayed as writes during startup.

Service-conflict backups are stored separately at `%ProgramData%\PredatorLite\service-backup.json` and are accessible only through the fixed elevated-helper command surface.

## Physical performance Mode key

The performance Mode key uses Acer Quick Access over `wss://localhost:5141/`. `QuickAccessModeKeySource` authenticates with fixed localhost protocol constants and subscribes to `FunctionQuery` / `KeyEvent` notifications. These constants are interoperability values, not user credentials.

This channel requires `AcerQAAgentSvis`. If it is unavailable, users can still select modes through the UI or `Ctrl+Alt+F12`.

## PredatorSense launch key

The separate PredatorSense launch key is a keyboard input, not a Quick Access notification. On the validated PHN16-71 it arrives with scan code `0x75`. While PredatorLite is running, its low-level keyboard listener consumes physical transitions and toggles the main window once on key release. Injected and Unicode packet events are deliberately ignored.

PredatorLite does not install a system launcher for this key and does not modify `PredatorSenseLauncher` automatically. The key cannot cold-start PredatorLite after the process exits. The explicit conflict-management action remains available if an Acer software version launches PredatorSense through a separate channel.
