# 架构与关键决策记录

## D1 · 技术栈：.NET 10 + WPF（而非 Electron / Qt / Rust）

- **选择**：C# / .NET 10 + WPF，两个可执行程序（主程序 + 覆盖层）。
- **理由**：
  - 与 Windows 原生集成成本最低（P/Invoke、DWM、ETW、DPI、热键、托盘）。
  - WPF 提供硬件无关的矢量渲染，编辑器画布与覆盖层可共用同一套绘制代码，保证"所见即所得"。
  - .NET 生态直接可用 LibreHardwareMonitor 与 TraceEvent（ETW）。
  - 单文件自包含发布可行，安装体积可控。
- **代价**：分层透明窗口（AllowsTransparency）在 WPF 中走软件合成路径。HUD 面积小（数百像素见方），实测更新成本可接受；若后续需要更大面积/更高帧率，可迁移到 Direct2D/DirectComposition，渲染层已隔离在 `Focus.Hud`。

## D2 · 帧数据：自建 ETW 消费者（Microsoft-Windows-DxgKrnl Present 事件）

- **选择**：使用 `Microsoft.Diagnostics.Tracing.TraceEvent` 订阅 `Microsoft-Windows-DxgKrnl` 的 `Present_Info`（ID 184）实时事件，按进程 ID 归属帧。
- **理由**：
  - D3D9/11/12、Vulkan、OpenGL 在 Windows 上的最终呈现都会经过 DxgKrnl，覆盖面与 PresentMon 一致。
  - 避免捆绑/派生 PresentMon 二进制，减少供应链与授权复杂度（TraceEvent 为 MIT）。
  - 直接获得呈现时间戳，可自行实现帧时间、1% Low、会话记录。
- **限制与缓解**：
  - 需要管理员权限创建 ETW 实时会话（与 Afterburner/RTSS 等同类工具一致）；未提权时明确提示且不显示伪造数据。
  - 仅统计"提交的 Present"，不等同于显示端合成结果；与引擎内置计数可能存在个位数差异（文档说明）。
  - **实测结论（Windows 10 19045）**：`Present` 事件不匹配 0x8000000 或 0x6B 关键字，关键字为 0 时才会投递。最终采用内核级 EventID 过滤：`EnableProvider(guid, Verbose, keywords=0, new TraceEventProviderOptions { EventIDsToEnable = {184} })`。诊断确认会话内只投递 Present 事件（`events=0`），避免了全量 DxgKrnl 事件的解析开销。
  - 仍保留保护逻辑：若 6 秒内完全没有事件，自动回退为"不过滤"模式（并写入 `%TEMP%\focus-etw.log`），保证在其它 Windows 版本上不会完全采不到帧。

## D3 · 硬件数据：LibreHardwareMonitorLib（MPL-2.0）

- 直接引用 NuGet 0.9.6，不自带内核驱动。
- 传感器按名称启发式映射（CPU Package / GPU Core / D3D 3D / SmallData 显存等），缺失即视为不可用。
- 采样线程 `BelowNormal` 优先级，默认 500 ms 可配置；数据以不可变快照发布，UI/HUD 永不阻塞于硬件 IO。

## D4 · 覆盖层进程隔离 + 共享内存 IPC（JSON + 双缓冲）

- **选择**：`FocusOverlay.exe` 独立进程；主程序通过命名共享内存（"Focus.Overlay.State.<实例>"）推送不可变快照，版本号 + 双槽位。
- **理由**：
  - 渲染崩溃不会拖垮主程序；主程序卡顿不影响 HUD。
  - 覆盖层无需接触 ETW/硬件，权限与依赖最小化。
  - JSON 便于演进；每 50 ms 约 5–20 KB，开销可忽略。
- **协议**：头部 64 字节（Magic、槽位、长度、版本），两个 ~2 MB 槽；写者先写非当前槽，再发布长度/槽位，最后原子递增版本；读者版本不变则不解析。
- **退出**：主程序设置命名事件 `Focus.Overlay.Exit.<实例>`；覆盖层另有 20 秒无状态心跳超时自杀。

## D5 · 覆盖层窗口：WPF 分层窗口 + WS_EX_TRANSPARENT/NOACTIVATE

- `AllowsTransparency=true`、`WindowStyle=None`、`ShowActivated=false`、`Focusable=false`，并附加 `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`。
- 使用 `SetWindowPos(HWND_TOPMOST)` 按目标窗口/显示器物理像素定位；尺寸 = 画布 × 缩放 × 显示器 DPI。
- 不注入、不挂钩、不修改游戏进程 —— 不针对反作弊做任何规避。

## D6 · HUD 文档模型与统一渲染

- 单一 `HudProfile` JSON（元素为扁平数组 + Kind 判别），编辑器直接编辑内存模型，覆盖层解析同一份 JSON。
- `HudRenderer.Render(DrawingContext, profile, dataSource, pixelsPerDip)` 同时服务于：
  - 编辑器画布（含网格/选择框由编辑器额外绘制）
  - 覆盖层窗口
  - 配置文件缩略图（使用 `SampleHudDataSource` 仅用于设计预览）
- 指标访问通过 `IHudDataSource` 抽象，主程序、覆盖层、预览各有实现。

## D7 · 统计口径（明确且一致）

- **帧时间**：相邻两次 Present 的时间差（ms）。
- **平均 FPS**：`(帧数-1) / (首帧到末帧的秒数)`（会话内）。
- **1% / 0.1% Low**：将帧时间升序排序，取最慢的 1% / 0.1% 帧，求其平均帧时间 `t`，低帧率 = `1000 / t`（与 RTSS/CapFrameX 惯例一致）。
- **卡顿帧**：帧时间 > 中位数 × 2 的帧；同时报告占比。
- **实时 FPS 窗口**：最近 1 秒内 Present 事件按实际时间跨度计算；无新帧时随时间衰减到 0。
- 采样 FPS（事件计数）与基于帧时间的统计不混用。

## D8 · 配置与会话持久化

- 配置：`%AppData%\Focus\profiles\<id>.json`，原子写入（临时文件 + 替换），内置预设只读、编辑时自动复制为用户配置。
- 设置：`settings.json` + 上次备份，读取失败时自动回退备份。
- 会话：`.fss` = GZip(magic + JSON 元数据 + float32 帧时间数组)，只保存实测帧时间，统计在停止时计算。
- CSV：`frame,time_seconds,frametime_ms,fps`。

## D9 · 游戏识别与配置自动切换

- 每 1 秒检查前台窗口；仅当该进程近 1 秒至少有 4 次 Present，或已绑定配置时，才视为"游戏"并自动设为监控目标。
- 绑定关系存放于 `HudProfile.GameExecutable`（单一事实来源），避免设置与配置双写。
- 记忆最近目标进程，但不在启动时自动附加（避免误采集桌面程序）。

## D10 · 手动选择优先于自动检测

- 自动识别只在"当前没有监控目标"或"检测到的进程绑定了配置"时才替换目标；用户手动选择的目标不会被前台程序抢占。避免打开浏览器/桌面程序时误切目标。

## D11 · 开发/测试用实例隔离

- 环境变量 `FOCUS_INSTANCE` 改变命名对象后缀与数据目录（`%AppData%\Focus-<suffix>`），可并行运行多个实例。
- 命令行支持 `--page`、`--screenshot`、`--capture-screen`、`--hide-window`、`--attach <exe>`，用于 UI 回归截图与无交互验证。
