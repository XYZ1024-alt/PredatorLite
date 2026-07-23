# Repository Guidelines

## Project Structure & Module Organization

`PredatorLite.slnx` groups five production projects under `src/`. `PredatorLite.App` contains the WinUI 3 interface, XAML resources, tray/OSD integration, and view models. `PredatorLite.Core` owns platform-neutral models, abstractions, settings, and fan safety logic. `PredatorLite.Platform.Windows` implements Acer protocols and Windows integration; `PredatorLite.FanGuard` and `PredatorLite.ElevatedHelper` are companion executables for fan recovery and narrowly scoped service management. Unit tests live in `tests/PredatorLite.Tests`; architecture, hardware safety, and manual QA notes live in `docs/`. Treat ignored `PreySense/` as research material only: do not copy its code, assets, binaries, or firmware.

## Build, Test, and Development Commands

Use Windows 11 x64 and the .NET 10 SDK pinned by `global.json`.

```powershell
dotnet restore PredatorLite.slnx
dotnet build PredatorLite.slnx -c Release --no-restore
dotnet test PredatorLite.slnx -c Release --no-build
dotnet format PredatorLite.slnx --verify-no-changes --no-restore
.\build\publish.ps1
```

These commands restore packages, compile the full solution, run xUnit tests, enforce formatting, and create a validated framework-dependent bundle in `publish\win-x64`. For local UI work, run `dotnet run --project src\PredatorLite.App\PredatorLite.App.csproj`.

## Coding Style & Naming Conventions

Follow existing C# conventions: four-space indentation, file-scoped namespaces, nullable reference types, and implicit usings. Use `PascalCase` for types and public members, `camelCase` for parameters and locals, and `I` prefixes for interfaces. Keep XAML views paired with their `.xaml.cs` code-behind and place reusable UI strings in both localization dictionaries. Run `dotnet format` before submitting.

## Testing Guidelines

Tests use xUnit and files follow `<Subject>Tests.cs`; test methods describe behavior, for example `MissingSafetyEndpointIsRejected`. Add focused tests for protocol encoding, capability gates, persistence, and every safety invariant changed. There is no numeric coverage threshold, but CI must pass. Hardware-facing changes also require the applicable checks in `docs/manual-testing.md`.

## Hardware Safety & Configuration

Do not broaden write support beyond the explicit Acer Predator PHN16-71 / BIOS V1.20 whitelist without protocol evidence, read-back verification, failure testing, and recovery coverage. Preserve ordinary-user execution and the fixed elevated-command whitelist. Never commit generated `bin/`, `obj/`, `publish/`, diagnostics, logs, or machine-specific settings.

## Commit & Pull Request Guidelines

History follows Conventional Commit-style subjects such as `feat(platform): ...`, `test: ...`, and `refactor(app): ...`. Keep commits focused and use an imperative summary. Pull requests should explain behavior and safety impact, link relevant issues, list automated and manual verification, and include screenshots for visible WinUI changes. Call out supported hardware and BIOS used for any hardware-write testing.
