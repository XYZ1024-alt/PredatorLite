# PROJECT KNOWLEDGE BASE

**Generated:** 2026-07-24

## OVERVIEW

Project: **PredatorLite**

PredatorLite is a v1.0.1 ordinary-user Windows control utility and independent, unofficial alternative to PredatorSense for Acer Predator devices. Hardware writes are authorized only by explicit model/BIOS profiles. The current writable profile is `Predator PHN16-71`, BIOS `V1.20`, on Windows 11 24H2 (build 26100+) x64. Other models and BIOS versions may expose diagnostics and read-only telemetry, but must remain unable to write hardware state.

Stack: C# on .NET 10 SDK `10.0.302`; WinUI 3 with Microsoft Windows App SDK `2.3.1`; CommunityToolkit.Mvvm; xUnit; BenchmarkDotNet; PowerShell release tooling; Inno Setup 6.

The main app is unpackaged, x64-only, framework-dependent, and `asInvoker`. `PredatorLite.Core` targets plain `net10.0`; Windows projects target `net10.0-windows10.0.26100.0`.

## STRUCTURE

`PredatorLite.slnx` contains five production projects, one test project, and one benchmark project.

- `src/PredatorLite.App`: WinUI 3 UI, tray/OSD, single-instance activation, localization, app services, and the shared `MainViewModel` coordinator.
- `src/PredatorLite.Core`: platform-neutral models, abstractions, settings persistence, startup policy, logging, and fan-curve safety logic. It must not depend on WinUI or Acer implementations.
- `src/PredatorLite.Platform.Windows`: `IPredatorPlatform` implementation, AcerService/WMI/system-monitor protocols, Windows integration, and read-only telemetry sources.
- `src/PredatorLite.FanGuard`: current-user watchdog that restores automatic fan control when the app loses ownership or stops heartbeating.
- `src/PredatorLite.ElevatedHelper`: narrowly scoped elevated service/task manager; it has no hardware-protocol dependency.
- `tests/PredatorLite.Tests`: xUnit coverage for Core, Platform, FanGuard, and ElevatedHelper. It deliberately does not reference the WinUI App project.
- `benchmarks/PredatorLite.Benchmarks`: packet codec, fan-curve, and telemetry-merging microbenchmarks.
- `build`: publish, installer, signing-gate, UI automation, startup measurement, and AOT audit scripts.
- `docs`: architecture, hardware safety, protocol provenance, service dependencies, performance evidence, and manual QA.
- `licenses`: third-party license texts included in release output.

Dependency direction is `App -> Core + Platform`, `Platform -> Core`, `FanGuard -> Core + Platform`, and `ElevatedHelper -> Core`. The App references companion executables without linking their assemblies and copies their outputs beside the app.

## COMMANDS

Run commands from the repository root in PowerShell. Development requires Windows 11 24H2 x64 and the SDK selected by `global.json`.

| Action | Command |
| --- | --- |
| Restore dependencies | `dotnet restore PredatorLite.slnx` |
| Build | `dotnet build PredatorLite.slnx -c Release --no-restore` |
| Test | `dotnet test PredatorLite.slnx -c Release --no-build` |
| Verify formatting | `$env:Configuration = "Release"; dotnet format PredatorLite.slnx --verify-no-changes --no-restore` |
| Audit dependencies | `dotnet package list --project PredatorLite.slnx --vulnerable --include-transitive --no-restore` |
| Benchmark smoke | `dotnet run --project benchmarks\PredatorLite.Benchmarks\PredatorLite.Benchmarks.csproj -c Release --no-build -- --job Dry --filter "*"` |
| Run the UI | `dotnet run --project src\PredatorLite.App\PredatorLite.App.csproj` |
| Run UI automation | `.\build\ui-tests.ps1 -AppPid <PID>` |
| Publish ReadyToRun | `.\build\publish.ps1` |
| Publish IL comparison | `.\build\publish.ps1 -OutputPath publish\win-x64-il -ReadyToRun:$false` |
| Build formal release package | `.\build\prepare-release.ps1 -Version 1.0.1` |
| Build installer test package | `.\build\build-installer.ps1 -SkipSigning` |
| Test signing integration | `.\build\test-installer-signing.ps1` |
| Audit Native AOT | `.\build\aot-audit.ps1` |

CI runs restore, dependency audit, Release build, format verification, tests, BenchmarkDotNet `Dry`, and both IL and ReadyToRun publish validation. Run the focused checks appropriate to the change; use the full CI sequence before submission.

## ARCHITECTURE

- `src/PredatorLite.App/Program.cs` owns the STA entry point, COM setup, single-instance redirection, and WinUI dispatcher initialization.
- `src/PredatorLite.App/App.xaml.cs` is the manual composition root and ordered lifetime/shutdown owner. There is no DI container.
- `MainViewModel` is a CommunityToolkit `ObservableObject` shared by lazily created pages. It coordinates critical/deferred startup, settings, commands, telemetry polling, automation, and disposal.
- UI commands flow through `MainViewModel`, then `IPredatorPlatform`, then `PredatorPlatform`; hardware protocol details must stay outside Core and XAML/code-behind.
- Critical startup loads settings and probes identity, power, backend, and operating mode. Deferred startup performs the full capability probe, telemetry, optional listeners, service inventory, and polling. Hidden startup must keep pages lazy.
- User-initiated hardware workflows are serialized by `MainViewModel._hardwareGate`. `PredatorPlatform._operationGate` additionally serializes operating-mode, fan, and GPU-MUX Acer operations; not every Platform setter uses that gate. Preserve these boundaries and assess concurrency explicitly when adding or changing setters.
- Settings and diagnostics use source-generated `System.Text.Json` metadata. When serialized models change, update the applicable JSON context in Core, App diagnostics, Acer integration, Quick Access integration, or ElevatedHelper.

## CODING STANDARDS

- Root `.editorconfig` is authoritative: UTF-8, LF endings, final newline, spaces, four-space C# indentation, and two-space XAML/XML/project/YAML indentation.
- Use file-scoped namespaces, nullable reference types, implicit usings, and explicit local types in the established style. Use `PascalCase` for types/public members, `camelCase` for parameters/locals/private fields, and `I` prefixes for interfaces.
- Latest recommended .NET analyzers and build-enforced code style are enabled. Warnings, NuGet audit findings `NU1901`-`NU1904`, and analyzer violations fail the build.
- Follow existing async patterns: `Async` suffixes, cancellation propagation, deterministic disposal, and `ConfigureAwait(false)` in non-UI platform/library code.
- Use CommunityToolkit `[ObservableProperty]` and `[RelayCommand]` patterns in view models. Keep page code-behind limited to UI event handling, lazy navigation, or helpers required by compiled bindings.
- XAML favors compiled `{x:Bind ..., Mode=OneWay}`, `{StaticResource ...}` for localized strings/styles, `{ThemeResource ...}` for theme-aware brushes, and explicit automation names/IDs for interactive elements.
- Keep each XAML view paired with its `.xaml.cs`. Add reusable UI strings with identical keys to both `Resources/Strings.enUS.xaml` and `Resources/Strings.zhCN.xaml`.
- Prefer existing abstractions and protocol codecs over ad hoc parsing. Do not add dependencies unless they materially improve correctness or match an established boundary.

## HARDWARE SAFETY

Read `docs/hardware-safety.md` and `docs/protocol-provenance.md` before changing any hardware-facing path.

- Never authorize a new profile based only on a matching marketing name. Each model/BIOS profile needs independent protocol evidence, explicit mapping/capability gates, read-back where supported, failure tests, recovery coverage, and hardware/BIOS manual validation.
- Every hardware action must pass the identity/backend capability gate, remain serialized, issue only bounded commands, verify resulting state when possible, and preserve the previous visible/persisted state on failure.
- Startup may restore only the saved operating mode, or Eco through the explicit battery automation. It must never replay fan, lighting, GPU routing, charge-limit, or device settings. A freshly observed matching mode must not send an Acer write.
- Max and Custom fan modes require a successful current-user FanGuard pipe handshake and an active platform lease before the write. Preserve the five-second recovery timeout and Platform's ownership rule: shutdown restores Auto only when this process successfully established Max/Custom.
- Fan curves must have increasing temperatures, non-decreasing speeds, bounded speed, and a final `95 C / 100%` point for both channels. Missing temperatures in Custom mode are treated as `95 C` and force 100%.
- The main app must remain ordinary-user. ElevatedHelper accepts only `disable|restore`; the fixed services `AcerCCAgentSvis`, `AcerDIAgentSvis`, `AcerDeviceEnablingServiceV2`, and `PredatorService`; and the fixed `\PredatorSenseLauncher` task. The App launcher passes `%ProgramData%\PredatorLite\service-backup.json`; helper validation permits only a file named `service-backup.json` beneath `%ProgramData%\PredatorLite`. Never broaden this path or turn the helper into a general privileged broker.
- GPU routing remains AcerService-only and reboot-confirmed. Do not add iGPU-only, adapter disabling, overclocking, voltage/power-limit, arbitrary EC/MSR/NVAPI, BIOS, vBIOS, firmware, or ROM write paths.
- Do not copy Acer/PredatorSense code, decompiled output, binaries, drivers, firmware, ROMs, artwork, or unredacted diagnostics. `PreySense/` is not part of this repository and is not currently protected by `.gitignore`; if local research material appears there, never stage or derive contributions from it.

## TESTING

- Tests use xUnit. Test files follow `<Subject>Tests.cs`; method names describe behavior, such as `MissingSafetyEndpointIsRejected`.
- Add focused tests for every changed safety invariant, protocol encoding/parser path, capability gate, persistence rule, startup policy, and companion allowlist/recovery behavior.
- There is no numeric coverage threshold, but CI must pass. The test project does not cover App/ViewModel/XAML behavior directly, so UI changes also need successful XAML compilation, `build\ui-tests.ps1`, applicable manual checks, and screenshots.
- Run the app and UI checks as an ordinary user. Hardware-write tests are permitted only on a matching writable profile; other systems must be tested for read-only behavior.
- Follow `docs/manual-testing.md` for visible WinUI, tray/OSD, accessibility, localization, single-instance, installer, and hardware checks.
- Follow `docs/performance.md` for benchmark/startup evidence. Tray startup measurement is non-writing; `Critical` and `Deferred` require `-AllowHardwareInitialization`, a closed app, and the current writable profile machine.

## RELEASE NOTES

- `build/publish.ps1` produces a validated framework-dependent balanced ReadyToRun layout in `publish\win-x64`: startup-critical assemblies use R2R while deferred telemetry and unused projection assemblies remain IL. Retain the entire directory; target machines need .NET 10 Runtime x64 and Windows App Runtime 2.3 x64.
- ReadyToRun is the production mode. Native AOT is currently blocked by documented trim/AOT and unpackaged WinUI resource issues; do not suppress diagnostics or promote it without the full regression matrix.
- `build/prepare-release.ps1` produces the four v1.0.1 formal assets in `publish\release`: a ReadyToRun portable ZIP, the installer, and one SHA-256 sidecar for each. The public `main` CD workflow creates the matching GitHub Release only when its `vX.Y.Z` release does not already exist.
- `build/build-installer.ps1 -SkipSigning` remains a test-only path under `artifacts\installer\unsigned`; certificate signing and the existing signing gates remain available for future releases.
- Never commit generated `bin/`, `obj/`, `publish/`, `artifacts/`, `BenchmarkDotNet.Artifacts/`, `TestResults/`, `coverage/`, UI captures, logs, dumps, traces, archives/packages, machine settings, environment files, diagnostics, or signing key/certificate material.

## WHERE TO LOOK

- Runtime composition/lifetime: `src/PredatorLite.App/Program.cs`, `src/PredatorLite.App/App.xaml.cs`
- User workflows/startup/automation: `src/PredatorLite.App/ViewModels/MainViewModel.cs`
- Platform contract: `src/PredatorLite.Core/Abstractions/IPredatorPlatform.cs`
- Hardware profile and safety boundaries: `src/PredatorLite.Platform.Windows/HardwareTargetProfileCatalog.cs`, `src/PredatorLite.Core/Models/HardwareTargetProfile.cs`, `src/PredatorLite.Platform.Windows/PredatorPlatform.cs`
- Acer protocol code: `src/PredatorLite.Platform.Windows/Acer/`, `docs/protocol-provenance.md`
- Fan safety: `src/PredatorLite.Core/Services/FanCurveEngine.cs`, `src/PredatorLite.FanGuard/Program.cs`, `docs/hardware-safety.md`
- Privilege boundary: `src/PredatorLite.ElevatedHelper/Program.cs`, `tests/PredatorLite.Tests/CompanionSafetyBoundaryTests.cs`
- UI conventions/resources: `src/PredatorLite.App/Views/`, `src/PredatorLite.App/Resources/`, `docs/manual-testing.md`
- Build/release truth: `Directory.Build.props`, `Directory.Packages.props`, `.github/workflows/build.yml`, `build/`, `docs/performance.md`

## COMMITS AND PULL REQUESTS

Use focused, imperative Conventional Commit-style subjects such as `feat(platform): ...`, `fix(app): ...`, `test: ...`, or `refactor(app): ...`. Pull requests should explain user-visible behavior and safety impact, link relevant issues, list automated and manual verification, include screenshots for visible WinUI changes, cite protocol provenance, and identify the supported hardware/BIOS used for any hardware-write testing.
