# PredatorLite Manual Test Checklist

Run this checklist on Windows 11 x64. Hardware-write cases require an Acer Predator PHN16-71 with BIOS V1.20; on every other model or BIOS, verify that controls remain read-only instead.

## Preparation

1. Install .NET 10 Runtime x64 and Windows App Runtime 1.8 x64. Run `/winui-setup` if `winapp` is not available.
2. Keep `AcerServiceSvc`, `AcerLightingService`, `ASMSvc`, and `AcerApplicationBaseDriver_Device` installed and running.
3. Close every existing PredatorLite instance, including older WPF builds, before launching the WinUI build.
4. Build and launch the app as an ordinary user with `dotnet run --project src\PredatorLite.App\PredatorLite.App.csproj`. For a validated published directory, launch `PredatorLite.exe` from that complete directory. Do not elevate the main app.
5. For the repeatable, non-writing navigation and accessibility pass, run `build\ui-tests.ps1 -AppPid <PID>`.

## Window and navigation

1. Verify the initial window is approximately 600x840 DIPs and opens at the lower-right of the display containing the mouse pointer, with about 12 DIPs between the window and the work-area right/bottom edges.
2. At 100%, 125%, and 150% display scaling, verify the window stays fully inside `DisplayArea.WorkArea`. Repeat with the taskbar on the bottom and one side, then with the pointer on each display of a mixed-DPI dual-monitor setup.
3. Verify the window cannot be resized below 560x640 DIPs or above 640x900 DIPs. Resize between those bounds; the bottom navigation must remain fixed while page content scrolls independently.
4. Confirm the bottom bar exposes five equal-width navigation items: Home, Cooling, Lighting, Monitor, and Settings. Home must not contain a status icon or status message.
5. Open each secondary page from the bottom bar. The selected state must follow the active page, and `Alt+Left` must return directly to Home.
6. Verify all pages at 100%, 125%, and 150% display scaling and at the minimum window size. No text, toggle, action button, or setting row may overlap or clip.
7. Switch Windows between light, dark, and a contrast theme. Desktop Acrylic, glass cards, text, semantic badges, focus indicators, and controls must remain readable. With transparency disabled, the fallback surface must remain opaque and legible.
8. Switch between Chinese and English. The shell, bottom navigation, tray menu, dialogs, OSD, mode names, and validation messages must update.
9. Trigger a read-only state, reboot-required state, and a recoverable error where practical. Only one notice may be visible, with priority Error, Reboot required, then Read-only; full error text must wrap instead of being truncated.
10. Minimize or close the main window. It must hide to the tray rather than terminate.
11. Resize the window, hide it, move the pointer to another display, then left-click the tray icon or use its Open command. The same window must retain its logical size, move to that display's lower-right, and receive focus.
12. Start PredatorLite again through `winapp run`. No second main process should remain, and the existing window must move to the pointer display's lower-right and open.

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
3. Enable the OSD and FPS separately. The overlay must stay topmost, ignore mouse input, avoid taskbar/Alt+Tab, and close during app exit. When FPS is off, its entire metric column must collapse and the remaining three columns must fill the width.
4. Switch language while the OSD is visible. Every OSD label must update. Simulate stale telemetry and verify the OSD stale indicator appears and clears on recovery.
5. Verify `Ctrl+Alt+F11` opens the window at the pointer display's lower-right and `Ctrl+Alt+F12` cycles operating modes when global shortcuts are enabled.
6. On a PHN16-71, hide PredatorLite and press the PredatorSense key. It must open and focus PredatorLite without also opening PredatorSense. Press it while PredatorLite is visible in the background to reposition and focus the window, then press it while PredatorLite is in the foreground to hide it.
7. Hold the PredatorSense key and verify only one visibility change occurs on release. Disable global shortcuts and repeat; the dedicated key must remain active while `Ctrl+Alt+F11` and `Ctrl+Alt+F12` are disabled.
8. Exit PredatorLite from the tray and press the PredatorSense key. PredatorLite must not cold-start. If Acer software still launches PredatorSense through an independent channel while PredatorLite is running, use the explicit Disable conflicts action and repeat the test.
9. Press the separate physical Mode key and verify it still cycles exactly one operating mode per press.
10. Export diagnostics and confirm a ZIP is created at the selected path. Open Logs must open `%LocalAppData%\PredatorLite\logs`.

## Hardware controls

1. Change one operating mode at a time and verify the visible state matches the next telemetry read.
2. Test Auto fan first. Before testing Max or Custom, confirm `PredatorLite.FanGuard.exe` starts and the UI shows FanGuard active.
3. While Max or Custom is active, terminate only `PredatorLite.exe` from Task Manager. Within five seconds FanGuard must restore Auto fan and then exit.
4. Apply a valid custom curve. Out-of-range, decreasing, non-increasing, or non-100%-at-95C curves must be rejected without changing fan ownership.
5. Test the charge limit, refresh rate, overdrive, and lighting controls, then verify their reported state.
6. Test Hybrid and Discrete GPU routing only when a reboot is acceptable. Cancel must preserve the previous selection; confirm must show the reboot-required banner.

## Installer

1. Build the unsigned installer with `build\build-installer.ps1 -SkipSigning` and verify it is written under `artifacts\installer\unsigned` with `-unsigned` in its filename. Confirm no unsigned artifact is written under `publish`; never publish the local test directory.
2. Build the production installer with a trusted Authenticode certificate. Use SignTool to verify the setup executable and every PredatorLite-owned EXE/DLL; after installation, also verify the generated uninstaller. All signatures must use SHA-256 and carry an RFC 3161 timestamp.
3. From a clean ordinary-user profile with the documented runtimes installed, install without elevation. Verify the default location is `%LocalAppData%\Programs\PredatorLite`, the Start menu entry works, and the optional desktop shortcut follows the selected task.
4. Launch the installed app and exercise read-only navigation, tray, OSD, dedicated key, and diagnostics. The main app and installer must not request elevation; only an explicit conflict-management action may launch the elevated helper.
5. Install a newer build with the same Inno Setup `AppId`. Verify it upgrades in place without duplicating the installed-app entry or deleting user settings. If that release removes or renames a payload file, add an exact `[InstallDelete]` entry and verify the obsolete file is removed during this upgrade test.
6. Enable Start with Windows and confirm the `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\PredatorLite` value exists. Exit PredatorLite from the tray, uninstall it, and verify that value, the install directory, shortcuts, and uninstall registration are removed without requiring a restart. User settings and logs should remain available unless a future UI offers an explicit data-removal choice.

## Exit and logs

1. Exit from the tray menu. The main process, OSD, FanGuard when not needed, and tray icon must all disappear.
2. Review the newest log for unhandled UI exceptions, shutdown errors, failed native window hooks, or repeated hardware writes.
