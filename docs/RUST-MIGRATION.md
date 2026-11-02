# Rust 核心迁移（开发版）

本次采用 Rust 核心 + WPF 界面。已有 JSON 配置、FSS1 会话格式和 HUD 渲染器继续使用；ETW、LibreHardwareMonitor 与 Windows 窗口接入仍在托管适配层。历史发布包 v0.0.2 不包含本次修改。

## 实现边界

- `rust/focus-native`：无第三方 crate 的 Rust 库；拥有帧时间线、环形缓冲、统计计算和原子文件写入。
- `Focus.Core/Native`：C ABI 适配与 SafeHandle；逐次检查错误码，启动首次调用检查 ABI 版本。数组在调用期间借用，不跨边界转移所有权。
- `FrameTimeline`：保持既有 C# 调用接口，实际存储与统计由 Rust 执行。Rust Mutex 保护时间线，托管锁保护跨调用快照。
- `TelemetryHub`：目标切换、Present 处理、历史记录与快照发布使用同一把锁。硬件读取仍由独立采样线程负责。
- 共享内存：保留双槽协议，增加进程间 Mutex，避免读者取得混合的槽位、长度与内容；读取繁忙时跳过一轮。
- 配置、设置、会话：Rust 写入唯一同目录临时文件、刷新到磁盘、重试替换；失败保留旧目标，不直接截断目标。设置失败返回状态并通知界面。

## 已修复

1. 首个帧间隔丢失、末尾负帧间隔和环形回绕统计错误。
2. NaN/Infinity 等无效统计样本；停止呈现后的 FPS 衰减。
3. 传感器未匹配却借用同类首个传感器；风扇真实 0 RPM 被误判不可用。
4. 缺失 GPU/CPU 样本按零计算；无数据时硬件统计现在保存为 JSON null。历史文件中的零值不能可靠还原为缺失值。
5. 超过 5 秒的真实卡顿被丢弃；停止录制与晚到帧之间的状态检查竞态。
6. 时间线与历史字典的并发访问、目标切换时旧帧进入新时间线。
7. 设置重试后兜底不可达及失败静默；配置保存失败被吞掉；会话直接覆盖文件。
8. 编辑器再次加载后刷新计时器未重启；预览和覆盖层历史采用不同采样预算。两者现在统一使用 20 Hz 曲线样本，原始逐帧数据仍用于会话统计。
9. 同一指标多个曲线窗口发送数据时只满足较短窗口；发布线程持有可变编辑文档。
10. 覆盖层优先启动残留的同目录程序；ETW 处理线程异常退出后运行状态不更新。

## 构建与验证

需要 .NET 10 SDK、PowerShell 7、Rust 工具链和可用的 Windows GNU x64 链接器；安装 Rust 的 `x86_64-pc-windows-gnu` target。运行时不需要 Rust。

```powershell
pwsh -File build-native.ps1 -Test
pwsh -File build-native.ps1 -Lint
dotnet test tests/Focus.Tests/Focus.Tests.csproj -c Release
dotnet build Focus.slnx -c Release
pwsh -File build.ps1
```

MSBuild 自动调用 `build-native.ps1`，把 `focus_native.dll` 放入所有相关项目的构建和发布输出。GNU 链接器的中间文件放在按仓库路径散列的临时目录，规避中文项目路径问题。项目仅支持 x64；不要混用其他架构 DLL。

测试覆盖原始帧间隔、卡顿、回绕、并发读写、非有限数、缺失传感器、文件锁定时保留原文件、设置备份恢复、IPC 并发发布与旧格式往返。测试不需要管理员权限或启动硬件驱动。

接口依据：[Rust FFI](https://doc.rust-lang.org/nomicon/ffi.html)、[Windows MoveFileExW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-movefileexw)。导出函数使用 C 调用约定、固定布局数据与明确长度；Rust panic 转换为失败码。

## 仍需实测或另行设计

- Windows 11、真实游戏、反作弊、多显示器、高 DPI、HDR、数小时资源占用与安装升级。
- 按 PID 聚合 Present，尚不区分交换链，也不代表显示端实际展示帧。
- 独占全屏不支持普通置顶 HUD，换语言不能消除这个显示模式限制。
- 部分传感器需要管理员权限或驱动；不存在的传感器保持不可用。
- 实时低帧统计基于最近 60,000 个 Present；完整录制统计基于会话数据。
- 编辑器大型文件拆分、叠加会话对比曲线、画布相对分布属于后续功能/维护工作。
- 会话当前最多记录 8,000,000 个帧间隔；超长录制的分段落盘与流式统计尚未实现。

不得把上述实测项描述为已通过，也不宣称完成了整个应用的纯 Rust 重写。
