# Security Policy

## Supported versions

PredatorLite is pre-release software. Security fixes are applied to the latest commit on `main`; older snapshots and unpublished local builds are not supported.

## Reporting a vulnerability

Use this repository's [private vulnerability report form](https://github.com/XYZ1024-alt/PredatorLite/security/advisories/new). Private Vulnerability Reporting is enabled. Do not open a public issue for vulnerabilities involving:

- the elevated helper or service-management boundary;
- named-pipe authentication or process activation;
- arbitrary hardware writes or capability-gate bypasses;
- fan recovery failures;
- diagnostic data exposure;
- unsafe parsing of AcerService, WMI, or Quick Access input.

Include a concise reproduction, affected commit, impact, and any proposed mitigation. Remove serial numbers, account names, machine-specific AES values, logs containing personal paths, and proprietary Acer material before attaching evidence.

If private reporting is unavailable, open a public issue containing only a request for a private contact channel. Do not include exploit details.

## Scope

The project does not provide security support for Acer firmware, drivers, services, or PredatorSense. Reports about those components should be sent to the vendor. Fixed protocol constants used by localhost interoperability are not account credentials; machine-local secrets must never be committed or included in diagnostics.
