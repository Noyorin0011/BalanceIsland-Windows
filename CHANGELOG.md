# Changelog

## Unreleased

## 0.1.2

- 修复 Windows 11 选择任务栏嵌入后浮岛完全消失的问题。
- 参考 TrafficMonitor：Explorer 拒绝跨进程嵌入时自动切换到通知区域/任务栏左侧的置顶伴随窗口，不再隐藏挂件或只显示错误。
- Windows 11 改用 popup-parented layered surface，让 WPF 浮岛保留独立 DWM 合成层；Windows 10 继续使用 child host。
- 嵌入后强制刷新 DWM，并检测不可见或 cloaked 状态；检测失败时立即恢复悬浮模式并显示原因。

## 0.1.1

- 设置窗口现在跟随 Windows 应用深浅色模式，并在系统主题切换后实时更新。
- 为按钮、文本框、密码框、Provider 下拉框、下拉菜单、表格表头、行、单元格、选中态和禁用态补齐深色资源。
- Windows 10/11 原生标题栏同步启用或关闭 DWM 深色模式；任务栏浮岛背景与文字也随应用主题更新。
- 新增可持久化的“悬浮窗 / 任务栏嵌入（实验）”显示模式切换。
- 嵌入模式在 Windows 11 居中任务栏使用左侧空位；左对齐任务栏和 Windows 10 使用通知区域左侧空位。
- Explorer 重启、任务栏重建、DPI 或对齐变化后自动重新挂载；没有安全空间时回退悬浮模式。

## 0.1.0 MVP

- 以 Android `v0.9.2` 的 Provider、刷新和显示行为为基线建立 Windows 工程。
- 新增托盘常驻和任务栏内余额文字条，每 5 秒轮换、单击立即切换。
- 新增九个 Provider 的余额查询或官方 Key 验证。
- 新增 Windows Credential Manager API Key 存储和本地 JSON 状态。
- 新增每账户刷新周期、30 秒手动防抖、429 `Retry-After` 与最长 24 小时退避。
- 新增手动余额、分级状态、余额通知基础设施和本地今日用量估算。
- 新增 Windows GitHub Actions 构建与可下载的 `win-x64` artifact。
