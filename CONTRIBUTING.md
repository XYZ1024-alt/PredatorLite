# Contributing to PredatorLite

PredatorLite controls real laptop hardware. Correctness, recovery behavior, and provenance take priority over feature breadth.

## Before opening a change

- Search existing issues and keep each pull request focused.
- Do not submit Acer binaries, drivers, firmware, ROMs, decompiled source, vendor artwork, serial numbers, or unredacted diagnostics.
- Read [`docs/hardware-safety.md`](docs/hardware-safety.md) and [`docs/protocol-provenance.md`](docs/protocol-provenance.md) before changing a hardware-facing path.
- Report security vulnerabilities through the private process in [`SECURITY.md`](SECURITY.md), not a public issue.

## Build and test

Use Windows 11 x64 and the .NET 10 SDK pinned by `global.json`.

```powershell
dotnet restore PredatorLite.slnx
dotnet build PredatorLite.slnx -c Release --no-restore
dotnet test PredatorLite.slnx -c Release --no-build
dotnet format PredatorLite.slnx --verify-no-changes --no-restore
```

For UI changes, run the applicable checks in [`docs/manual-testing.md`](docs/manual-testing.md) and include screenshots. Do not run hardware-write tests on an unvalidated model or BIOS.

## Hardware changes

A new write target requires all of the following:

1. Exact manufacturer, model, and BIOS identification.
2. Independently captured request/response evidence.
3. Explicit value mapping and capability gating.
4. Read-back verification where the transport supports it.
5. Failure-path tests and fan recovery coverage where applicable.
6. Documentation of the hardware and BIOS used for manual validation.

Never broaden the current PHN16-71 / BIOS V1.20 whitelist based only on a marketing-family match.

## Pull requests

Use an imperative Conventional Commit-style title such as `feat(platform): ...` or `fix(app): ...`. The pull request should describe:

- the user-visible behavior;
- hardware and safety impact;
- automated commands run;
- manual checks performed;
- supported model and BIOS for hardware-write testing;
- protocol provenance for new interoperability facts.

By submitting a contribution, you agree that it is your original work (or that you have the right to submit it) and that it is licensed under the repository's MIT License.
