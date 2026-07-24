# Performance and deployment evidence

## Policy

Performance gates run on one fixed Acer Predator PHN16-71 machine. Hosted CI restores, builds, tests, publishes, and executes only the BenchmarkDotNet `Dry` smoke job; hosted timing is not a release gate.

Do not change hardware-write ordering, FanGuard coverage, process priority, GC mode, or undocumented JIT settings to improve a benchmark. The ordinary startup path must still complete identity, BIOS, power, backend, capability, and read-back checks before its one allowed operating-mode restore.

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
- `tray-ready`
- `critical-ready` or `critical-failed`
- `deferred-ready` or `deferred-failed`
- `redirect-activated` after a redirected launch reaches the existing window

`build/measure-startup.ps1` also supplies a private named pipe and uses the same QPC timestamps. Its default `Tray` scope passes `--startup-tray-only`: the hidden window and tray are created, then startup stops before settings-driven hardware initialization. This safe scope may run beside a developer instance using an isolated measurement instance key.

`Critical` and `Deferred` scopes execute normal initialization and can restore the validated operating mode. They require both `-AllowHardwareInitialization` and a closed existing instance, and may be run only on the fixed PHN16-71 / BIOS V1.20 machine.

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

`build/publish.ps1` publishes App, FanGuard, and ElevatedHelper separately for `win-x64`, then composes one framework-dependent directory. ReadyToRun is enabled by default and the script verifies the managed native header on all five first-party assemblies. Use `-ReadyToRun:$false` only to create an IL comparison layout.

The promotion measurement on July 24, 2026 used .NET 10.0.10, Windows build 26200, an Intel Core i5-13500HX, two excluded warmups, and 15 hidden tray-only samples per layout:

| Layout | Size | p50 | p95 |
| --- | ---: | ---: | ---: |
| Framework-dependent IL | 108,767,771 bytes | 472.11 ms | 519.73 ms |
| Framework-dependent ReadyToRun | 135,945,987 bytes | 383.77 ms | 476.68 ms |

ReadyToRun improved p50 by 88.34 ms (18.71%) and p95 by 43.05 ms (8.28%) at a 27,178,216-byte size cost. This evidence justified enabling R2R for the unpackaged release. Re-run the comparison after SDK, WinAppSDK, startup, XAML, or deployment-layout changes.

## Native AOT audit

Native AOT is not a production mode. It is self-contained for .NET, requires trimming, and must never be presented as another framework-dependent R2R setting.

Run the clean, isolated audit:

```powershell
.\build\aot-audit.ps1
```

The script writes `artifacts/aot-audit/report.json` and `publish.log`, changes no production output, and fails unless the publish has zero trim/AOT/CsWinRT/XAML diagnostics and complete PRI/XBF resources. Even a compile pass is only eligible for manual regression; promotion additionally requires the full UI, activation, hardware-write, FanGuard, helper, diagnostics, startup, and installer matrix.

The current audit is blocked by trim/AOT diagnostics from `LibreHardwareMonitorLib`, `Microsoft.Diagnostics.Tracing.TraceEvent`, `System.Management`, and transitive `HidSharp`, including ILC methods that would always throw. These diagnostics are not suppressed. Windows App SDK also has an open unpackaged Native AOT PRI/XBF publishing defect, so Native AOT remains an audited candidate only.
