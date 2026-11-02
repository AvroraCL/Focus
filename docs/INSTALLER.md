# 安装与卸载

`Focus-Setup-0.0.3-win-x64.exe` 是自包含的 Windows x64 安装包，包含主程序、覆盖层、更新程序及它们使用的 .NET 10 运行时。无需先安装 Rust 或 .NET。安装程序使用 Inno Setup 7，提供简体中文和英文向导。

## 安装

默认目录为 `%LocalAppData%\Programs\Focus`，只为当前用户安装，不要求管理员权限。可选择目录和桌面快捷方式；开始菜单包含 Focus 和卸载入口，Windows“已安装的应用”中登记卸载信息。程序采集 ETW 帧数据时仍需由用户提权。

重复运行安装包可覆盖安装和修复程序文件，保留配置与会话。安装器和卸载器检查 Focus 与覆盖层的运行互斥量；运行时要求先退出，不强制杀进程。请先结束录制，从托盘完全退出，再更新或卸载。

## 卸载

从 Windows 设置、开始菜单“卸载 Focus”，或安装目录内 `unins000.exe` 启动。卸载前选择：

- **是：完全清除**。删除安装的程序、随包运行时、快捷方式、卸载登记，并清理当前用户 `%AppData%\Focus` 的所有内容，包括配置、会话、日志、更新下载、安装备份和缓存。无法恢复。
- **否：保留数据**。删除程序及登记，保留配置和会话，便于重新安装。
- **取消**。不卸载。

只处理当前用户的生产数据目录，不读取 `FOCUS_DATA_ROOT` 环境变量，不清理 `Focus-<开发实例>` 或用户导出到外部的文件。清理时不跟随 junction / 符号链接；链接目标保持不变。如果数据被占用而无法删除，会提示具体目录并记录日志。

卸载依据安装日志及 `focus-installed-files.txt` 文件清单删除程序文件。安装版的更新程序会把新增文件纳入清单，失败时随文件一起回滚；覆盖安装保留已有清单，因此后续 ZIP 更新新增或退役的文件也会被清理。不递归删除用户指定安装目录中的陌生文件。ZIP 更新不会刷新 Windows 卸载登记的版本号，详见 [自动更新说明](AUTO-UPDATE.md)。

## 静默操作

```powershell
# 安装；不自动启动应用
.\Focus-Setup-0.0.3-win-x64.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART

# 卸载，默认保留个人数据
.\unins000.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART

# 完全卸载，明确删除当前用户的 Focus 个人数据
.\unins000.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /PURGEDATA=1
```

附加 `/LOG="完整日志路径"` 可以保存诊断日志。自动化操作应检查退出码；需要确认数据清除结果时，还应检查数据目录是否存在。

## 构建与验证

```powershell
pwsh -File build-installer.ps1 -CompilerPath 'C:\path\to\ISCC.exe'
pwsh -File installer/Test-Installer.ps1 -CompilerPath 'C:\path\to\ISCC.exe'
```

构建使用独立的 `artifacts/installer-payload` 目录，不覆盖已发布 ZIP。安装包与 SHA-256 校验文件输出到 `artifacts/installer`，随包附带 Focus 和 Microsoft 运行时许可证。

回归脚本编译独立测试 AppId 和独立数据目录的安装包，不使用真实 Focus 数据；检查安装、修复、已安装应用启动、运行中保护、保留数据卸载、完全清除、注册表与快捷方式清理、只读文件和目录链接边界。日志与启动截图保存在 `artifacts/installer-check/<随机ID>`。测试会临时写入当前用户的应用登记和开始菜单，卸载后移除。

2026-11-03 已在 Windows 10 x64 完成上述回归，并验证 ZIP 更新文件清单、覆盖安装后清单保留、越界路径拒绝和陌生文件保留。65 项 .NET 测试及发布版更新程序端到端检查通过，中文安装和卸载选项已通过桌面界面检查。测试包关闭压缩以缩短回归时间，正式包使用 LZMA2 压缩。
