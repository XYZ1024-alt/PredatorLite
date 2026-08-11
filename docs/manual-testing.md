# PredatorLite Manual Test Checklist

Run this checklist on Windows 11 24H2 (build 26100+) x64. Hardware-write cases require a matching writable profile; the current writable profile is Acer Predator PHN16-71 with BIOS V1.20. On every other model or BIOS, verify that controls remain read-only instead.

## Preparation

1. Install .NET 10 Runtime x64 and Windows App Runtime 2.3 x64. Run `/winui-setup` if `winapp` is not available.
2. Keep `AcerServiceSvc`, `AcerLightingService`, `ASMSvc`, and `AcerApplicationBaseDriver_Device` installed and running.
3. Close every existing PredatorLite instance, including older WPF builds, before launching the WinUI build.
4. Build and launch the app as an ordinary user with `dotnet run --project src\PredatorLite.App\PredatorLite.App.csproj`. For a published directory, launch `PredatorLite.exe` from that complete directory. Do not elevate the main app.
5. For the repeatable, non-writing navigation and accessibility pass, run `build\ui-tests.ps1 -AppPid <PID>`. Pass `-DistributionChannel Store` when exercising an installed Microsoft Store or Store-sideload package.

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
2. On a GitHub build, enable startup, inspect `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\PredatorLite`, then disable it again. On a Store build, enable the packaged `PredatorLiteStartup` task, confirm Windows lists PredatorLite under Startup apps, then disable it through Windows and verify PredatorLite does not silently re-enable it.
3. Enable the OSD. The overlay must stay topmost, ignore mouse input, avoid taskbar/Alt+Tab, and close during app exit. Its CPU, GPU, and CPU fan columns must remain equal-width in Chinese and English.
4. Switch language while the OSD is visible. Every OSD label must update. Simulate stale telemetry and verify the OSD stale indicator appears and clears on recovery.
5. Verify `Ctrl+Alt+F11` opens the window at the pointer display's lower-right and `Ctrl+Alt+F12` cycles operating modes when global shortcuts are enabled.
6. On a PHN16-71, hide PredatorLite and press the PredatorSense key. It must open and focus PredatorLite without also opening PredatorSense. Press it while PredatorLite is visible in the background to reposition and focus the window, then press it while PredatorLite is in the foreground to hide it.
7. Hold the PredatorSense key and verify only one visibility change occurs on release. Disable global shortcuts and repeat; the dedicated key must remain active while `Ctrl+Alt+F11` and `Ctrl+Alt+F12` are disabled.
8. Exit PredatorLite from the tray and press the PredatorSense key. PredatorLite must not cold-start. If Acer software still launches PredatorSense through an independent channel while PredatorLite is running, use the explicit Disable conflicts action and repeat the test.
9. Press the separate physical Mode key and verify it still cycles exactly one operating mode per press.
10. Export diagnostics and confirm a ZIP is created at the selected path. Open Logs must open `%LocalAppData%\PredatorLite\logs`.
11. On a GitHub build with the current version equal to or newer than the latest Stable GitHub release, select Check for updates. Verify the card reports that PredatorLite is up to date, creates no installer, and shows no UAC prompt.
12. On a GitHub build, run an older Stable version while a newer Stable release exists. Check for updates and verify the dialog names both versions, defaults focus to Cancel, and starts no download when cancelled. A newer Beta or RC alone must not trigger this dialog.
13. On a GitHub build, accept the Stable update. Verify progress is shown, Setup appears under `%LocalAppData%\PredatorLite\Updates` only after its release sidecar and GitHub asset digest agree with the computed SHA-256, and a failed or interrupted download leaves no `.download` file or launched process.
14. On a GitHub build, approve the installer elevation prompt and complete the in-place upgrade. Setup must close PredatorLite, preserve settings, avoid a duplicate installed-app entry, and the next launch must show the new version. Cancelled elevation or an offline/API failure must leave the current app usable and report the failure in the update card and log.

## Startup and resume mode restoration

1. On the current writable PHN16-71 / BIOS V1.20 profile, select Silent, Balanced, Performance and Turbo in turn. After each selection, exit PredatorLite, change the hardware mode and launch PredatorLite manually; the saved mode must be restored and verified.
2. Repeat with Start with Windows enabled and `StartMinimized` on. The tray must appear before full telemetry and the saved mode must be restored without opening the shell.
3. Launch while the hardware already uses the saved mode. The startup log must report `already-active`; no Acer operating-mode set packet may be sent, while the matching Windows power overlay is still selected.
4. With battery Eco automation enabled, launch on battery and verify Eco, then reconnect AC and verify the saved non-Eco mode returns once. With automation disabled, a battery launch must restore the saved non-Eco mode.
5. Delay or stop AcerService on the current profile machine. Verify read-only startup probes stop at the ten-second deadline, never bypass the profile catalog, and do not loop hardware writes. If the deferred probe first discovers the backend, it may perform only one pending restore.
6. On an unknown model or BIOS, verify startup remains read-only and sends no hardware setter. A denied Acer WMI mode read must not make `CanWriteHardware` true when AcerService is unavailable. Confirm diagnostics record no writable target profile.
7. During hidden startup, verify no `MainShell` or page is created until the tray, dedicated key, or existing-instance activation shows the window. Navigate through every page after opening and confirm each page initializes once and remains functional.
8. Review EventSource provider `PredatorLite-Startup` and the startup timing lines from at least five healthy cold launches. `critical-ready` must precede `deferred-ready`; APGe, service inventory, and non-current page construction must remain outside the critical path.
9. Run both safe non-writing startup comparisons in [`performance.md`](performance.md). Confirm `Tray` emits `tray-ready`, `Shell` emits `shell-ready`, and both `--startup-tray-only` paths stop without `critical-ready`, a hardware setter, or a FanGuard launch.
10. Select Performance, record the app PID, then sleep and wake Windows without exiting PredatorLite. Within 15 seconds, Performance must be selected with the same PID. The log must contain one queued resume line and exactly one `applied` or `already-active` outcome for that wake.
11. Repeat the resume check with Turbo and Silent, then with Performance while the main window is hidden to the tray. The hidden HWND must still queue and complete exactly one restore.
12. Enable battery Eco automation, select Performance on AC, sleep, disconnect AC while asleep, and wake. Eco must become active while `LastAcMode` remains Performance; reconnecting AC must restore Performance.
13. Sleep and wake while hardware retains the selected mode. The outcome must be `already-active`, with no `applied` outcome or operating-mode setter packet, and the Windows power overlay must remain synchronized.
14. Stop AcerService only for a controlled failure case. With WMI available, resume restoration must use its verified fallback. Without WMI, the log must show the ten-second read deadline and one failed outcome without changing `LastAcMode`. Restart AcerService immediately, then repeat on an unknown model or BIOS to confirm the profile gate remains read-only.

## Hardware controls

1. Change one operating mode at a time and verify the visible state matches the next telemetry read.
2. Test Auto fan first. Before testing Max or Custom, confirm `PredatorLite.FanGuard.exe` starts and the UI shows FanGuard active.
3. While Max or Custom is active, terminate only `PredatorLite.exe` from Task Manager. Within five seconds FanGuard must restore Auto fan and then exit.
4. Apply a valid custom curve. Out-of-range, decreasing, non-increasing, or non-100%-at-95C curves must be rejected without changing fan ownership.
5. Test the charge limit, refresh rate, overdrive, and lighting controls, then verify their reported state.
6. Test Hybrid and Discrete GPU routing only when a reboot is acceptable. Cancel must preserve the previous selection; confirm must show the reboot-required banner.

## Installer

1. Run `build\test-release-version.ps1`, then build Stable, RC, and Beta assets with `build\prepare-release.ps1 -Version 1.0.2 -Channel Stable`, `-Channel RC -Iteration 1`, and `-Channel Beta -Iteration 1`. For each invocation, verify `publish\release` contains exactly the portable ZIP, Setup, and their SHA-256 sidecars named with `1.0.2`, `1.0.2-rc.1`, or `1.0.2-beta.1`; confirm the sidecar hashes match, no test-only naming or notice file is present, and the eight first-party PE files plus Setup have an empty certificate table. Verify the first-party DLL product version matches the displayed release version and the numeric file-version ordering is Beta `<` RC `<` Stable.
2. Build the installer test package with `build\build-installer.ps1 -SkipSigning` and verify the installer is written under `artifacts\installer\unsigned` with `-unsigned` in its filename and a `1.0.2` base version segment. Confirm the command neither creates nor modifies `publish\installer`; never attach this local test output to a GitHub Release. Inspect the staged first-party DLLs and confirm the publish script accepted their ReadyToRun managed native headers.
3. On a `main` push or manual `build` workflow run, verify the workflow builds and validates an `rc.1` installer but uploads no Actions artifact and creates no GitHub Release. Pull requests must skip the Inno Setup installer build.
4. Manually dispatch the `release` workflow from `main`. Beta must require a positive iteration, reject `confirm_public=true`, and create a Draft Pre-release visible only to users with push access. RC must require a positive iteration plus `confirm_public=true` and create a public Pre-release. Stable must reject an iteration, require `confirm_public=true`, and create a normal public Release. Every channel must reject an existing complete version instead of overwriting it. Do not grant public-repository push access solely for Beta downloads; use a private repository or private storage for broader internal QA.
5. Run `build\test-installer-signing.ps1` locally before a release. To inspect GitHub-hosted behavior, manually dispatch the separate `installer signing gates` workflow; it must upload no artifact and must not create or modify a GitHub Release. The test must reject Debug signing and a locally trusted private CA, exercise signed-build failure cleanup, sign all eight PredatorLite-owned EXE/DLL files plus Setup and the generated uninstaller with SHA-256 and RFC 3161 timestamps, install and uninstall successfully, remove its exact temporary certificates, and leave no `-test-signed` artifact or registry state. Existing `publish` content must remain unchanged.
6. For the optional certificate-signed path, build the installer with a trusted Authenticode certificate whose chain terminates in `LocalMachine\AuthRoot`. Verify `publish\win-x64` remains unchanged and only the final Setup and `.sha256` are promoted to `publish\installer`. A failure before promotion must preserve the previous installer; a failure after promotion starts must leave no candidate or incomplete target. Verify Setup and every PredatorLite-owned EXE/DLL with SignTool, then install in a disposable profile and verify the generated uninstaller.
7. From a clean ordinary-user profile on native x64 Windows with the documented runtimes installed, install without elevation. Verify the default location is `%LocalAppData%\Programs\PredatorLite`, the Start menu entry works, and the optional desktop shortcut follows the selected task. Confirm Setup rejects Windows on ARM and x86 systems.
8. Launch the installed app and exercise read-only navigation, tray, OSD, dedicated key, and diagnostics. The main app runs as an ordinary user; the installer and Setup require an administrator to install into a protected `%ProgramFiles%\PredatorLite` directory so the elevated helper cannot be tampered by a same-user non-administrator. Only an explicit conflict-management action may launch the elevated helper.
9. Verify the elevated helper trust check rejects launching from a user-writable copy. Copy the portable build to `%LocalAppData%\Programs\PredatorLite`, start it, and attempt to disable conflicting services; PredatorLite must refuse and report that the helper is not in a protected installation directory.
10. Install Beta, RC, and Stable builds with the same Inno Setup `AppId` in numeric file-version order. Verify each upgrades in place without duplicating the installed-app entry or deleting user settings. If that release removes or renames a payload file, add an exact `[InstallDelete]` entry and verify the obsolete file is removed during this upgrade test.
11. On a GitHub installation, enable Start with Windows and confirm the `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\PredatorLite` value exists. Exit PredatorLite from the tray, uninstall it, and verify that value, the install directory, shortcuts, and uninstall registration are removed without requiring a restart. User settings and logs should remain available unless a future UI offers an explicit data-removal choice.

## Microsoft Store package

1. Install the Visual Studio MSIX Packaging Tools/WAP targets, then run `build\build-store-package.ps1 -Configuration Release -Version 1.0.3 -Mode StoreUpload`. Confirm the command fails before promotion if the tools are absent, the version differs from `Directory.Build.props`, or any Store MSBuild property is overridden.
2. Run `build\test-store-package.ps1 -PackagePath publish\store\PredatorLite-Store-1.0.3-win-x64.msixupload -ExpectedVersion 1.0.3.0`. Confirm the upload contains exactly one x64 MSIX and no ARM/x86 bundle, symbols, source, XML documentation, or loose `.cer`/`.pfx` file.
3. Unpack the validated MSIX and confirm the Partner Center identity name, publisher, publisher display name, `1.0.3.0` version, x64 architecture, `runFullTrust`, `PredatorLiteStartup`, seven package assets, and `en-US`/`zh-CN` resources. Confirm `allowElevation` and every `windows.fullTrustProcess` helper extension are absent; `PredatorLite.exe` and `PredatorLite.FanGuard.exe` must each exist exactly once at package root, and no `PredatorLite.ElevatedHelper.*` payload may exist anywhere in the package.
4. Build Sideload mode with a disposable certificate whose subject exactly matches the manifest publisher. Confirm the final output is one WAP-generated `_Test` directory under `artifacts\store`, includes its dependency installer and install script, and never writes `publish\store`. Remove the package and certificate after testing.
5. On a clean x64 Windows 11 24H2 virtual machine without .NET Runtime or Windows App Runtime preinstalled, install through the WAP-generated `_Test` directory. Confirm the .NET-self-contained app starts and the dependency installer satisfies the Windows App Runtime/VCLibs requirements without asking the user to locate runtimes manually.
6. Run `build\ui-tests.ps1 -AppPid <PID> -DistributionChannel Store`. On Settings, confirm the description says Microsoft Store supplies updates, while Check for updates, update progress, View release notes, Disable conflicts, and Restore services are absent. The read-only service inventory and GitHub project link must remain available.
7. Enable Start with Windows. Confirm the packaged StartupTask launches hidden to the tray without `--background`, restores only the permitted saved operating mode, and creates no shell/page before the user opens it. Disable startup through Windows Settings and confirm the app reports it off and does not request enablement again without an explicit user toggle.
8. Launch the GitHub distribution and Store distribution in both orders, including concurrent cold launches. Exactly one `PredatorLite` process may remain across both channels. A blocked launch must not initialize hardware, start FanGuard, create a tray icon, or restore a saved operating mode.
9. Confirm GitHub and Store settings/logs remain independently scoped by their distribution identities. Installing, upgrading, resetting, or uninstalling one channel must not corrupt or silently migrate the other channel's state.
10. On WindowsApps installation, confirm the protected-path validator accepts the package location. Max and Custom fan modes must still require the current-user FanGuard handshake and lease, and terminating the main process must restore Auto within five seconds.
11. Confirm the Store package exposes no Disable conflicts or Restore services action, launches no UAC prompt during normal use, and contains no `PredatorLite.ElevatedHelper.*` payload. Repeat the GitHub installed-build checks separately to retain its protected-path validation, fixed helper allowlists, UAC cancellation behavior, and `%ProgramData%\PredatorLite\service-backup.json` boundary.
12. Install a lower Stable Store package, then update through a Partner Center flight to the next Stable package. Confirm Microsoft Store owns the update, no GitHub update files are created, settings remain intact, both packaged executables update together, and startup/FanGuard behavior still passes.
13. Upload the exact workflow-produced `.msixupload` to Partner Center product `9PBFRHTRXL2Q`. Confirm it is recognized as the existing `PredatorLite` product with identity `XYZ1024.PredatorLite`, publisher `CN=AD743FEC-8EA7-47AF-A7E4-DB022A7071DA`, x64 architecture, and the expected four-component version. Complete the listing and certification fields from [`store-submission.md`](store-submission.md); never submit Beta or RC.

## Exit and logs

1. Exit from the tray menu. The main process, OSD, FanGuard when not needed, and tray icon must all disappear.
2. Review the newest log for unhandled UI exceptions, shutdown errors, failed native window hooks, or repeated hardware writes.
