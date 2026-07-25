# Interoperability and Protocol Provenance

PredatorLite is an independent, unofficial PredatorSense alternative. The repository contains factual interface information needed to communicate with Acer components installed on hardware owned and tested by project contributors.

## Profile evidence boundary

Hardware writes are enabled only through an explicit profile in the platform catalog. A profile matches exact manufacturer aliases, model and BIOS values after trimming and case-insensitive comparison. A Predator family name, a matching service, or a similar protocol value never authorizes a new target automatically.

Each writable profile must retain or reference a redacted evidence record containing:

- exact manufacturer, model, BIOS, Windows build and architecture;
- validation date and hardware test owner;
- per-control request/response observations and value mappings;
- transport, capability-probe and fallback behavior;
- read-back samples where supported;
- rejection, partial-write, timeout and recovery results;
- reviewer and redaction notes.

The current writable profile is `acer-predator-phn16-71-v1.20`, validated against an Acer Predator PHN16-71 with BIOS V1.20. New mappings require capture evidence, failure testing, read-back verification and recovery coverage. A target with incomplete evidence may be listed for read-only diagnostics but must not be added as a writable profile.

## Retained protocol facts

Retained protocol facts are limited to observable interface details such as:

- localhost endpoints and packet framing;
- request/response JSON field names and numeric packet IDs;
- WMI class and method names exposed by the installed driver;
- value mappings confirmed through read-back;
- physical key scan codes and notification payloads;
- Windows power-overlay identifiers.

## Fixed protocol values

The Quick Access handshake strings and fixed AES keys in `QuickAccessModeKeySource` are constants required by the localhost vendor protocol. They are shared interoperability values, not account credentials and not machine secrets.

A separate AcerService AES value may exist at `HKCU\Software\Acer\XSense\AESkey`. PredatorLite reads that machine-local value at runtime. It is not committed, logged, or included in diagnostics.

## Source boundary

The repository does not distribute Acer source code, decompiled code, executables, drivers, firmware, ROM images, or vendor artwork. The local `PreySense/` directory is explicitly ignored, is not part of the project or its Git history, and must not be used as a source for contributed code or assets.

Contributors must submit independently authored implementations and the profile evidence described above for each new hardware-write path. Do not attach proprietary binaries, firmware, serial numbers or unredacted diagnostics to issues or pull requests.

Acer, Predator and PredatorSense are trademarks of their respective owners. Their names are used only to identify compatible products and interfaces. This project is not affiliated with or endorsed by Acer.
