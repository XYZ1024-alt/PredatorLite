# Third-Party Notices

PredatorLite is licensed under the MIT License. Its build and published output also use the third-party components below. Each component remains subject to its own license.

## Production dependencies

| Component | Version | License | Source |
| --- | ---: | --- | --- |
| Microsoft Windows App SDK | 2.3.1 | Microsoft Software License Terms | https://github.com/microsoft/WindowsAppSDK |
| Microsoft Windows App SDK AI | 2.3.4 | Microsoft Software License Terms | https://github.com/microsoft/WindowsAppSDK |
| Microsoft Windows App SDK WinUI | 2.3.0 | Microsoft Software License Terms and bundled third-party notices | https://github.com/microsoft/WindowsAppSDK |
| Microsoft Windows App SDK ML | 2.1.74 | Microsoft Software License Terms and bundled third-party notices | https://github.com/microsoft/WindowsAppSDK |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | https://github.com/CommunityToolkit/dotnet |
| CommunityToolkit.WinUI Controls | 8.2.251219 | MIT | https://github.com/CommunityToolkit/Windows |
| H.NotifyIcon.WinUI | 2.4.1 | MIT | https://github.com/HavenDV/H.NotifyIcon |
| Microsoft WebView2 | 1.0.3719.77 | BSD-3-Clause-style redistribution terms and notice | https://developer.microsoft.com/microsoft-edge/webview2 |
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0 | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/3d331e3370efb858411f19511373eff65a218701 |
| Microsoft.Diagnostics.Tracing.TraceEvent | 3.2.5 | MIT | https://github.com/microsoft/perfview |
| System.Management | 10.0.10 | MIT | https://github.com/dotnet/dotnet |
| System.ServiceProcess.ServiceController | 10.0.10 | MIT | https://github.com/dotnet/dotnet |

## Notable transitive runtime dependencies

| Component | Version | License | Source |
| --- | ---: | --- | --- |
| BlackSharp.Core | 1.0.7 | MPL-2.0 | https://github.com/Blacktempel/BlackSharp/tree/c70b735c6cec123ee8a046ac4a0bc6c606f52cf0 |
| DiskInfoToolkit | 1.1.2 | MPL-2.0 | https://github.com/Blacktempel/DiskInfoToolkit/tree/25319eae5781e75bcf141e844ceab2afe94d40ea |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0 | https://github.com/Blacktempel/RAMSPDToolkit/tree/3b47b960e0830fef344624ad5e389675d5f0a1ce |
| HidSharp | 2.6.4 | Apache-2.0 | https://software.seekye.com/hidsharp |
| Mono.Posix.NETStandard | 1.0.0 | MIT/X11 and component-specific notices | https://github.com/mono/mono |
| Microsoft.Extensions runtime libraries | 6.0.0 | MIT | https://github.com/dotnet/runtime |
| .NET Windows support libraries | 10.0.10 | MIT and bundled third-party notices | https://github.com/dotnet/runtime |
| Microsoft Windows SDK .NET runtime projections | 10.0.26100.57 | Microsoft Windows SDK License Terms | https://www.nuget.org/packages/Microsoft.Windows.SDK.NET.Ref/10.0.26100.57 |

## Installer packaging dependency

| Component | Version | License | Source |
| --- | ---: | --- | --- |
| Inno Setup | 6.x | Inno Setup License | https://jrsoftware.org/isinfo.php |

The MPL-covered libraries are consumed unmodified from NuGet. Their corresponding source versions are available at the links above and through the repository metadata embedded in each NuGet package.

Full license material shipped with release bundles:

- `LICENSE` - PredatorLite MIT License
- `licenses/MPL-2.0.txt` - Mozilla Public License 2.0
- `licenses/HidSharp.txt` - HidSharp notice and Apache License 2.0
- `licenses/Inno-Setup.txt` - Inno Setup copyright and redistribution terms
- `licenses/Microsoft-Windows-App-SDK.txt` - shared Microsoft Windows App SDK, AI, and WinUI terms
- `licenses/Microsoft-Windows-App-SDK-NOTICE.txt` - Windows App SDK and WinUI third-party notices
- `licenses/Microsoft-Windows-App-SDK-ML.txt` - Windows App SDK ML terms
- `licenses/Microsoft-Windows-App-SDK-ML-NOTICE.txt` - Windows App SDK ML third-party notices
- `licenses/Microsoft-Windows-SDK.rtf` - Microsoft Windows SDK terms for the shipped .NET runtime projections
- `licenses/WebView2-LICENSE.txt` and `licenses/WebView2-NOTICE.txt` - WebView2 redistribution terms and notice
- `licenses/CommunityToolkit-dotnet-LICENSE.md`, `licenses/CommunityToolkit-Windows-LICENSE.md`, and `licenses/CommunityToolkit-NOTICE.txt` - CommunityToolkit licenses and notices
- `licenses/H-NotifyIcon-LICENSE.md` - H.NotifyIcon MIT License
- `licenses/dotnet-MIT.txt` and `licenses/dotnet-NOTICE.txt` - .NET runtime license and third-party notices
- `licenses/Mono-LICENSE.txt` - Mono runtime and class-library license information

Development and test packages are listed by `dotnet package list --project PredatorLite.slnx --include-transitive`. NuGet package metadata is authoritative if this notice and a restored package disagree.
