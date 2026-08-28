# Balance Island for Windows

Windows 任务栏 AI API 余额与用量监控工具。当前仓库是以 Android
[Balance Island v0.9.2](https://github.com/Noyorin0011/BalanceIsland/releases/tag/v0.9.2)
为行为基线建立的 Windows 版本；当前应用版本为 `0.2.1`。

## 当前功能

- Windows 10 1809+ / Windows 11，C#、.NET 8、WPF 与 Win32。
- 托盘常驻；关闭主窗口后继续运行，托盘菜单可打开、刷新、切换浮岛或退出。
- Windows 11 使用透明悬浮模式；Windows 10 保留“悬浮窗 / 任务栏组件（兼容）”模式。
- 浮岛支持 Widgets 后、任务栏居中、托盘前三种位置预设，以及紧凑、标准、大号和自定义尺寸。
- 开启编辑后可直接拖动和八方向缩放；关闭后固定并鼠标穿透，避免挡住任务栏按钮。
- 每 5 秒轮换已勾选显示的账户；全屏切入/切出由 Windows 事件立即驱动，并在 80ms 后复核。
- 浮岛为透明无背景的双行白字布局，显示与 Android 版同源的九家 Provider 矢量图标。
- 设置窗口、标签页、控件、表格和原生标题栏跟随 Windows 深浅色模式，运行中切换会立即更新。
- 同一 Provider 多账户；备注留空时显示 API Key 后四位。
- API Key 清洗与 Windows Credential Manager 存储；本地 JSON 不保存完整 Key。
- 每账户 `1–1440` 分钟刷新，`0` 使用 v0.9.2 的 Provider 建议周期。
- 手动刷新 30 秒防抖；HTTP 429 遵循 `Retry-After` 并指数退避，最长 24 小时。
- 手动余额、警告线、固定下降步长、异常变动检测以及本地托盘通知。
- 可从受支持的进程、用户和系统环境变量动态发现 Provider 凭据。
- DeepSeek、OpenAI、OpenRouter、SiliconFlow、Moonshot、MiMo、Anthropic、Gemini 与 xAI。
- DeepSeek、Moonshot、SiliconFlow 的本地“今日已用”估算会跨进程保存并修正检测到的充值。

### Provider 行为

| Provider | 当前行为 |
| --- | --- |
| DeepSeek | 官方余额、充值与赠送余额 |
| OpenAI | `sk-admin-` 查询组织本月成本/限制；其他 Key 仅验证 |
| OpenRouter | Management Key 查询总额度、累计用量与可用时的当日用量 |
| SiliconFlow | 官方 `/user/info` 账户总余额 |
| Moonshot | 中国站 CNY 与国际站 USD 余额回退 |
| MiMo | 普通 Key 验证；拒绝 Token Plan `tp-`；余额手动填写 |
| Anthropic / Gemini / xAI | 官方模型端点验证；余额手动填写 |

## 与 Android v0.9.2 的边界

当前 Windows 版不宣称已经完成逐项 UI 等价。以下内容留给后续版本：

- ChatGPT/Codex 非公开套餐接口、隔离登录会话、5 分钟实验页自动读取及重置周期通知。
- 自定义轮播分组、固定 Provider、套餐与 API 账户混合轮播。
- 完整异常变动设置 UI、自动隐藏、五套语言及平滑长文字滚动。
- 多显示器独立浮岛、任务栏自动隐藏与 Explorer 重启恢复的完整兼容测试。
- MSIX、签名和正式 Release 工作流。

非公开 ChatGPT/Codex 套餐接口不会在后台静默迁移；后续实现必须保留 v0.9.2
“明确风险确认、会话视同密码、只存筛选后额度字段、仅实验页面自动联网”的安全边界。

## 构建

安装 Visual Studio 2022 的“.NET 桌面开发”工作负载或 .NET 8 SDK：

```powershell
dotnet restore BalanceIsland-Windows.sln
dotnet build BalanceIsland-Windows.sln -c Release
dotnet run --project src/BalanceIsland.Windows/BalanceIsland.Windows.csproj
```

GitHub Actions 会在 Windows runner 上构建并上传 `win-x64` artifact。

## 数据与安全

- API Key 位于当前 Windows 用户的 Credential Manager，目标名为 `BalanceIsland/<account-id>`。
- 账户、Key 后四位、余额快照与刷新状态位于
  `%LOCALAPPDATA%\BalanceIsland\state.json`。
- 日志和错误信息不得输出完整 API Key。
- 应用只请求各 Provider 的官方 HTTPS API；不内置代理或证书绕过。

## 许可证

[GNU General Public License v3.0](LICENSE)
