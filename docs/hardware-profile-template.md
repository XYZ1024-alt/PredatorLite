# Hardware Profile Contribution Template

Use this template for a proposed writable profile. Keep the evidence independently authored, redacted and reviewable. A profile without complete evidence may be registered for read-only diagnostics only.

## Identity

- Profile ID:
- Manufacturer aliases (exact values):
- Model (exact value):
- BIOS version (exact value):
- Windows build and architecture:
- Validation machine ownership and date:

## Control matrix

For each proposed control, complete one row.

| Control | Allowed values | Primary transport | Fallback | Capability probe | Read-back | Failure/recovery result |
| --- | --- | --- | --- | --- | --- | --- |
| Operating mode | | | | | | |
| Fan mode/curve | | | | | | |
| GPU routing | | | | | | |
| Battery health | | | | | | |
| Lighting | | | | | | |
| Device settings | | | | | | |
| Display controls | | | | | | |

## Evidence record

- Request/response or observation references for every mapping:
- Rejected values and transport errors:
- Partial-write behavior:
- Read-back samples and timing:
- FanGuard timeout, parent exit and normal shutdown results:
- Unknown model/BIOS read-only result:
- Manual test log location after redaction:
- Independent reviewer:

Do not attach Acer binaries, drivers, firmware, ROMs, vendor artwork, serial numbers, machine secrets or unredacted diagnostics. The profile must be added as an explicit catalog entry with focused tests; do not remove or bypass the profile gate.
