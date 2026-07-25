# Performance and deployment evidence

## Policy

Performance gates run on one fixed Acer Predator PHN16-71 / BIOS V1.20 profile machine. Hosted CI restores, builds, tests, publishes and executes only the BenchmarkDotNet `Dry` smoke job; hosted timing is not a release gate.

Do not change hardware-write ordering, FanGuard coverage, process priority, GC mode, or undocumented JIT settings to improve a benchmark. The ordinary startup path must still complete identity, exact profile, BIOS, power, backend, capability and read-back checks before its one allowed operating-mode restore.

## Microbenchmarks

The separate `benchmarks/PredatorLite.Benchmarks` console project measures:

- plain and encrypted Acer packet encoding plus plain decoding;
- allocation-free evaluation of an already validated, ordered fan curve;
- primary and fallback telemetry merging.

Run the CI smoke job:

```powershell
dotnet run --project benchmarks\PredatorLite.Benchmarks\PredatorLite.Benchmarks.csproj -c Release --no-build -- --job Dry --filter "*"
```

Run a local short measurement:

```powershell
dotnet run --project benchmarks\PredatorLite.Benchmarks\PredatorLite.Benchmarks.csproj -c Release -- --job Short --filter "*"
```

`BenchmarkDotNet.Artifacts` is generated output and must not be committed.

## Startup telemetry

The app emits EventSource provider `PredatorLite-Startup`, event ID 1, for these milestones:

- `process-start`
- `primary-instance`
- `xaml-start`
- `localization-ready`
- `window-created`
- `tray-ready` after tray integration is ready (before any shell on hidden startup, after `shell-ready` on visible startup)
- `shell-ready` when the first visible shell rendering tick begins
- `critical-ready` or `critical-failed`
- `deferred-ready` or `deferred-failed`
- `redirect-activated` after a redirected launch reaches the existing window

`build/measure-startup.ps1` also supplies a private named pipe and uses the same QPC timestamps. Its `Tray` scope passes `--startup-tray-only`: the hidden window and tray are created, then startup stops before settings-driven hardware initialization. Its `Shell` scope uses the same non-writing switch but shows the Home shell and measures through entry into its first rendering tick. Both safe scopes may run beside a developer instance using an isolated measurement instance key.

`Critical` and `Deferred` scopes execute normal initialization and can restore the profile-authorized operating mode. They require both `-AllowHardwareInitialization` and a closed existing instance, and may be run only on the fixed PHN16-71 / BIOS V1.20 profile machine.

Example:

```powershell
.\build\measure-startup.ps1 `
  -Executable .\publish\win-x64\PredatorLite.exe `
  -Scope Tray `
  -WarmupIterations 2 `
  -Iterations 15 `
  -OutputPath .\artifacts\performance\startup-r2r-tray.json
```

Compare two measurements from the same machine and milestone:

```powershell
.\build\compare-startup.ps1 `
  -Baseline .\artifacts\performance\baseline.json `
  -Candidate .\artifacts\performance\candidate.json
```

A historical regression fails only when either condition is true:

- p50 is worse by more than both 10% and 25 ms;
- p95 is worse by more than both 15% and 40 ms.

Warmups are excluded. Keep the machine on the same power source and Windows power plan, close unrelated foreground work, and retain all raw samples in the ignored `artifacts/performance` directory when recording a release decision.

## ReadyToRun decision

`build/publish.ps1` publishes App, FanGuard, and ElevatedHelper separately for `win-x64`, then composes one framework-dependent directory. The default balanced ReadyToRun layout keeps the five first-party assemblies plus WinUI, MVVM, and tray startup dependencies precompiled. Deferred LibreHardwareMonitor dependencies and unused AI/ML/Widgets projections remain IL. The script verifies the expected managed-native state, rejects non-AMD64 native PE and 32-bit-required managed PE, and enforces 80 MiB R2R and 65 MiB IL budgets. Use `-ReadyToRun:$false` only to create an IL comparison layout.

The original promotion measurement on July 24, 2026 used .NET 10.0.10, Windows build 26200, an Intel Core i5-13500HX, two excluded warmups, and 15 hidden tray-only samples per layout:

| Layout | Size | p50 | p95 |
| --- | ---: | ---: | ---: |
| Framework-dependent IL | 108,767,771 bytes | 472.11 ms | 519.73 ms |
| Framework-dependent ReadyToRun | 135,945,987 bytes | 383.77 ms | 476.68 ms |

ReadyToRun improved p50 by 88.34 ms (18.71%) and p95 by 43.05 ms (8.28%) at a 27,178,216-byte size cost. This evidence justified retaining R2R for startup-critical assemblies instead of switching the release to all IL.

## Size and startup optimization

The July 25, 2026 optimization removed the FPS/ETW feature and its runtime graph, excluded framework-provided Windows ML native binaries before dependency-file generation, reduced the PNG tray asset to its rendered size, and applied balanced R2R. The installer keeps `lzma2/max` solid compression and now rejects non-native-x64 Windows. Final budgets and measured payloads are:

| Artifact | Before | Final | Reduction |
| --- | ---: | ---: | ---: |
| R2R publish directory | 136,001,208 bytes | 68,924,181 bytes | 49.32% |
| IL publish directory | 108,767,771 bytes | 51,938,493 bytes | 52.25% |
| Installer package | 36,094,770 bytes | 15,127,372 bytes | 58.09% |

Startup was measured on the same machine with two excluded warmups and 15 samples. To control for background-load drift, the retained pre-optimization layout and final layout were measured back-to-back for each scope:

| Scope | Metric | Paired baseline | Final | Change |
| --- | --- | ---: | ---: | ---: |
| Tray / `tray-ready` | p50 | 383.89 ms | 384.63 ms | +0.74 ms (+0.19%) |
| Tray / `tray-ready` | p95 | 426.72 ms | 449.38 ms | +22.67 ms (+5.31%) |
| Shell / `shell-ready` | p50 | 542.19 ms | 498.85 ms | -43.34 ms (-7.99%) |
| Shell / `shell-ready` | p95 | 640.04 ms | 567.90 ms | -72.14 ms (-11.27%) |

Shell p50 clears the optimization target of both 5% and 15 ms. Both Tray and Shell pass the historical p50/p95 regression gate. When the first visible rendering tick begins, the path emits `shell-ready`; it then queues Acrylic/Mica, tray integration, the dedicated-key listener, and the compiled lower Home graphics/display template for the next dispatcher turn. The milestone is a consistent rendering-pipeline boundary, not proof that the compositor has presented pixels. Re-run both safe scopes after SDK, WinAppSDK, startup, XAML, or deployment-layout changes.

## Native AOT audit

Native AOT is not a production mode. It is self-contained for .NET, requires trimming, and must never be presented as another framework-dependent R2R setting.

Run the clean, isolated audit:

```powershell
.\build\aot-audit.ps1
```

The script writes `artifacts/aot-audit/report.json` and `publish.log`, changes no production output, and fails unless the publish has zero trim/AOT/CsWinRT/XAML diagnostics and complete PRI/XBF resources. Even a compile pass is only eligible for manual regression; promotion additionally requires the full UI, activation, hardware-write, FanGuard, helper, diagnostics, startup, and installer matrix.

The current audit is blocked by trim/AOT diagnostics from `LibreHardwareMonitorLib`, `System.Management`, and transitive `HidSharp`, including ILC methods that would always throw. These diagnostics are not suppressed. Windows App SDK also has an open unpackaged Native AOT PRI/XBF publishing defect, so Native AOT remains an audited candidate only.
