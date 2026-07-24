# Hardware Safety Boundary

## Validated write target

Hardware writes require all of the following:

- manufacturer contains `Acer`;
- model equals `Predator PHN16-71`;
- BIOS version equals `V1.20`;
- AcerService or the required Acer WMI transport is available.

A mismatch leaves telemetry and diagnostics available but returns `NotSupported` for writes.

Read-only CPU/GPU telemetry uses the ordinary-user Acer system monitor socket on
`127.0.0.1:46753`. LibreHardwareMonitor is restricted to GPU and memory sensors. Its CPU backend
remains disabled, so telemetry does not require a PawnIO ACL change, an elevated broker or a new
privileged service.

## Allowed controls

| Control | Values | Primary transport |
| --- | --- | --- |
| Operating mode | Silent, Balanced, Performance, Turbo, Eco | AcerService, verified WMI fallback |
| Fan mode | Auto, Max, Custom 20-100% | AcerService, documented WMI fallback |
| Graphics routing | Discrete direct `1`, Hybrid `2` | AcerService only |
| Battery health | 80% limit on/off | Acer battery WMI |
| Keyboard/logo lighting | documented effects, colors, brightness | AcerService |
| Device switches | only individually probed writable functions | AcerService or documented WMI |
| Refresh rate | modes already advertised by the active display | Windows display API |

## Startup operating-mode automation

Each primary-instance launch may restore one operating mode after identity, BIOS, power state and a usable control backend have been read. The target is the last successfully selected non-Eco mode, except that enabled battery automation selects Eco while on battery. Unknown power with battery automation enabled causes no write.

A freshly read matching mode sends no Acer write and only synchronizes the Windows power overlay. A changed mode uses the same serialized setter and read-back verification as an interactive request. Unsupported hardware, an unvalidated BIOS, a missing backend or an invalid saved enum value cannot bypass the write gate. No fan, lighting, GPU-routing, battery or device setting is replayed during startup.

## Explicitly excluded

- iGPU-only or Endurance modes
- disabling a display adapter in Windows
- user CPU/GPU overclocking
- PL1/PL2 or other power-limit modification
- undervolting
- PawnIO, IntelMSR or arbitrary EC access
- NVAPI writes
- BIOS or vBIOS writes and flashing tools
- firmware/ROM distribution

## Custom fan invariants

- Curve temperatures must increase.
- Fan speed cannot decrease as temperature rises.
- Speed is clamped to 20-100%.
- Both curves must reach 100% at 95°C.
- Runtime evaluation always forces 100% at or above 95°C.
- A missing temperature sensor is treated as 95°C while Custom mode is active.
- Max and Custom cannot be applied unless FanGuard has completed its handshake.

## Adding hardware support

Do not broaden the current model or BIOS check based only on a matching marketing name. A new target needs read-only protocol capture, value mapping, failure behavior, read-back verification and fan recovery testing. Add a separate explicit whitelist entry and tests for every new value.
