# PredatorLite Manual Test Checklist

Run this checklist on Windows 11 24H2 (build 26100+) x64. Hardware-write cases require a matching writable profile; the current writable profile is Acer Predator PHN16-71 with BIOS V1.20. On every other model or BIOS, verify that controls remain read-only instead.

## Preparation

1. Install .NET 10 Runtime x64 and Windows App Runtime 2.3 x64. Run `/winui-setup` if `winapp` is not available.
2. Keep `AcerServiceSvc`, `AcerLightingService`, `ASMSvc`, and `AcerApplicationBaseDriver_Device` installed and running.
3. Close every existing PredatorLite instance, including older WPF builds, before launching the WinUI build.
4. Build and launch the app as an ordinary user with `dotnet run --project src\PredatorLite.App\PredatorLite.App.csproj`. For a published directory, launch `PredatorLite.exe` from that complete directory. Do not elevate the main app.
5. For the repeatable, non-writing navigation and accessibility pass, run `build\ui-tests.ps1 -AppPid <PID>`.

## Window and navigation

1. Verify the initial window is approximately 600x840 DIPs and opens at the lower-right of the display containing the mouse pointer, with about 12 DIPs between the window and the work-area right/bottom edges.
2. Confirm the title bar exposes only the 46x40 DIP Minimize and Close buttons, with no maximize button or empty caption-button slot. With the pointer elsewhere, Close must use its normal transparent state; pointer entry and exit must apply and clear the critical hover state.
3. At 100%, 125%, and 150% display scaling, verify the window stays fully inside `DisplayArea.WorkArea`. Repeat with the taskbar on the bottom and one side, then with the pointer on each display of a mixed-DPI dual-monitor setup.
4. Verify the window cannot be resized below 560x640 DIPs or above 640x900 DIPs. Resize between those bounds; the bottom navigation must remain fixed while page content scrolls independently.
5. Confirm the bottom bar exposes five equal-width navigation items: Home, Cooling, Lighting, Monitor, and Settings. Home must not contain a status icon or status message.
6. Open each secondary page from the bottom bar. The selected state must follow the active page, and `Alt+Left` must return directly to Home.
7. Verify all pages at 100%, 125%, and 150% display scaling and at the minimum window size. No text, toggle, action button, or setting row may overlap or clip.
8. Switch Windows between light, dark, and a contrast theme. Desktop Acrylic, glass cards, text, semantic badges, focus indicators, controls, and both caption buttons must remain readable. With transparency disabled, the fallback surface must remain opaque and legible.
9. Switch between Chinese and English. The shell, caption-button automation names and tooltips, bottom navigation, tray menu, dialogs, OSD, mode names, and validation messages must update.
10. Trigger a read-only state, reboot-required state, and a recoverable error where practical. Only one notice may be visible, with priority Error, Reboot required, then Read-only; full error text must wrap instead of being truncated.
11. Click Minimize, then reopen through the tray, dedicated Predator key, or `Ctrl+Alt+F11`. The window must restore and receive focus without leaving either caption button highlighted.
12. Click Close and press `Alt+F4` in separate passes. Both close paths must hide the main window to the tray rather than terminate the process; reopening must show the Close button in its normal state while the pointer is elsewhere.
13. Drag the custom title region, open the system menu with right-click and `Alt+Space`, and double-click the title region. Dragging and the system menu must work, while double-click, `Win+Up`, and caption hover must expose no maximize or Snap Layout action.
14. Resize the window, hide it, move the pointer to another display, then left-click the tray icon or use its Open command. The same window must retain its logical size, move to that display's lower-right, and receive focus.
15. Start PredatorLite again through `winapp run`. No second main process should remain, and the existing window must move to the pointer display's lower-right and open with normal caption-button states.
16. Launch at least ten secondary processes concurrently while the primary is starting and again during the ten-second backend probe. Every secondary must exit, exactly one primary and tray icon must remain, the window must open on the UI thread, and startup mode restoration must run at most once.
17. Trigger a redirected launch while the primary window is not yet assigned, then during shutdown. The early activation must be delivered after tray creation; the shutdown activation may be ignored without an unhandled exception or recreated window.

## Page-specific UI

1. On Home, verify Silent, Balanced, Performance, and Turbo appear as four native radio-card controls, with no manual Eco tile.
2. Verify the first dashboard area contains Fan, Battery, Performance, and Lighting cards, and the Graphics and display card remains reachable by scrolling.
3. On Cooling, switch the CPU/GPU selector. Only the selected curve may be visible.
4. Edit several fan points. Validation must update inline without issuing a hardware write; Apply must disable while invalid.
5. Verify both final curve points are read-only at 95 degrees C and 100%.
6. On Lighting, select Static and verify the simplified keyboard shows four equal-width zones ordered from left to right. Each zone must show its number and full hex value on a neutral strip, remain readable for black, white, and bright colors, and open the color dialog without writing hardware until Apply is selected.
7. Switch Lighting to a dynamic effect. Verify the four editable zones are replaced by one unified primary-color keyboard preview, speed and direction become available, and switching back to Static restores the previous zone colors while hiding speed and direction. The normal state must not show a success badge; when unavailable, the read-only warning and disabled controls must remain visible.
8. Repeat the Lighting checks in Chinese and English at the minimum window width and 150% scaling, then in light, dark, and a contrast theme. The keyboard orientation labels, key outlines, hex values, focus visuals, bottom navigation, and scrolling must remain readable and unobstructed.
9. On Monitor, verify normal temperatures use neutral text and no live badge is shown. After three consecutive refresh failures, the stale indicator must appear; one successful refresh must clear it.
10. On Settings, verify application rows use SettingsCard, services use SettingsExpander, and no generic Device switches section is present. Service and diagnostic action pairs must be equal-sized with the secondary action on the left and the blue primary action on the right.

## Read-only telemetry

1. Launch as an ordinary, non-administrator user. Within one two-second refresh cycle, verify CPU and GPU temperature, load, frequency, and fan RPM contain plausible values without a UAC prompt.
2. Verify Monitor has no CPU package power row. GPU power, VRAM, and memory should populate when the corresponding hardware exposes them.
3. With Windows Memory integrity (HVCI) enabled, open Monitor and verify the same values populate. Review the log and confirm there is no PawnIO or CPU MSR initialization failure.
4. Stop `ASMSvc` for a failure test. CPU load/frequency should use Windows counters, GPU readings may use LibreHardwareMonitor on Monitor, and unavailable Acer-only temperature/fan fields must not retain fabricated values.
5. Restart `ASMSvc`, wait through the ten-second retry backoff, and verify Acer temperature/frequency/load/fan telemetry recovers without restarting PredatorLite. The log should contain at most one outage entry and one recovery entry.
6. Repeat several cold launches and power-mode changes. CPU frequency must remain within 0-10000 MHz and no higher than 125% of the service-reported maximum; discard transient values such as 40074 MHz.
7. Stop or block all applicable telemetry sources for three refresh cycles and verify the stale indicator appears. Restore any live primary source and verify it clears on the next successful snapshot.

## Settings and integration

1. Toggle start minimized and global shortcuts, restart, and verify the settings persist.
2. Enable startup, inspect `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\PredatorLite`, then disable it again.
3. Enable the OSD. The overlay must stay topmost, ignore mouse input, avoid taskbar/Alt+Tab, and close during app exit. Its CPU, GPU, and CPU fan columns must remain equal-width in Chinese and English.
4. Switch language while the OSD is visible. Every OSD label must update. Simulate stale telemetry and verify the OSD stale indicator appears and clears on recovery.
5. Verify `Ctrl+Alt+F11` opens the window at the pointer display's lower-right and `Ctrl+Alt+F12` cycles operating modes when global shortcuts are enabled.
6. On a PHN16-71, hide PredatorLite and press the PredatorSense key. It must open and focus PredatorLite without also opening PredatorSense. Press it while PredatorLite is visible in the background to reposition and focus the window, then press it while PredatorLite is in the foreground to hide it.
7. Hold the PredatorSense key and verify only one visibility change occurs on release. Disable global shortcuts and repeat; the dedicated key must remain active while `Ctrl+Alt+F11` and `Ctrl+Alt+F12` are disabled.
8. Exit PredatorLite from the tray and press the PredatorSense key. PredatorLite must not cold-start. If Acer software still launches PredatorSense through an independent channel while PredatorLite is running, use the explicit Disable conflicts action and repeat the test.
9. Press the separate physical Mode key and verify it still cycles exactly one operating mode per press.
10. Export diagnostics and confirm a ZIP is created at the selected path. Open Logs must open `%LocalAppData%\PredatorLite\logs`.

## Startup mode restoration

1. On the current writable PHN16-71 / BIOS V1.20 profile, select Silent, Balanced, Performance and Turbo in turn. After each selection, exit PredatorLite, change the hardware mode and launch PredatorLite manually; the saved mode must be restored and verified.
2. Repeat with Start with Windows enabled and `StartMinimized` on. The tray must appear before full telemetry and the saved mode must be restored without opening the shell.
3. Launch while the hardware already uses the saved mode. The startup log must report `already-active`; no Acer operating-mode set packet may be sent, while the matching Windows power overlay is still selected.
4. With battery Eco automation enabled, launch on battery and verify Eco, then reconnect AC and verify the saved non-Eco mode returns once. With automation disabled, a battery launch must restore the saved non-Eco mode.
5. Delay or stop AcerService on the current profile machine. Verify read-only startup probes stop at the ten-second deadline, never bypass the profile catalog, and do not loop hardware writes. If the deferred probe first discovers the backend, it may perform only one pending restore.
6. On an unknown model or BIOS, verify startup remains read-only and sends no hardware setter. A denied Acer WMI mode read must not make `CanWriteHardware` true when AcerService is unavailable. Confirm diagnostics record no writable target profile.
7. During hidden startup, verify no `MainShell` or page is created until the tray, dedicated key, or existing-instance activation shows the window. Navigate through every page after opening and confirm each page initializes once and remains functional.
8. Review EventSource provider `PredatorLite-Startup` and the startup timing lines from at least five healthy cold launches. `critical-ready` must precede `deferred-ready`; APGe, service inventory, and non-current page construction must remain outside the critical path.
9. Run both safe non-writing startup comparisons in [`performance.md`](performance.md). Confirm `Tray` emits `tray-ready`, `Shell` emits `shell-ready`, and both `--startup-tray-only` paths stop without `critical-ready`, a hardware setter, or a FanGuard launch.

## Hardware controls

1. Change one operating mode at a time and verify the visible state matches the next telemetry read.
2. Test Auto fan first. Before testing Max or Custom, confirm `PredatorLite.FanGuard.exe` starts and the UI shows FanGuard active.
3. While Max or Custom is active, terminate only `PredatorLite.exe` from Task Manager. Within five seconds FanGuard must restore Auto fan and then exit.
4. Apply a valid custom curve. Out-of-range, decreasing, non-increasing, or non-100%-at-95C curves must be rejected without changing fan ownership.
5. Test the charge limit, refresh rate, overdrive, and lighting controls, then verify their reported state.
6. Test Hybrid and Discrete GPU routing only when a reboot is acceptable. Cancel must preserve the previous selection; confirm must show the reboot-required banner.

## Installer

1. Build with `build\build-installer.ps1 -SkipSigning` and verify the installer is written under `artifacts\installer\unsigned` with `-unsigned` in its filename. Confirm the command neither creates nor modifies `publish\installer`; never attach this local test output to a GitHub Release. Inspect the staged first-party DLLs and confirm the publish script accepted their ReadyToRun managed native headers.
2. On a `main` push or manual `build` workflow run, verify the workflow uploads `PredatorLite-win-x64-portable-UNSIGNED-TEST-ONLY` and `PredatorLite-installer-UNSIGNED-TEST-ONLY` Actions artifacts with 14-day retention. Both must contain `UNSIGNED-TEST-ONLY.txt`; the installer artifact must contain only that notice, the `-unsigned.exe`, and its `.sha256`. Pull requests must not upload either artifact, and the workflow must not create or modify a GitHub Release.
3. Run `build\test-installer-signing.ps1` locally before a release. To inspect GitHub-hosted behavior, manually dispatch the separate `installer signing gates` workflow; it must upload no artifact and must not create or modify a GitHub Release. The test must reject Debug signing and a locally trusted private CA, exercise signed-build failure cleanup, sign all eight PredatorLite-owned EXE/DLL files plus Setup and the generated uninstaller with SHA-256 and RFC 3161 timestamps, install and uninstall successfully, remove its exact temporary certificates, and leave no `-test-signed` artifact or registry state. Existing `publish` content must remain unchanged.
4. Build the production installer with a trusted Authenticode certificate whose chain terminates in `LocalMachine\AuthRoot`. Verify `publish\win-x64` remains unchanged and only the final Setup and `.sha256` are promoted to `publish\installer`. A failure before promotion must preserve the previous installer; a failure after promotion starts must leave no candidate or incomplete target. Verify Setup and every PredatorLite-owned EXE/DLL with SignTool, then install in a disposable profile and verify the generated uninstaller.
5. From a clean ordinary-user profile on native x64 Windows with the documented runtimes installed, install without elevation. Verify the default location is `%LocalAppData%\Programs\PredatorLite`, the Start menu entry works, and the optional desktop shortcut follows the selected task. Confirm Setup rejects Windows on ARM and x86 systems.
6. Launch the installed app and exercise read-only navigation, tray, OSD, dedicated key, and diagnostics. The main app and installer must not request elevation; only an explicit conflict-management action may launch the elevated helper.
7. Install a newer build with the same Inno Setup `AppId`. Verify it upgrades in place without duplicating the installed-app entry or deleting user settings. If that release removes or renames a payload file, add an exact `[InstallDelete]` entry and verify the obsolete file is removed during this upgrade test.
8. Enable Start with Windows and confirm the `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\PredatorLite` value exists. Exit PredatorLite from the tray, uninstall it, and verify that value, the install directory, shortcuts, and uninstall registration are removed without requiring a restart. User settings and logs should remain available unless a future UI offers an explicit data-removal choice.

## Exit and logs

1. Exit from the tray menu. The main process, OSD, FanGuard when not needed, and tray icon must all disappear.
2. Review the newest log for unhandled UI exceptions, shutdown errors, failed native window hooks, or repeated hardware writes.
