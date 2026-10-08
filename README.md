# Shotline · 晾图

Windows 截图暂存工具。图片挂在顶部绳线上，指针停留后展开，离开后收起；单击复制、双击打开、长按编辑，也可拖入资源管理器或接受文件的应用。

C# + .NET 10 + WPF。兼容目标为 Windows 10 **22H2 x64** 和 Windows 11 x64，共用一份包含运行时的便携 ZIP。功能源码、行为测试和 Windows 构建工作流已提供；实际验证记录见 [验收清单](docs/ACCEPTANCE.md)。构建成功不代表桌面交互已验收。

## 使用

1. 从仓库的 **Actions → Windows portable → 成功构建 → Artifacts** 下载 `Shotline-win-x64-提交号`。解开 Actions 下载包，再解开其中的 `Shotline-1.0.0-win-x64.zip`。
2. 将整个 `Shotline` 文件夹放到稳定位置，双击 `Shotline.exe`。无需另装 .NET，首次运行默认不开机启动、不开启剪贴板导入。
3. 从托盘打开“设置与运行状态”，确认实际监听目录。默认尝试 Windows 截图已知目录，否则使用图片目录下的 `Screenshots`；目录不存在会明确显示未监听。不会修改截图工具、OneDrive 或系统截图快捷键。
4. 使用现有截图方式。`Win+Shift+S` 等只向剪贴板写图片的来源需要主动开启剪贴板图片导入。此模式也会接收普通复制图片；开启时不读取已有剪贴板内容。

默认快捷键 **Ctrl+Alt+T**，可在设置修改。指针在当前显示器工作区顶部约 250 ms 后展开；离开约 650 ms 后收起。新图展示约 2.8 秒。长按、菜单和拖放期间保持展开。

| 卡片操作 | 行为 |
| --- | --- |
| 单击 | 双击判定期结束后复制图片；成功后显示反馈，占用时有限重试并报告失败 |
| 双击 | 系统默认图片应用打开，取消待执行的单击复制 |
| 长按约 500 ms | 调用外部编辑器，默认 `mspaint.exe`；保存原文件后自动刷新 |
| 拖出 | 提供真实文件及复制偏好，允许目标协商复制/移动；取消或失败回位 |
| 关闭按钮 / 从晾图移除 | 仅移除记录，保留文件 |
| 另存为 | 复制原文件，保留卡片和来源 |
| 移到回收站 | 单独确认后调用 Windows Shell 回收，不使用永久删除作为替代 |

右键还提供复制、打开、编辑、打开所在文件夹。托盘提供显示/收起、监听启停、剪贴板导入、设置、清空和退出。重复启动唤起现有实例。设置支持系统/浅色/深色主题、声音、减少动画及开机启动。

## 数据与诊断

数据位于 `%LOCALAPPDATA%\Shotline`，设置提供各目录入口：

| 路径 | 用途 |
| --- | --- |
| `state.json` | 配置与有效展示记录；原子写入 |
| `state.json.bak` | 上一次有效状态；恢复损坏主文件 |
| `state.json.corrupt-时间` | 恢复时保留的损坏主文件 |
| `state.json.tmp.recovered-ID` | 从中断的首次写入恢复时保留的临时文件 |
| `Imports` | 剪贴板管理副本；不自动清理 |
| `Logs` | 事件、项目 ID、错误类型及代码；每个日志约 1 MiB，最多主文件加 3 份轮转 |

目录监听和手动导入保留外部文件位置。关闭卡片、清空和 3–12 张容量淘汰均只移除展示记录，**不会清理外部文件或管理副本**。文件失效后移除记录。回收站操作仅接受本地固定磁盘文件；网络或可移动盘文件可在资源管理器中处理。

损坏状态优先从备份恢复并保留损坏主文件；备份不可用时尝试中断写入的临时文件并保留原副本；均无法恢复时禁用写入。请先保留这些文件，再手工修复或移到其他目录后重启。不要在应用运行时编辑状态文件。

目录事件经有界队列处理，稳定检查及解码共最多 8 次尝试。监听目录恢复每 5 秒检查，不做高频全盘扫描。队列满或文件读取失败会通过托盘、设置和日志提示，可手动导入重试。支持 PNG、JPEG、BMP；每个文件最多 64 MiB / 4000 万像素。

相同路径重复事件合并，编辑保留原记录 ID。30 秒内像素一致的图片合并跨来源事件；同图晚到外部目录时优先引用外部源文件，原管理副本保留。JPEG 有损编码改变像素后不保证与 PNG 去重。自身“复制图片”通过剪贴板标记和序列号抑制。

## 构建与发布

Windows 安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，在仓库根目录使用 PowerShell 7：

```powershell
pwsh -NoProfile -File .\scripts\build.ps1
dotnet run --project .\src\Shotline\Shotline.csproj --configuration Release --no-build -- --settings
pwsh -NoProfile -File .\scripts\publish.ps1
```

输出为 `artifacts\UTC时间\Shotline-1.0.0-win-x64.zip`，附 SHA-256 和构建信息。脚本检查 x64 PE、运行时文件，并解压后运行隔离的 `--ci-smoke` 启动/退出检查。测试和构建失败会阻止发布。

Windows 交互桌面上的额外自动检查会改写剪贴板，包含非激活窗口、快捷键冲突和仅对测试副本的回收操作：

```powershell
pwsh -NoProfile -File .\scripts\build.ps1 -DesktopTests
```

macOS 仅能执行核心测试及 Windows 目标的跨平台编译，无法运行 WPF：

```bash
dotnet run --project tests/Shotline.Tests --configuration Release
dotnet build Shotline.slnx --configuration Release
```

图标可用 Python 3 标准库重建：

```bash
python3 scripts/make-icon.py
```

GitHub Actions 自动执行 Windows Release 构建、核心测试、Windows 解码/监听测试、自包含发布及隔离启动检查，产物保留 30 天。工作流本身不发布 Release，也不执行 Win10/Win11 真机验收。

## 已知边界

- 全屏检测使用显示器中心最上层可见窗口的 Win32 边界，排除普通有标题栏最大化、桌面及 DWM 隐藏窗口。独占全屏、特殊叠加窗口、跨屏窗口和虚拟桌面仍需实测，不能等同 macOS 私有 Space API。
- 绳线完全点击穿透；卡片窗口只在有可见像素的区域响应。多屏负坐标、PerMonitorV2 DPI 和热插拔已有实现，实际混合 DPI、焦点和穿透需双系统真机确认。
- 外部编辑器只接收一个原文件路径参数，没有 Markup 回调。另存为新路径时，原卡片不自动改绑新文件。
- 拖放效果由目标决定。只有目标报告移动且源文件已不存在才按移动移除；文件监听也会清理已失效路径。资源管理器和具体应用的兼容性尚未真机确认。
- 便携包未签名，Windows 可能显示 SmartScreen 提示。开机启动引用当前 EXE 完整路径，移动便携目录后应重新开关注册。
- .NET 10 的官方系统支持按版本和生命周期划分；应用目标 Win10 22H2 不等于仍受微软支持。不默认承诺更早 Win10 或 LTSC。参见 [.NET 10 支持系统](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)及[支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)。

## 来源与许可

参考 [Tendedero](https://github.com/alejandrobujan/tendedero)，固定源码 `d991454`，Windows 实现重新编写并对照其交互意图。Shotline 使用独立名称、原创 SVG/ICO；不是上游官方 Windows 版本，不暗示作者背书。保留上游 MIT 声明，详见 [来源对照](docs/SOURCE-MAP.md)、[第三方声明](THIRD-PARTY-NOTICES.md)及 [LICENSE](LICENSE)。应用不使用后端、账号、网络同步、遥测或自动更新。
