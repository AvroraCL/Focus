# 自动更新

## 用户流程

设置中的“启动时自动检查更新”默认开启。主窗口显示 5 秒后查询 GitHub 最新正式版；后台失败不会弹出阻塞窗口。发现新版本时侧栏出现入口。设置页支持手动检查、下载进度、取消、重试和“重启并安装”。不会自动下载或静默重启。

安装前禁止进行中的录制，保存编辑器待保存内容和窗口位置。独立更新程序先校验并解压，通知主程序准备就绪；收到安装确认、主程序退出后才替换文件。关闭主程序也会等待覆盖层退出。更新程序位于数据目录的独立任务目录，不会覆盖自身正在使用的文件。

## 发布协议

- API：`https://api.github.com/repos/AvroraCL/Focus/releases/latest`，无认证请求，客户端不放置令牌。
- 标签：`vMAJOR.MINOR.PATCH`，跳过草稿、预发布与不高于当前版本的版本。
- ZIP：`Focus-vMAJOR.MINOR.PATCH-win-x64.zip`，根目录直接包含 `Focus.exe`、`Focus.dll`、运行时配置、`focus_native.dll`、`overlay/` 和 `updater/`。
- 校验：优先使用 GitHub asset 的 `sha256:` digest；否则使用同名 `.zip.sha256` 文件。没有有效 SHA-256 的新版本不能安装。
- `pwsh -File build.ps1` 发布 app、overlay 和 updater，输出 ZIP 与 SHA-256 文件。版本来自 `Directory.Build.props`，Inno Setup 从产物读取版本。发布时把 ZIP 和校验文件一起上传，标签版本必须一致。
- v0.0.3 是本次本地开发版本，尚未创建 GitHub Release。v0.0.2 没有此更新客户端，首次升级需手动解压新版。

GitHub 接口依据：[Releases REST API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release)。

## 文件与失败处理

下载写入 `.partial`，按 API 声明大小限制流量（最多 512 MiB），结束后校验大小与哈希；取消、超时或校验失败会移除临时文件。安装前再次校验 ZIP。压缩包限制 10,000 个条目、2 GiB 解压总量，拒绝越界路径、盘符、ADS、重复文件及符号链接。目标路径上的 junction/symlink 同样拒绝。

安装前为每个被覆盖文件保留备份；任一步失败，逆序恢复所有已修改文件并移除新增文件。完整回滚后尝试重新启动旧版。不会删除包外的用户文件。任务、备份与错误详情保留在 `%AppData%/Focus/updates/<任务ID>/`。该机制是异常失败回滚，不承诺断电时跨文件事务原子性；备份暂不自动清理。

安装目录没有写权限时会在关闭界面前失败，可通过设置中的“以管理员身份重启”后再试。UAC 拒绝、杀毒软件持续占用和系统断电不在本地测试覆盖范围内。安装程序的卸载登记版本不会由 ZIP 更新改写。

## 验证

```powershell
& 'C:/Program Files/dotnet/dotnet.exe' test tests/Focus.Tests/Focus.Tests.csproj -c Release
& 'C:/Program Files/dotnet/dotnet.exe' run --project docs/tools/UiCheck/UiCheck.csproj -c Release -- artifacts/ui-preview
& 'C:/Program Files/dotnet/dotnet.exe' run --project docs/tools/UpdateCheck/UpdateCheck.csproj -c Release -- src/Focus.Updater/bin/Release/net10.0-windows artifacts/update-check
```

单元测试覆盖版本/来源过滤、HTTP 错误、校验文件回退、取消、截断/超限/哈希错误、危险 ZIP、完整替换和故障回滚。端到端工具在隔离目录运行真实更新程序与测试用主进程，验证准备握手、退出等待、备份、数据保留、替换和重启，不更改用户安装。

GitHub 连接器已确认当前远端为 v0.0.2，已有 ZIP 命名与 SHA-256 digest 符合协议。本机 PowerShell 直连 GitHub 出现 TLS 身份验证错误，NuGet 漏洞审计源也不可达；因此网络流测试使用受控 HTTP 响应，未宣称从公网下载和安装新版成功。没有禁用 TLS 校验。
