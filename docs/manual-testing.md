# PredatorLite Manual Test Checklist

Run this checklist on Windows 11 x64. Hardware-write cases require an Acer Predator PHN16-71 with BIOS V1.20; on every other model or BIOS, verify that controls remain read-only instead.

## Preparation

1. Install .NET 10 Runtime x64 and Windows App Runtime 2.3 x64.
2. Keep `AcerServiceSvc`, `AcerLightingService`, and `AcerApplicationBaseDriver_Device` installed and running.
3. Close every existing PredatorLite instance, including older WPF builds, before launching the WinUI build.
4. Run `build\publish.ps1` and launch `publish\win-x64\PredatorLite.exe` as an ordinary user.

## Window and navigation

1. Verify Home, Lighting, Monitor, and Settings open without layout overlap at 100%, 125%, and 150% display scaling.
2. Switch Windows between light, dark, and high-contrast modes; text and controls must remain readable.
3. Switch between Chinese and English. The shell, tray menu, dialogs, OSD, mode names, and device-setting labels must update.
4. Minimize or close the main window. It must hide to the tray rather than terminate.
5. Left-click the tray icon and use its Open command. The same window must return and receive focus.
6. Launch `PredatorLite.exe` again. No second main process should remain, and the existing window must open.

## Settings and integration

1. Toggle start minimized and global shortcuts, restart, and verify the settings persist.
2. Enable startup, inspect `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\PredatorLite`, then disable it again.
3. Enable the OSD and FPS separately. The overlay must stay topmost, ignore mouse input, avoid taskbar/Alt+Tab, and close during app exit.
4. Verify `Ctrl+Alt+F11` opens the window and `Ctrl+Alt+F12` cycles operating modes when global shortcuts are enabled.
5. Export diagnostics and confirm a ZIP is created at the selected path. Open Logs must open `%LocalAppData%\PredatorLite\logs`.

## Hardware controls

1. Change one operating mode at a time and verify the visible state matches the next telemetry read.
2. Test Auto fan first. Before testing Max or Custom, confirm `PredatorLite.FanGuard.exe` starts and the UI shows FanGuard active.
3. While Max or Custom is active, terminate only `PredatorLite.exe` from Task Manager. Within five seconds FanGuard must restore Auto fan and then exit.
4. Apply a valid custom curve. Invalid, decreasing, or sub-100%-at-95C curves must be rejected without changing fan ownership.
5. Toggle each supported device switch once. On a rejected write, its visible state must return to the previous value without issuing a reverse write.
6. Test the charge limit, refresh rate, overdrive, and lighting controls, then verify their reported state.
7. Test Hybrid and Discrete GPU routing only when a reboot is acceptable. Cancel must preserve the previous selection; confirm must show the reboot-required banner.

## Exit and logs

1. Exit from the tray menu. The main process, OSD, FanGuard when not needed, and tray icon must all disappear.
2. Review the newest log for unhandled UI exceptions, shutdown errors, failed native window hooks, or repeated hardware writes.
