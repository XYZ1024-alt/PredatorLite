# Acer Service Dependencies

PredatorLite uses Acer components already installed on the target machine. It does not bundle Acer services, drivers, firmware, or installers.

## Runtime components

| Component | Role in PredatorLite | Requirement |
| --- | --- | --- |
| `AcerServiceSvc` | AcerService command endpoint for operating modes, fan control, GPU routing, lighting, and supported device settings | Required for the full control surface |
| `AcerLightingService` | Routes keyboard/logo lighting and related AcerService commands | Required for lighting controls |
| `ASMSvc` | Ordinary-user CPU/GPU temperature, frequency, load, memory, and fan telemetry on localhost TCP 46753 | Recommended for complete primary telemetry; limited fallbacks remain available |
| `AcerQAAgentSvis` | Local Quick Access WebSocket notifications for the physical performance Mode key | Optional; not needed for UI controls, standard shortcuts, or the PredatorSense launch key |
| `AcerApplicationBaseDriver_Device` | Acer ACPI/WMI bridge used by installed Acer services and the WMI fallbacks | Required for Acer WMI functionality |

Missing components are treated as capability failures. PredatorLite keeps the affected controls read-only or unavailable instead of installing, starting, or replacing vendor components automatically.

## Explicit conflict management

The Settings page can explicitly disable and later restore this fixed conflict list:

- `AcerCCAgentSvis`
- `AcerDIAgentSvis`
- `AcerDeviceEnablingServiceV2`
- `PredatorService`
- the `PredatorSenseLauncher` scheduled task

This action requires administrator approval. Before making changes, `PredatorLite.ElevatedHelper` records the original service start modes, running states, and launcher-task state in `%ProgramData%\PredatorLite\service-backup.json`. Restore uses only that backup and the same fixed whitelist.

PredatorLite does not silently disable services during startup. The ordinary application process remains unelevated.

## Failure behavior

- If AcerService is unavailable, service-only controls such as lighting and GPU routing remain unavailable.
- If Acer WMI is unavailable or access is denied, WMI-only controls such as the charge limit or keyboard timeout remain unavailable.
- If `ASMSvc` is unavailable, Windows counters and the restricted LibreHardwareMonitor reader may provide partial telemetry; missing Acer-only fields remain unavailable.
- If `AcerQAAgentSvis` is unavailable, the physical performance Mode key is disabled, but UI mode selection and `Ctrl+Alt+F12` continue to work.

No fixed RAM, CPU, temperature, or battery-life savings are claimed. Resource impact depends on the installed Acer software version, enabled PredatorLite features, polling sources, and hardware. Measure it on the target system when comparing configurations.
