## Summary

Describe the user-visible behavior and why the change is needed.

## Safety impact

- Supported model / BIOS tested:
- Hardware writes added or changed:
- Capability gate and read-back behavior:
- Failure and recovery behavior:
- Protocol provenance (if applicable):

Use `N/A` where a field does not apply. Never attach proprietary Acer binaries, firmware, serial numbers, or unredacted diagnostics.

## Verification

- [ ] `dotnet build PredatorLite.slnx -c Release --no-restore`
- [ ] `dotnet test PredatorLite.slnx -c Release --no-build`
- [ ] `dotnet format PredatorLite.slnx --verify-no-changes --no-restore`
- [ ] Applicable checks from `docs/manual-testing.md`
- [ ] Screenshots included for visible UI changes
