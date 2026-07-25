# Hardware Safety Boundary

## Profile-based write authorization

PredatorLite is an independent, unofficial PredatorSense alternative. Hardware writes are authorized by an explicit target profile, not by a Predator marketing-family match or by the presence of an Acer service.

A write requires all of the following:

- the current manufacturer, model and BIOS match one catalog profile by exact, case-insensitive value after trimming;
- the profile explicitly authorizes the requested control;
- the corresponding AcerService, Acer WMI or Windows display capability has been probed successfully;
- the operation remains within the profile's bounded values and transport policy.

The catalog currently contains one writable profile:

| Profile | Manufacturer aliases | Model | BIOS | Authorized controls |
| --- | --- | --- | --- | --- |
| `acer-predator-phn16-71-v1.20` | `Acer`, `Acer Incorporated` | `Predator PHN16-71` | `V1.20` | operating mode, fan, GPU MUX, battery health, lighting, individually probed device settings, advertised display rates |

An unknown model, BIOS or profile remains usable for telemetry and diagnostics but cannot write hardware state. A matching AcerService or WMI class never creates write authorization by itself.

Read-only CPU/GPU telemetry uses the ordinary-user Acer system monitor socket on `127.0.0.1:46753`. LibreHardwareMonitor is restricted to GPU and memory sensors. Its CPU backend remains disabled, so telemetry does not require a PawnIO ACL change, an elevated broker or a new privileged service.

## Allowed controls

| Control | Allowed values | Primary transport | Verification and recovery |
| --- | --- | --- | --- |
| Operating mode | Silent, Balanced, Performance, Turbo, Eco | AcerService, verified WMI fallback | AcerService/WMI read-back; matching startup state sends no Acer write |
| Fan mode | Auto, Max, Custom 20-100% | AcerService, documented WMI fallback | FanGuard lease for Max/Custom; mode read-back where available; failed bounded writes restore Auto |
| Graphics routing | Discrete direct `1`, Hybrid `2` | AcerService only | mode read-back; change reports reboot required |
| Battery health | 80% limit on/off | Acer battery WMI | status read-back |
| Keyboard/logo lighting | documented effects, colors, brightness | AcerService | keyboard and logo are reported as a complete operation only when both succeed |
| Device switches | only profile-authorized and individually probed writable functions | AcerService or documented WMI | function-specific read-back where available; unsupported functions remain read-only |
| Refresh rate | modes enumerated by the active display | Windows display API | current-rate read-back; Overdrive failure attempts refresh-rate rollback |

Profile authorization does not replace live capability probing. A control is unavailable when its backend, function probe or read-back requirement is not satisfied.

## Startup operating-mode automation

Each primary-instance launch may restore one operating mode after the target profile, identity, BIOS, power state and a usable control backend have been read. The target is the last successfully selected non-Eco mode, except that enabled battery automation selects Eco while on battery. Unknown power with battery automation enabled causes no write.

A freshly read matching mode sends no Acer write and only synchronizes the Windows power overlay. A changed mode uses the same serialized setter and read-back verification as an interactive request. An unknown profile, missing backend or invalid saved enum value cannot bypass the write gate. No fan, lighting, GPU-routing, battery or device setting is replayed during startup.

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
- wildcard or automatic authorization for an unverified Predator model or BIOS

## Custom fan invariants

- Curve temperatures must increase.
- Fan speed cannot decrease as temperature rises.
- Speed is clamped to 20-100%.
- Both curves must reach 100% at 95 C.
- Runtime evaluation always forces 100% at or above 95 C.
- A missing temperature sensor is treated as 95 C while Custom mode is active.
- Max and Custom cannot be applied unless the current process has an active FanGuard lease.
- If the lease or heartbeat is lost, no further Custom write is allowed; FanGuard restores Auto within the five-second recovery window.

## Privilege and recovery boundaries

The main app remains ordinary-user and `asInvoker`. ElevatedHelper accepts only `disable|restore`, the fixed conflict services, the fixed `\\PredatorSenseLauncher` task and the fixed `%ProgramData%\\PredatorLite\\service-backup.json` path. Backup entries are revalidated before any elevated service operation. The helper has no hardware-protocol dependency.

FanGuard independently resolves the current target profile before using AcerService or WMI for Auto recovery. An unknown profile exits without a hardware write. Recovery failures are logged and never reported as verified success.

GPU routing remains AcerService-only and reboot-confirmed. No profile may add arbitrary EC/MSR/NVAPI, BIOS, vBIOS, firmware or ROM write paths.

## Adding a hardware profile

Do not broaden write support based only on a matching marketing name. A new writable profile requires:

- exact manufacturer aliases, model and BIOS identity;
- OS/build and architecture metadata from the validation machine;
- independently authored protocol capture or observation evidence for every control;
- exact value mappings, capability probes and transport fallback rules;
- read-back behavior where supported;
- failure, partial-write and FanGuard recovery tests;
- manual validation on the exact model and BIOS;
- redacted evidence and an independent review record.

A target without this evidence may be recorded for read-only diagnostics only. Adding a profile must add an explicit catalog entry, per-control authorization and focused tests. It must never be implemented by deleting the profile gate.
