# Microsoft Store Submission Guide

This document is the release source of truth for PredatorLite's Microsoft Store listing and manual Stable submission. GitHub remains the distribution channel for portable ZIP and Inno Setup EXE assets. Microsoft Store publishes Stable MSIX packages only.

## Product identity

These values are case-sensitive and come from Partner Center **Product management > Product identity**. Do not regenerate or substitute them.

| Field | Value |
|---|---|
| Reserved product name | `PredatorLite` |
| Store product ID | `9PBFRHTRXL2Q` |
| `Package/Identity/Name` | `XYZ1024.PredatorLite` |
| `Package/Identity/Publisher` | `CN=AD743FEC-8EA7-47AF-A7E4-DB022A7071DA` |
| `Package/Properties/PublisherDisplayName` | `XYZ1024` |
| Publisher ID | `qs69q4d43t9q8` |
| Package family name | `XYZ1024.PredatorLite_qs69q4d43t9q8` |
| Partner Center product | <https://partner.microsoft.com/dashboard/products/9PBFRHTRXL2Q/overview> |

The checked-in identity is duplicated intentionally in:

- `src/PredatorLite.Package/Package.appxmanifest`;
- `src/PredatorLite.Package/Package.StoreAssociation.xml`.

## Listing metadata

### Shared properties

| Field | Value |
|---|---|
| Category | Utilities & tools |
| Pricing | Free |
| Markets | All markets permitted by Partner Center |
| Architecture | x64 only |
| Minimum OS | Windows 11 24H2, build 26100 |
| Privacy policy URL | <https://github.com/XYZ1024-alt/PredatorLite/blob/main/PRIVACY.md> |
| Support URL | <https://github.com/XYZ1024-alt/PredatorLite/issues> |
| Website | <https://github.com/XYZ1024-alt/PredatorLite> |
| License | MIT |

Do not claim universal Acer hardware support. The current hardware-write profile is limited to Acer Predator PHN16-71 with BIOS V1.20. Other models or BIOS versions remain diagnostics/read-only unless a separately validated profile is added.

### English (United States)

**Product name**

PredatorLite

**Short description**

A safe, lightweight PredatorSense alternative for supported Acer Predator laptops.

**Description**

PredatorLite is an independent, unofficial Windows control utility for compatible Acer Predator laptops. It provides operating modes, fan control, keyboard and logo lighting, GPU routing, battery and display settings, live hardware monitoring, tray access, OSD, and global shortcuts in a compact WinUI interface.

Hardware writes are disabled unless the exact model and BIOS match an independently validated profile. The current writable profile is Acer Predator PHN16-71 with BIOS V1.20 on Windows 11 24H2 x64. Other devices remain available for diagnostics and read-only telemetry.

Safety is built into each hardware path. Custom and maximum fan modes require FanGuard, which restores automatic fan control if the main app stops heartbeating. GPU routing is limited to verified Hybrid and Discrete values and requires a restart. The main app runs as an ordinary user; administrator approval is requested only when the user explicitly disables or restores a fixed allowlist of conflicting Acer services.

Microsoft Store delivers Stable updates automatically. PredatorLite contains no advertising, analytics, accounts, cloud synchronization, or automatic diagnostic upload.

PredatorLite is not an Acer product and is not affiliated with or endorsed by Acer. Acer, Predator, and PredatorSense are used only to describe compatibility.

**Feature bullets**

- Silent, Balanced, Performance, Turbo, and battery Eco operating modes
- Automatic, maximum, and validated custom fan curves with FanGuard recovery
- Keyboard and logo lighting controls
- Hybrid graphics and discrete-GPU routing with restart confirmation
- Battery charge limit, display refresh rate, and LCD response controls
- CPU, GPU, fan, memory, VRAM, battery, and system monitoring
- System tray, optional OSD, bilingual UI, startup task, and global shortcuts
- Explicit model/BIOS write authorization; unsupported systems remain read-only

**Search terms**

`Acer Predator`, `PredatorSense alternative`, `fan control`, `hardware monitor`, `laptop control`

### Chinese (Simplified)

**产品名称**

PredatorLite

**简短说明**

面向受支持 Acer Predator 笔记本的安全、轻量 PredatorSense 替代工具。

**说明**

PredatorLite 是面向兼容 Acer Predator 笔记本的独立、非官方 Windows 控制工具。它以紧凑的 WinUI 界面提供运行模式、风扇控制、键盘与标志灯光、显卡路由、电池与屏幕设置、实时硬件监控、系统托盘、OSD 和全局快捷操作。

只有设备机型和 BIOS 与独立验证的硬件 profile 完全匹配时，硬件写入才会开放。当前可写 profile 仅适用于 Windows 11 24H2 x64 上的 Acer Predator PHN16-71 与 BIOS V1.20。其他设备仍可使用诊断与只读遥测功能。

每条硬件路径都包含明确的安全边界。自定义和全速风扇模式必须先建立 FanGuard 保护；主程序停止心跳后，FanGuard 会恢复自动风扇。显卡路由仅允许经过验证的混合模式与独显直连值，并要求重启。主程序始终以普通用户权限运行；只有用户明确停用或恢复固定白名单内的 Acer 冲突服务时才请求管理员批准。

Microsoft Store 自动提供正式版更新。PredatorLite 不包含广告、分析、账号、云同步或自动诊断上传。

PredatorLite 不是 Acer 官方产品，也不隶属于 Acer。Acer、Predator 和 PredatorSense 名称仅用于说明兼容性。

**功能要点**

- 安静、均衡、性能、极速与电池节能运行模式
- 自动、全速与经过验证的自定义风扇曲线，支持 FanGuard 故障恢复
- 键盘与标志灯光控制
- 混合显卡与独显直连切换，并明确提示重启
- 电池充电上限、屏幕刷新率与 LCD 响应控制
- CPU、GPU、风扇、内存、显存、电池与系统监控
- 系统托盘、可选 OSD、中英文界面、启动任务与全局快捷操作
- 按机型和 BIOS 显式授权写入；不受支持的系统保持只读

**搜索词**

`Acer Predator`、`PredatorSense 替代`、`风扇控制`、`硬件监控`、`笔记本控制`

## Listing images

Package logos in `src/PredatorLite.Package/Assets` are app-package assets, not Partner Center screenshots.

For each locale, capture at least one new desktop screenshot at 1366x768 or larger on an ordinary-user session. Show the application at a readable scale with no serial number, username, machine key, unredacted log, diagnostic archive, or unrelated desktop notification. Recommended sequence:

1. Home with operating modes and overview cards.
2. Cooling with Auto/Max and CPU custom curve visible.
3. Lighting with the four-zone static keyboard preview.
4. Monitor with plausible telemetry and no stale/error state.
5. Settings showing the Microsoft Store update description.

Use the matching UI language for each listing. Do not reuse the portrait README images as Store screenshots.

## Certification notes

Paste and maintain the following facts in **Notes for certification**:

> PredatorLite is an independent, unofficial control utility for Acer Predator laptops. No account or test credentials are required. The main WinUI process runs as a standard user and is declared as a desktop full-trust application because it integrates with local Acer services and Windows hardware APIs.
>
> The package contains two first-party executables: `PredatorLite.exe` and `PredatorLite.FanGuard.exe`. FanGuard is a current-user safety watchdog. It starts only before Max or Custom fan control, requires a named-pipe handshake and lease, and restores automatic fan control if the main app stops heartbeating for five seconds.
>
> The Microsoft Store package does not include `PredatorLite.ElevatedHelper.exe`, does not declare a `windows.fullTrustProcess` helper extension, and hides Disable conflicts and Restore services. Those user-initiated administrative actions remain available only in the GitHub distribution.
>
> Hardware writes are authorized only for an exact validated model/BIOS profile. The current writable profile is Acer Predator PHN16-71 with BIOS V1.20 on Windows 11 24H2 x64. Other systems remain diagnostics/read-only. Certification can exercise navigation, settings, logs, diagnostics, and read-only monitoring without that device; write controls will remain disabled when the profile does not match.
>
> The user-configurable `PredatorLiteStartup` StartupTask launches the app hidden in the system tray. The application does not automatically re-enable a startup task disabled by the user or policy.
>
> The Store build has no in-app update checker or installer downloader. Microsoft Store supplies updates. The app has no ads, analytics, accounts, cloud sync, or automatic diagnostics upload. Hardware communication is local to Acer services on the same computer.

Disclose `runFullTrust`, the local Acer service and hardware integrations, and the FanGuard safety process in Partner Center when requested. The Store package does not declare `allowElevation` or a `windows.fullTrustProcess` helper extension, and the main process remains `asInvoker`. Do not describe FanGuard as an elevated service or privileged broker.

## Build artifacts

The Store package is intentionally not part of `PredatorLite.slnx`. It requires the Visual Studio MSIX Packaging Tools/WAP targets.

Stable upload candidate:

```powershell
.\build\build-store-package.ps1 `
  -Configuration Release `
  -Version 1.1.0 `
  -Mode StoreUpload

.\build\test-store-package.ps1 `
  -PackagePath publish\store\PredatorLite-Store-1.1.0-win-x64.msixupload `
  -ExpectedVersion 1.1.0.0
```

The result is:

- `publish\store\PredatorLite-Store-1.1.0-win-x64.msixupload`;
- `publish\store\PredatorLite-Store-1.1.0-win-x64.msixupload.sha256`.

Sideload package for disposable-machine validation:

```powershell
.\build\build-store-package.ps1 `
  -Configuration Release `
  -Version 1.1.0 `
  -Mode Sideload `
  -CertificatePath C:\secure\PredatorLite-Test.pfx `
  -CertificatePassword $certificatePassword
```

Use only a disposable test certificate whose subject matches the manifest publisher. Trust it only on disposable test machines. Install through the WAP-generated `_Test` directory and dependency installer; do not create or maintain a separate install script. Remove the test certificate and package after validation.

## Stable submission procedure

1. Merge the intended Stable source revision to `main`. Confirm `Directory.Build.props` contains the exact three-component Stable base version.
2. Manually dispatch `.github/workflows/release.yml` with `channel=stable`, an empty `iteration`, and `confirm_public=true`.
3. Confirm the workflow completes the existing portable ZIP and Inno Setup validations, then builds and validates the Store upload before creating the GitHub Release.
4. Download the Actions artifact named `PredatorLite-Store-<version>-win-x64`. Artifact retention is 30 days.
5. Verify the `.msixupload` against its SHA-256 sidecar. Do not use or upload a candidate whose hash differs.
6. Open Partner Center product `9PBFRHTRXL2Q` and create a new submission. The first submission and every Store publication remain manual.
7. Upload the `.msixupload` under **Packages**. Confirm Partner Center recognizes identity `XYZ1024.PredatorLite`, publisher `CN=AD743FEC-8EA7-47AF-A7E4-DB022A7071DA`, the `<version>.0` package version, x64 architecture, and the existing reserved product.
8. Complete Properties, age ratings, English and Simplified Chinese listings, privacy URL, support URL, screenshots, restricted-capability declarations, and certification notes from this document.
9. Review the package flight and availability settings. Microsoft Store distributes Stable only; do not submit Beta or RC packages.
10. Submit for certification. Store certification may take up to three business days, so the GitHub Stable release and Store listing need not become public at the same minute.
11. Monitor certification and publication. Preserve the workflow run, source commit, GitHub tag, artifact hash, Partner Center submission ID, and certification result in the release record.
12. If certification rejects the package, fix the source or metadata at the repository boundary. For a package change, increment `Directory.Build.props` and run a new Stable workflow. Do not rebuild a different package from another commit under the rejected version.

The GitHub and Store artifacts for a Stable version must come from the same workflow run and commit. They are alternative distribution channels: simultaneous execution is blocked, and settings are not guaranteed to migrate or be shared across package identities.
