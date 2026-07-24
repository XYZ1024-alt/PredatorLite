# PredatorLite

[![build](https://github.com/XYZ1024-alt/PredatorLite/actions/workflows/build.yml/badge.svg)](https://github.com/XYZ1024-alt/PredatorLite/actions/workflows/build.yml)
[![license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

PredatorLite 是面向 Acer Predator PHN16-71 的轻量控制工具，用普通用户权限提供 PredatorSense 中与日常使用相关的功能。当前硬件写入白名单仅验证到：

- Acer Predator PHN16-71
- BIOS V1.20
- Windows 11 x64

其他机型或 BIOS 版本仍可查看诊断信息，但所有硬件写入都会被禁用。

> [!WARNING]
> PredatorLite 仍处于预发布阶段。硬件控制存在固有风险；请先确认型号和 BIOS 完全匹配，并阅读[硬件安全边界](docs/hardware-safety.md)。

## 功能

- 安静、均衡、性能、极速和节能运行模式
- 自动、全速和带温度曲线的自定义风扇控制
- 混合显卡与独显直连切换，切换后明确提示重启
- 屏幕刷新率、LCD 响应加速和 80% 充电上限
- 四区静态键盘灯、动态灯效和机身标志灯
- Windows 键、粘滞键快捷触发、开机音效和键盘灯超时等设备开关
- CPU/GPU、风扇、内存、显存、电池与可选 FPS 监控
- 系统托盘、单实例、中英文界面、可选 OSD 与全局快捷操作
- 每次唤出自动停靠鼠标所在显示器右下角，并在运行期接管 PredatorSense 专用键
- Acer 服务状态、冲突服务备份/停用/恢复和脱敏诊断包

PredatorLite 不提供用户超频、电压调节、功耗墙修改、MSR/NVAPI 写入、BIOS 写入或 vBIOS 工具。

## 安全模型

- 启动阶段只执行能力探测和遥测读取，不自动恢复上次硬件设置。
- 每次写入都来自明确的用户操作，或来自用户主动启用的供电状态自动化。
- 写入仅在 PHN16-71 / V1.20 白名单匹配后开放；端点支持查询时执行结果回读，其余操作要求明确的传输成功响应。
- GPU 路由只有 `Hybrid = 2` 和 `Discrete = 1`，没有 iGPU-only 或禁用 Windows 显卡设备的路径。
- 全速或自定义风扇启用前必须启动独立 FanGuard。主程序失联 5 秒或异常退出时，FanGuard 会恢复 EC 自动风扇。
- 主程序以普通用户权限运行。只有停用或恢复冲突服务时启动固定命令白名单的管理员辅助程序。

详见 [架构说明](docs/architecture.md) 与 [硬件安全边界](docs/hardware-safety.md)。

## 依赖

PredatorLite 复用 Acer 官方驱动和服务提供的接口，不附带或替换固件：

- `AcerServiceSvc`：运行模式、风扇、显卡路由和部分设备设置
- `AcerLightingService`：键盘和标志灯光
- `AcerApplicationBaseDriver_Device`：Acer WMI/硬件桥接驱动
- `AcerQAAgentSvis`：可选，仅用于物理性能模式键通知；PredatorSense 启动键使用独立键盘监听

应用不会停用上述必需组件。设置页只允许管理经过固定白名单识别的 PredatorSense 冲突服务，并在 `%ProgramData%\PredatorLite\service-backup.json` 保存启动方式备份。

部分 Acer WMI 方法可能被当前驱动 ACL 拒绝。此时 PredatorLite 保持普通用户权限，不会为轮询遥测请求管理员权限；WMI 专属的 CPU 温度、风扇转速、充电上限或键盘灯超时会显示为不可用。自定义风扇遇到缺失温度时按 95°C 处理并使用 100% 转速，不会静默使用低转速。

## 构建

需要 Windows 11 x64 和 .NET SDK 10.0.302 或兼容的 10.0.x SDK。界面使用 WinUI 3，项目固定使用 Microsoft Windows App SDK 1.8.260710003 稳定版：

```powershell
dotnet restore PredatorLite.slnx
dotnet build PredatorLite.slnx -c Release --no-restore
dotnet test PredatorLite.slnx -c Release --no-build
```

从源码启动本地界面：

```powershell
dotnet run --project src\PredatorLite.App\PredatorLite.App.csproj
```

框架依赖发布：

```powershell
.\build\publish.ps1
```

输出位于 `publish\win-x64`，其中包含主程序、FanGuard 和管理员辅助程序。这是免安装、框架依赖的 x64 目录发布，不使用 MSIX。目标机器需要同时安装：

- .NET 10 Runtime x64
- Windows App Runtime 1.8 x64

发布目录必须整体保留，不能只复制 `PredatorLite.exe`。在完整目录中启动 `PredatorLite.exe`；运行 `build\publish.ps1` 后脚本会检查三个 EXE、运行时配置、WinUI PRI/XBF、Bootstrap DLL 和图标资源是否齐全。该目录版默认没有 Authenticode 签名，不属于下述签名安装器流水线。`main` 推送和手动运行的 CI 会把该目录保存 14 天，artifact 名称包含 `UNSIGNED-TEST-ONLY`；PR 只验证构建，不上传可下载产物。

Inno Setup 本地安装测试包：

```powershell
.\build\build-installer.ps1 -SkipSigning
```

输出为 `artifacts\installer\unsigned\PredatorLite-Setup-0.1.0-win-x64-unsigned.exe`。未签名载荷和测试包都位于忽略的 `artifacts`，不会读写 `publish`。`main` 推送和手动运行的 CI 还会生成同类安装器，并把它作为保留 14 天的 `PredatorLite-installer-UNSIGNED-TEST-ONLY` Actions artifact；artifact 内含未签名警告，只能用于测试，不能附加到 GitHub Release 或作为正式版本分发。当前 CI 没有创建或修改 GitHub Release 的权限。可重复的临时证书签名、安装、卸载与时间戳测试使用 `build\test-installer-signing.ps1`；其 `-test-signed` 产物同样只存在于 `artifacts` 并在测试结束时删除。

生产构建要求把带私钥、Code Signing EKU 且由公共信任 CA 签发的 Authenticode 证书导入 `CurrentUser\My`，且证书链根必须存在于 Windows `LocalMachine\AuthRoot`，然后执行：

```powershell
$env:PREDATORLITE_SIGNING_THUMBPRINT = "<certificate SHA-1 thumbprint>"
.\build\build-installer.ps1
```

生产构建不会修改 `publish\win-x64`。它在每次调用的独立 `artifacts` 工作目录中签署 8 个 PredatorLite 自有 EXE/DLL，并用 SignTool 固定到预期证书验证 SHA-256 签名和 RFC 3161 时间戳；Inno Setup 同样签署 Setup 与内嵌卸载器，测试脚本会安装后验证卸载器。Setup 验证和 `.sha256` 生成全部成功后，脚本才通过同父目录移动将两个文件提升到 `publish\installer`。提升前失败会保留已有正式安装包；提升开始后失败会删除候选和不完整目标。证书自签名、仅受本机私有根信任、已吊销、缺少私钥、用途错误或已过期时构建会失败。第三方 DLL 保留其原始发布者签名，不会被 PredatorLite 重新签署。

## 项目结构

```text
src/PredatorLite.App              WinUI 3 UI、托盘、OSD 与应用编排
src/PredatorLite.Core             模型、接口、设置与风扇曲线安全逻辑
src/PredatorLite.Platform.Windows AcerService、WMI 与 Windows 只读监控
src/PredatorLite.FanGuard         风扇故障恢复看门狗
src/PredatorLite.ElevatedHelper   固定白名单的服务管理辅助程序
tests/PredatorLite.Tests          协议、曲线、设置与能力边界测试
```

## 来源边界

PredatorLite 是独立实现的互操作项目，不分发 Acer 源码、反编译代码、驱动、固件、ROM 或厂商素材。固定协议值、验证方法和贡献要求见[协议来源说明](docs/protocol-provenance.md)。本地忽略的研究目录不属于项目或 Git 历史，也不能作为贡献代码与素材的来源。

PredatorLite 不是 Acer 官方产品，也不隶属于 Acer。Acer、Predator 和 PredatorSense 名称仅用于说明兼容性。

完整的发布前检查见[手动测试清单](docs/manual-testing.md)。

## 贡献与安全

提交代码前请阅读[贡献指南](CONTRIBUTING.md)。安全问题请按[安全策略](SECURITY.md)私下报告，不要在公开 Issue 中披露漏洞、机器密钥或未经脱敏的诊断信息。

## License

PredatorLite 源码和原创项目素材采用 [MIT License](LICENSE)。素材范围见 [ASSET-LICENSE.md](ASSET-LICENSE.md)，依赖组件及其许可证见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
