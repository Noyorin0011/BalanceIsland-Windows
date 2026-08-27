# Changelog

## Unreleased

## 0.1.0 MVP

- 以 Android `v0.9.2` 的 Provider、刷新和显示行为为基线建立 Windows 工程。
- 新增托盘常驻和任务栏内余额文字条，每 5 秒轮换、单击立即切换。
- 新增九个 Provider 的余额查询或官方 Key 验证。
- 新增 Windows Credential Manager API Key 存储和本地 JSON 状态。
- 新增每账户刷新周期、30 秒手动防抖、429 `Retry-After` 与最长 24 小时退避。
- 新增手动余额、分级状态、余额通知基础设施和本地今日用量估算。
- 新增 Windows GitHub Actions 构建与可下载的 `win-x64` artifact。
