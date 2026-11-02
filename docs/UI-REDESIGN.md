# 瑞士国际主义工作区

Rust 重构已提交并推送为 `f29e385`。本阶段沿用 WPF，首先完成统一工作区、编辑器布局与页面查找能力。

## 设计与行为

- 纸白背景、黑色细线、红色操作重点，无衬线字号层级。矩形控件与横线分节替代圆角卡片。`Themes/Dark.xaml` 保留资源路径兼容引用，内容已是浅色主题。HUD 配置本身的颜色与圆角不随应用主题改变。
- 188 px 编号导航栏和 64 px 页面标题区。`Alt+1…5` 直接切换页面；避免重复导航导致编辑器重新加载。
- 概览放大主标题和实时读数；配置操作集中到“编辑 / 设为默认 / 更多”；会话空状态使用编号与左对齐标题；设置首屏提供完整更新入口。
- `Ctrl+K` 打开快捷操作面板，支持页面和操作搜索、上下键选择、Enter 执行、Esc 取消；没有结果时显示明确提示。
- 编辑器将文档选择/保存与画布工具分成两行；多选时单独显示对齐工具，不挤占常驻操作。
- 图层和属性面板可拖动调整宽度；“专注画布”及 `Ctrl+Shift+F` 收起两侧面板，退出时保留用户设置的宽度。
- 配置按名称/关联游戏搜索；会话按名称/游戏/日期搜索，刷新时保留仍然存在的选中项。
- 搜索框提供可见提示与辅助功能名称，空结果与空数据分别处理。
- 设置保存失败时保留错误提示，不显示“已保存”；禁用的快捷键不再被误报为注册冲突。
- 来自后台服务的主界面、概览和列表更新切回 UI Dispatcher。

## 验证方法

```powershell
dotnet build src/Focus.App/Focus.App.csproj -c Release
dotnet run --project docs/tools/UiCheck/UiCheck.csproj -c Release -- artifacts/ui-preview
dotnet test tests/Focus.Tests/Focus.Tests.csproj -c Release
```

`UiCheck` 使用真实 WPF 控件执行程序化交互检查，覆盖快捷面板搜索、空结果、方向键、Enter、Esc、操作分发和画布专注模式的面板宽度恢复。它不模拟系统级键盘输入，不启动硬件采集。

使用应用自带截图命令检查页面：

```powershell
$env:FOCUS_INSTANCE = "ui-preview"
$env:FOCUS_DATA_ROOT = Join-Path $PWD "artifacts/ui-preview/data"
.\src\Focus.App\bin\Release\net10.0-windows\Focus.exe --page editor --screenshot artifacts/ui-preview/editor.png --screenshot-delay 1600
```

`FOCUS_DATA_ROOT` 将预览数据隔离于日常配置。截图矩阵覆盖五个页面的 1080×720 窗口；新增文案提供中英文。产物保存在忽略跟踪的 `artifacts/ui-preview`。

## 后续阶段

- 编辑器组件库、属性分组、图层搜索与批量操作。
- 会话详情的曲线联动、基线对比与异常帧定位。
- 设置页分类导航，以及更完整的首次使用流程。
- 对实际鼠标拖动、多显示器 DPI 和屏幕阅读器进行人工验证。

本次已生成 v0.0.3 ZIP 与校验文件，尚未发布。自动更新的协议、验证与已知边界见 [AUTO-UPDATE.md](AUTO-UPDATE.md)。上述后续交互拓展不在本次范围内。
