# PredatorLite Privacy Policy

Effective date: August 11, 2026

PredatorLite is an independent, unofficial Windows utility for compatible Acer Predator devices. This policy describes data handled by the PredatorLite application distributed through GitHub and Microsoft Store.

## Summary

PredatorLite does not include advertising, analytics, cloud synchronization, user accounts, or automatic crash-report upload. The application does not sell personal data. Hardware and application data is processed locally unless the user explicitly starts a network action described below.

## Data processed locally

PredatorLite may read and store the following information to provide its features:

- device manufacturer, model, BIOS version, capability state, and supported hardware profile;
- operating mode, fan, lighting, display, battery, and device-setting state;
- temperatures, loads, frequencies, fan speeds, memory, VRAM, battery, power, and display telemetry exposed by the device and Windows;
- application preferences, including language, startup behavior, shortcuts, OSD, polling, and automation choices;
- local Acer service status and the original startup modes of the fixed service allowlist when the user requests conflict management;
- application events, errors, and recovery outcomes written to local log files.

Settings and logs are stored in the current user's local application data. Windows may redirect packaged Microsoft Store data into the app's package-specific storage. In GitHub-distributed builds, using service conflict management stores the elevated helper's service backup at `%ProgramData%\PredatorLite\service-backup.json`. Microsoft Store builds do not include that helper or those service-management actions.

Logs replace the current user-profile path with `%USERPROFILE%`, retain no more than seven days, and are also bounded by file-count and total-size limits. PredatorLite does not log or export the machine-local AcerService AES registry value.

## Network communication

PredatorLite uses local connections to Acer services on the same computer for supported hardware control and telemetry. These localhost connections do not send data to PredatorLite servers.

GitHub-distributed builds contact GitHub only when the user selects **Check for updates** or confirms an installer download. These HTTPS requests use GitHub's release API and release-asset hosting. They include normal network metadata visible to GitHub, such as the user's IP address and a PredatorLite user-agent containing the application version. GitHub processes that data under the [GitHub Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).

Microsoft Store builds do not contain PredatorLite's GitHub update checker or installer downloader. Microsoft Store delivers and updates the package under Microsoft's terms and privacy practices. Selecting the GitHub project link opens the user's browser and is an explicit user action.

PredatorLite does not operate a remote telemetry, analytics, account, or diagnostics service.

## Diagnostic exports

A diagnostic archive is created only after the user selects **Export diagnostics** and chooses a destination. The archive can contain:

- device identity and capabilities;
- one current hardware snapshot;
- device-setting and managed-service state;
- application settings and version information;
- up to three recent redacted PredatorLite log files.

PredatorLite does not upload this archive. The user controls whether and where it is shared. Diagnostic archives may still reveal device configuration or error context; users should inspect them before sharing and should not post unredacted diagnostics publicly.

## Data deletion

Users can delete exported diagnostic archives and downloaded GitHub installers from the locations they selected or approved. Local settings and logs may remain after uninstall so that reinstalling does not silently erase user data. They can be removed by deleting PredatorLite's local application-data directory or package data after the app is closed. `%ProgramData%\PredatorLite\service-backup.json` may be deleted after conflicting Acer services have been restored and PredatorLite is closed.

## Children's privacy

PredatorLite is not directed at children and does not knowingly collect personal information from children.

## Changes to this policy

Material changes will be published in this repository with an updated effective date. The policy applicable to a release is the version published with that release's source revision.

## Contact

Privacy questions may be submitted through the [PredatorLite issue tracker](https://github.com/XYZ1024-alt/PredatorLite/issues). Security-sensitive reports must follow the repository's [security policy](SECURITY.md) instead of being posted publicly.
