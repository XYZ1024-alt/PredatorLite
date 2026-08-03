# PredatorLite

[![build](https://github.com/XYZ1024-alt/PredatorLite/actions/workflows/build.yml/badge.svg)](https://github.com/XYZ1024-alt/PredatorLite/actions/workflows/build.yml)
[![license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

PredatorLite 是面向 Acer Predator 设备的独立、非官方 PredatorSense 替代方案。它使用普通用户权限提供性能、散热、灯光、显卡路由、电池与遥测控制，并通过显式硬件 profile 管理写入能力。

当前硬件写入 profile 仅验证到：

- Acer Predator PHN16-71
- BIOS V1.20
- Windows 11 24H2（build 26100+）x64

其他机型或 BIOS 版本仍可查看诊断信息和只读遥测，但没有已验证 profile 时所有硬件写入都会被禁用。新增写入支持必须逐机型、逐 BIOS 提供独立协议证据和人工验证。

当前正式版本为 `v1.0.1`。PredatorLite 是独立、非官方的 PredatorSense 替代方案，不代表 Acer 官方产品或授权。硬件控制存在固有风险；请确认当前设备存在匹配的已验证 profile，并阅读[硬件安全边界](docs/hardware-safety.md)。首次运行新发布版本时，Windows 可能显示 SmartScreen 信誉提示。

## 功能

- 安静、均衡、性能、极速和节能运行模式
- 自动、全速和带温度曲线的自定义风扇控制
- 混合显卡与独显直连切换，切换后明确提示重启
- 屏幕刷新率、LCD 响应加速和 80% 充电上限
- 四区静态键盘灯、动态灯效和机身标志灯
- Windows 键、粘滞键快捷触发、开机音效和键盘灯超时等设备开关
- CPU/GPU、风扇、内存、显存、电池与性能浮窗监控
- 系统托盘、单实例、中英文界面、可选 OSD 与全局快捷操作
- 每次唤出自动停靠鼠标所在显示器右下角，并在运行期接管 PredatorSense 专用键
- Acer 服务状态、冲突服务备份/停用/恢复和脱敏诊断包

PredatorLite 不提供用户超频、电压调节、功耗墙修改、MSR/NVAPI 写入、BIOS 写入或 vBIOS 工具。

## 安全模型

- 每次主实例启动只自动恢复最后保存的运行模式；风扇、灯光、显卡路由及其他硬件设置不会重放。
- 每次写入都来自明确的用户操作、用户主动启用的供电状态自动化，或上述运行模式启动恢复。
- 写入仅在匹配显式硬件 profile、对应控制项已授权且后端能力探测成功后开放；未知机型或 BIOS 保持只读。
- 端点支持查询时执行结果回读；多步操作失败时不会把部分成功伪报为完整成功。
- GPU 路由只有 `Hybrid = 2` 和 `Discrete = 1`，没有 iGPU-only 或禁用 Windows 显卡设备的路径。
- 全速或自定义风扇启用前必须启动独立 FanGuard。主程序失联 5 秒或异常退出时，FanGuard 会恢复 EC 自动风扇。
- 主程序以普通用户权限运行。只有停用或恢复冲突服务时启动固定命令白名单的管理员辅助程序。

详见 [架构说明](docs/architecture.md) 与 [硬件安全边界](docs/hardware-safety.md)。

## Code signing policy

Free code signing is provided by [SignPath.io](https://signpath.io/), with the certificate issued by the [SignPath Foundation](https://signpath.org/).

- Authors, committers, and reviewers: [XYZ1024-alt](https://github.com/XYZ1024-alt)
- Approvers: [XYZ1024-alt](https://github.com/XYZ1024-alt)
- Every signing request must come from the automated GitHub Actions build for this repository and requires manual approval in SignPath.
- Only PredatorLite-owned binaries are signed under this policy. Third-party binaries retain their upstream signatures or remain unsigned.

Privacy statement: This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it. Runtime communication is limited to services on the local machine. Diagnostic archives are created only through an explicit user action and are saved to a location selected by the user.

## 依赖

PredatorLite 复用 Acer 官方驱动和服务提供的接口，不附带或替换固件：

- `AcerServiceSvc`：运行模式、风扇、显卡路由和部分设备设置
- `AcerLightingService`：键盘和标志灯光
- `AcerApplicationBaseDriver_Device`：Acer WMI/硬件桥接驱动
- `AcerQAAgentSvis`：可选，仅用于物理性能模式键通知；PredatorSense 启动键使用独立键盘监听

应用不会停用上述必需组件。设置页只允许管理经过固定白名单识别的 PredatorSense 冲突服务，并在 `%ProgramData%\PredatorLite\service-backup.json` 保存启动方式备份。

部分 Acer WMI 方法可能被当前驱动 ACL 拒绝。此时 PredatorLite 保持普通用户权限，不会为轮询遥测请求管理员权限；WMI 专属的 CPU 温度、风扇转速、充电上限或键盘灯超时会显示为不可用。自定义风扇遇到缺失温度时按 95°C 处理并使用 100% 转速，不会静默使用低转速。

## 构建

需要 Windows 11 24H2（build 26100+）原生 x64 和 `global.json` 固定的 .NET SDK 10.0.302。所有 Windows 项目统一面向 `net10.0-windows10.0.26100.0`，界面使用稳定版 Microsoft Windows App SDK 2.3.1：

```powershell
dotnet restore PredatorLite.slnx
dotnet build PredatorLite.slnx -c Release --no-restore
dotnet test PredatorLite.slnx -c Release --no-build
$env:Configuration = "Release"
dotnet format PredatorLite.slnx --verify-no-changes --no-restore
```

从源码启动本地界面：

```powershell
dotnet run --project src\PredatorLite.App\PredatorLite.App.csproj
```

框架依赖 ReadyToRun 发布：

```powershell
.\build\publish.ps1
```

输出位于 `publish\win-x64`，其中包含主程序、FanGuard 和管理员辅助程序。这是免安装、框架依赖的 x64 目录发布，不使用 MSIX。目标机器需要同时安装：

- .NET 10 Runtime x64
- Windows App Runtime 2.3 x64

发布目录必须整体保留，不能只复制 `PredatorLite.exe`。发布脚本分别发布主程序、FanGuard 和 ElevatedHelper，再合并各自拥有的文件；默认采用经过测量验证的平衡型 framework-dependent ReadyToRun：启动关键程序集保留 R2R，延迟遥测和未使用的 AI/ML/Widgets 托管投影保持 IL。脚本拒绝非 AMD64 原生 PE、32 位托管程序集、ARM/x86 子目录、TraceEvent 残留和 framework-dependent 布局中不应本地携带的 Windows ML 原生运行库，并执行 80 MiB 的 R2R 预算。使用 `build/prepare-release.ps1 -Version 1.0.1` 可生成正式发布包。使用 `build/publish.ps1 -ReadyToRun:$false` 可生成 IL 对照布局，预算为 65 MiB；正式发布脚本固定使用经过验证的 ReadyToRun 布局。

正式 v1.0.1 发布包：

```powershell
.\build\prepare-release.ps1 -Version 1.0.1
```

脚本会生成以下四个资产到 `publish\release`：

- `PredatorLite-1.0.1-win-x64-portable.zip`
- `PredatorLite-1.0.1-win-x64-portable.zip.sha256`
- `PredatorLite-Setup-1.0.1-win-x64.exe`
- `PredatorLite-Setup-1.0.1-win-x64.exe.sha256`

发布资产使用 framework-dependent ReadyToRun 便携目录和普通用户安装器。目标机器需要 .NET 10 Runtime x64、Windows App Runtime 2.3 x64，以及 Windows 11 24H2（build 26100+）原生 x64。首次运行新发布版本时，Windows 可能显示 SmartScreen 信誉提示；发布前后都应使用对应 `.sha256` 文件校验资产。

Inno Setup 本地安装测试包：

```powershell
.\build\build-installer.ps1 -SkipSigning
```

输出为 `artifacts\installer\unsigned\PredatorLite-Setup-1.0.1-win-x64-unsigned.exe`。内部测试载荷和测试包位于忽略的 `artifacts`，不会读写 `publish`；该路径只用于 signing-gates，不作为正式发布入口。`main` 推送和手动运行 `build` 工作流会上传 `PredatorLite-win-x64-portable` 与 `PredatorLite-installer` 两个正式命名的 Actions artifact。正式 v1.0.1 发布由 `.github\workflows\release.yml` 在 `main` 推送时处理；同版本 Release 已存在时不会覆盖。

完整的临时证书签名、安装、卸载与时间戳集成测试不阻塞日常构建。发布前必须在本地运行 `build\test-installer-signing.ps1`；需要检查 GitHub 托管环境时，可从 Actions 页面手动运行 `installer signing gates` 工作流。该手动工作流不上传 artifact，也不能创建或修改 GitHub Release；其 `-test-signed` 产物只存在于临时 runner，并在测试结束时删除。

可选的证书签名构建要求把带私钥、Code Signing EKU 且由公共信任 CA 签发的 Authenticode 证书导入 `CurrentUser\My`，且证书链根必须存在于 Windows `LocalMachine\AuthRoot`，然后执行：

```powershell
$env:PREDATORLITE_SIGNING_THUMBPRINT = "<certificate SHA-1 thumbprint>"
.\build\build-installer.ps1
```

证书签名构建不会修改 `publish\win-x64`。它在每次调用的独立 `artifacts` 工作目录中签署 8 个 PredatorLite 自有 EXE/DLL，并用 SignTool 固定到预期证书验证 SHA-256 签名和 RFC 3161 时间戳；Inno Setup 同样签署 Setup 与内嵌卸载器，测试脚本会安装后验证卸载器。Setup 验证和 `.sha256` 生成全部成功后，脚本才通过同父目录移动将两个文件提升到 `publish\installer`。提升前失败会保留已有正式安装包；提升开始后失败会删除候选和不完整目标。证书自签名、仅受本机私有根信任、已吊销、缺少私钥、用途错误或已过期时构建会失败。第三方 DLL 保留其原始发布者签名，不会被 PredatorLite 重新签署。

## 项目结构

```text
src/PredatorLite.App              WinUI 3 UI、托盘、OSD 与应用编排
src/PredatorLite.Core             模型、接口、设置与风扇曲线安全逻辑
src/PredatorLite.Platform.Windows AcerService、WMI 与 Windows 只读监控
src/PredatorLite.FanGuard         风扇故障恢复看门狗
src/PredatorLite.ElevatedHelper   固定白名单的服务管理辅助程序
tests/PredatorLite.Tests          协议、曲线、设置与能力边界测试
benchmarks/PredatorLite.Benchmarks 包编解码、曲线与遥测微基准
```

## 来源边界

PredatorLite 是独立实现的互操作项目，不分发 Acer 源码、反编译代码、驱动、固件、ROM 或厂商素材。固定协议值、验证方法和贡献要求见[协议来源说明](docs/protocol-provenance.md)。本地忽略的研究目录不属于项目或 Git 历史，也不能作为贡献代码与素材的来源。

PredatorLite 不是 Acer 官方产品，也不隶属于 Acer。Acer、Predator 和 PredatorSense 名称仅用于说明兼容性。

完整的发布前检查见[手动测试清单](docs/manual-testing.md)。

## 贡献与安全

提交代码前请阅读[贡献指南](CONTRIBUTING.md)。安全问题请按[安全策略](SECURITY.md)私下报告，不要在公开 Issue 中披露漏洞、机器密钥或未经脱敏的诊断信息。

## License

PredatorLite 源码和原创项目素材采用 [MIT License](LICENSE)。素材范围见 [ASSET-LICENSE.md](ASSET-LICENSE.md)，依赖组件及其许可证见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
