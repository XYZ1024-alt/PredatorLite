# PredatorLite

PredatorLite 是面向 Acer Predator PHN16-71 的轻量控制工具，用普通用户权限提供 PredatorSense 中与日常使用相关的功能。当前硬件写入白名单仅验证到：

- Acer Predator PHN16-71
- BIOS V1.20
- Windows 11 x64

其他机型或 BIOS 版本仍可查看诊断信息，但所有硬件写入都会被禁用。

## 功能

- 安静、均衡、性能、极速和节能运行模式
- 自动、全速和带温度曲线的自定义风扇控制
- 混合显卡与独显直连切换，切换后明确提示重启
- 屏幕刷新率、LCD 响应加速和 80% 充电上限
- 四区静态键盘灯、动态灯效和机身标志灯
- Windows 键、粘滞键快捷触发、开机音效和键盘灯超时等设备开关
- CPU/GPU、风扇、内存、显存、电池与可选 FPS 监控
- 系统托盘、单实例、中英文界面、可选 OSD 与全局快捷操作
- Acer 服务状态、冲突服务备份/停用/恢复和脱敏诊断包

PredatorLite 不提供用户超频、电压调节、功耗墙修改、MSR/NVAPI 写入、BIOS 写入或 vBIOS 工具。

## 安全模型

- 启动阶段只执行能力探测和遥测读取，不自动恢复上次硬件设置。
- 每次写入都来自明确的用户操作，或来自用户主动启用的供电状态自动化。
- 写入仅在 PHN16-71 / V1.20 白名单匹配后开放，并进行结果回读验证。
- GPU 路由只有 `Hybrid = 2` 和 `Discrete = 1`，没有 iGPU-only 或禁用 Windows 显卡设备的路径。
- 全速或自定义风扇启用前必须启动独立 FanGuard。主程序失联 5 秒或异常退出时，FanGuard 会恢复 EC 自动风扇。
- 主程序以普通用户权限运行。只有停用或恢复冲突服务时启动固定命令白名单的管理员辅助程序。

详见 [架构说明](docs/architecture.md) 与 [硬件安全边界](docs/hardware-safety.md)。

## 依赖

PredatorLite 复用 Acer 官方驱动和服务提供的接口，不附带或替换固件：

- `AcerServiceSvc`：运行模式、风扇、显卡路由和部分设备设置
- `AcerLightingService`：键盘和标志灯光
- `AcerApplicationBaseDriver_Device`：Acer WMI/硬件桥接驱动
- `AcerQAAgentSvis`：可选，仅用于物理模式键通知

应用不会停用上述必需组件。设置页只允许管理经过固定白名单识别的 PredatorSense 冲突服务，并在 `%ProgramData%\PredatorLite\service-backup.json` 保存启动方式备份。

部分 Acer WMI 方法可能被当前驱动 ACL 拒绝。此时 PredatorLite 保持普通用户权限，不会为轮询遥测请求管理员权限；WMI 专属的 CPU 温度、风扇转速、充电上限或键盘灯超时会显示为不可用。自定义风扇遇到缺失温度时按 95°C 处理并使用 100% 转速，不会静默使用低转速。

## 构建

需要 Windows 11 x64 和 .NET SDK 10.0.302 或兼容的 10.0.x SDK：

```powershell
dotnet restore PredatorLite.slnx
dotnet build PredatorLite.slnx -c Release --no-restore
dotnet test PredatorLite.slnx -c Release --no-build
```

框架依赖发布：

```powershell
.\build\publish.ps1
```

输出位于 `publish\win-x64`，其中包含主程序、FanGuard 和管理员辅助程序。目标机器需要安装 .NET 10 Desktop Runtime x64。

## 项目结构

```text
src/PredatorLite.App              WPF UI、托盘、OSD 与应用编排
src/PredatorLite.Core             模型、接口、设置与风扇曲线安全逻辑
src/PredatorLite.Platform.Windows AcerService、WMI 与 Windows 只读监控
src/PredatorLite.FanGuard         风扇故障恢复看门狗
src/PredatorLite.ElevatedHelper   固定白名单的服务管理辅助程序
tests/PredatorLite.Tests          协议、曲线、设置与能力边界测试
```

## 来源边界

根目录中的 `PreySense/` 仅作为行为研究参考，已被 `.gitignore` 排除。PredatorLite 不复用其中的代码、UI、资源、字体、二进制文件、ROM 或固件。

PredatorLite 不是 Acer 官方产品，也不隶属于 Acer。使用硬件控制功能前应确认型号与 BIOS 版本完全匹配。

## License

[MIT](LICENSE)
