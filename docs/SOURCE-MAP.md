# 固定源码行为对照

参考仓库 https://github.com/alejandrobujan/tendedero ，提交 `d991454`。只读核对相邻参考项目；Shotline 不修改或打包 Swift 工程。

| 上游模块 | 实际行为 | Shotline 对应 |
| --- | --- | --- |
| `AppDelegate.swift` | 生命周期、托盘、约 250 ms 顶部停留、约 500 ms 离开收起、2.5 秒新图展示、指针屏幕、宽度容量、登录启动 | `App.xaml.cs` / `TrayService` / `LinePresenter`：Win32 消息、分离窗口、250/650 ms、2.8 秒、新图屏幕、Run 注册项显式开关 |
| `LinePanel.swift` | 非激活透明面板，浮动，不进窗口循环，卡片区域动态接收鼠标 | `Native.Configure` / `RopeWindow` / `CardWindow`：NOACTIVATE、TOOLWINDOW、无任务栏；装饰窗完全透明穿透，卡片分窗依赖分层窗口像素命中 |
| `LineView.swift` | 抛物线绳线、174 点间距、空列表提示、上下偏移动画 | `Theme` / `RopeWindow` / `LinePresenter` / `HintWindow`：集中布局参数、原创配色、空提示和进入/收起 |
| `PeggedView.swift` | 图片按比例缩略、夹子、随机轻倾斜、入场摆动、偶发微风及复制反馈 | `CardWindow.xaml` / `.cs` / `CardViewModel`：纸卡、绿色夹子、稳定 ID 倾角、比例缩略、仅显示时摆动、复制反馈 |
| `CaptureFlight.swift` | 读取 macOS 截图区域 xattr；知道来源时飞向卡片，移除时跨屏下落 | `CardWindow`：Windows 常规图片无可信截图区域元数据，采用卡片从顶部落入和下落移除；不伪造截图坐标。取消拖放摆动回位 |
| `ScreenshotWatcher.swift` | 目录变更后防抖扫描，macOS 桌面依据截图标签过滤，启动前旧图不导入 | `ImportService`：FileSystemWatcher、有界串行队列、文件稳定与有限解码重试、重命名/编辑/删除、启动后候选、错误恢复；不使用 macOS 标签 |
| `Inbox.swift` | 改写 macOS 截图位置和浮动缩略图配置，退出恢复；管理文件放应用目录 | 剪贴板副本放 `Imports`；目录图片外部引用。不写 Windows 截图、OneDrive 设置，也没有接管截图位置模式 |
| `Line.swift` | 路径去重、缩略图、容量淘汰、复制/打开/另存、列表恢复；关闭 Inbox 图会回收 | `ScreenshotLibrary` / `ImageCodec` / `AppController` / `StateStore`：增加像素跨源去重；关闭所有来源均只移除记录；回收独立确认；原子状态及备份保留 |
| `GrabArea.swift` | 单击复制、双击打开、0.45 秒长按 Markup、Finder 固定移动意图、取消拖放 | `CardWindow`：单击等待双击期、0.5 秒长按、OLE 文件复制偏好及复制/移动协商；仅确认源消失后按移动移除 |
| `Markup.swift` | NSSharingService 私有系统编辑服务及保存回调 | 可配置外部程序，传一个完整文件路径参数；监听原文件保存更新。不宣称等价回调，也不改绑编辑器另存的新路径 |
| `FullScreen.swift` | 私有 CGS Space API 按屏识别当前全屏 Space | `Native.IsFullscreen`：公开 Win32/DWM 窗口边界、可见性与屏幕中心 Z 序启发式；排除普通最大化。限制列于 README 和验收表 |
| `HotKey.swift` | Carbon 全局 Control+Option+T，无失败提示 | `DesktopBridge`：RegisterHotKey、Ctrl+Alt+T、可配置修饰键与虚拟键、失败状态/日志及托盘设置入口 |

实现使用 .NET/WPF 和公开 Win10 API，没有 Win11 专属依赖，也没有跨平台适配层。图标、配色和 WPF 视图原创。上游 MIT 与品牌限制原文见 `THIRD-PARTY-NOTICES.md`。

## 实现目录

- `src/Shotline.Core`：纯数据集合、来源、去重、自身复制策略、原子状态。
- `src/Shotline`：后台导入解码、MVVM 集合、WPF 卡片/绳线/设置、Win32、托盘和 Shell 回收。
- `tests/Shotline.Tests`：跨平台数据行为测试。
- `tests/Shotline.WindowsTests`：Windows WPF 解码、真实文件监听、可选桌面自动检查。
- `scripts`：构建、发布、便携包验证、原创图标重建。

## 公开平台契约

- [分层窗口及透明命中](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)
- [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
- [剪贴板监听](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-addclipboardformatlistener)
- [IFileOperation 标志](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags)：使用 Windows 8 起提供的 `FOFX_RECYCLEONDELETE`。
- [.NET 10 系统支持](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)：官方生命周期与应用的 Win10 22H2 目标分别记录。
