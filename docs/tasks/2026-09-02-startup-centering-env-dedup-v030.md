# Balance Island Windows v0.3.0 收口任务书

日期：2026-09-02  
仓库：`Noyorin0011/BalanceIsland-Windows`  
目标版本：`v0.3.0`（不得升级版本号）  
开发基线：`main@284f26042a667010c64d8b0fa9e68ddb4719f8c1`  
前置合并：PR #20，Windows 11 四种任务栏横向布局与 Z-order 修复  
基线验证：Windows Build Run #107 成功

## 1. 任务目标

在不破坏 PR #20 已通过实机验收的横向定位、Windows 10 兼容路径和任务栏层级恢复逻辑的前提下，依次完成：

1. 修正 Windows 11 横向任务栏中浮岛略微偏上的问题，使浮岛在任务栏内垂直居中。
2. 增加静默启动，同时支持应用内持久化开关和命令行参数。
3. 优化启动环境 API 扫描：只有发现真正新增的环境凭据时才提示；静默启动时先发送通知，等用户打开主界面后再显示导入窗口。

实施顺序固定为“垂直居中 → 静默启动 → 新 Key 过滤与通知”，每项独立完成 RED→GREEN 后再进入下一项。

## 2. 范围与限制

### 2.1 本轮包含

- Windows 11 底部横向任务栏的浮岛垂直居中。
- 应用状态中新增静默启动设置。
- 主界面中新增静默启动复选框及说明。
- 命令行参数 `--silent`。
- 自动环境扫描的新凭据判定与去重。
- 静默启动发现新 Key 时的 Windows 通知及托盘回退。
- 用户稍后打开主界面时显示环境导入窗口。
- 单元测试、Release 构建、使用说明和验收记录。

### 2.2 本轮不包含

- Codex 使用量、WebView2 或任何 v0.4.0 功能。
- 修改 PR #20 已确认的四种横向位置规则。
- 修改 Windows 10 的旧悬浮/嵌入任务栏路径。
- 单实例进程、开机自启动注册、计划任务或安装器改造。
- 自动导入环境 Key；任何新增账户仍必须由用户确认。
- 持久化完整 API Key、可逆密文、Key 哈希或可用于关联 Key 的新标识。
- 点击通知后直接唤起窗口的协议激活；本轮只要求通知提示和用户主动打开主界面后的弹窗。

## 3. 功能一：任务栏内垂直居中

### 3.1 当前根因

Windows 11 自动定位路径把 `TaskbarFloatingPlacement.Result.Top` 直接设置为 `taskbar.Top`。标准浮岛高约 38px、任务栏高约 48px，因此浮岛顶边贴住任务栏顶边，视觉上向上偏约 5px。Z-order 恢复逻辑与该偏移无关，不应修改。

### 3.2 目标行为

当浮岛可以安全放入横向任务栏带内时：

```text
top = taskbarTop + max(0, (taskbarHeight - islandHeight) / 2)
```

计算使用已经完成 DPI 换算的物理像素值，并采用整数像素稳定舍入。示例：任务栏 `[1032, 1080)`、浮岛高度 `38px` 时，结果为 `1037px`。

当目标槽位空间不足、`FitsInTaskbarBand == false` 时，仍使用现有任务栏上方回退：

```text
top = taskbarTop - islandHeight - margin
```

不得把回退位置重新居中到任务栏内。

### 3.3 必须保持不变

- 居中开始按钮 + Widgets 开启：浮岛位于 Widgets 右侧。
- 居中开始按钮 + Widgets 关闭：浮岛位于任务栏最左安全槽位。
- 左对齐开始按钮 + Widgets 开启：浮岛右边框距 Widgets 左侧 6px。
- 左对齐开始按钮 + Widgets 关闭：浮岛右边框距通知区域左侧 6px。
- 点击任务栏或桌面后的 `HWND_TOPMOST` 恢复。
- Windows 10 `PlaceLegacyHorizontal` 的原有垂直居中结果。
- 竖向任务栏和自定义位置的现有行为。

### 3.4 测试要求

先新增或修改测试，使旧实现明确失败并输出 `Expected 1037 / Actual 1032`。至少覆盖：

- 48px 任务栏 + 38px 浮岛居中为 5px 偏移。
- 浮岛高度等于任务栏高度时偏移为 0。
- 浮岛高于任务栏时不得产生负偏移。
- `FitsInTaskbarBand == false` 时仍选择任务栏上方坐标。
- 四种横向布局的 Left 值不发生变化。
- Windows 10 旧路径仍使用原有居中公式。

## 4. 功能二：静默启动

### 4.1 用户行为

应用内增加“静默启动（不打开主窗口）”开关。开启后，下次启动：

- 不显示主配置窗口。
- 托盘图标正常创建。
- 已启用的浮岛正常显示。
- 后台刷新、余额告警、健康检查与主题监听正常启动。
- 用户双击托盘图标或选择“打开 Balance Island”后，主窗口正常显示和激活。

命令行支持：

```powershell
BalanceIsland.exe --silent
```

`--silent` 只强制本次启动静默，不修改应用内设置。参数比较忽略大小写；未知参数不导致启动失败。

### 4.2 设置模型

在 `AppState` 中加入 `SilentStartupEnabled`，默认 `false`。旧版 `state.json` 缺少字段时自动采用 `false`。

在 `BalanceCoordinator` 中提供唯一持久化入口 `SetSilentStartup(bool enabled)`，沿用 `SaveAndNotify()`。

启动决策提取为不依赖 WPF 的纯函数，输入为持久化设置和 `StartupEventArgs.Args`，逻辑为：

```text
silent = persistedSilent || args contains "--silent"
```

### 4.3 UI 要求

- 在现有设置区域增加复选框，不新建复杂页面。
- 文案明确：静默启动只隐藏主窗口，不关闭托盘、浮岛和后台刷新。
- 勾选变化立即持久化。
- `RefreshRows()` 和首次加载正确回填，并使用 `_loadingControls` 防止加载误写。

### 4.4 测试要求

先写失败测试，至少覆盖：

- 默认设置且无参数：显示主窗口。
- 持久化设置开启：静默。
- 设置关闭但带 `--silent`：静默。
- `--SILENT`：静默。
- 未知参数：不静默且不报错。
- 协调器设置后重新加载 `state.json` 仍保留值。
- 静默启动不阻止浮岛、托盘和后台协调器初始化；WPF 部分由纯函数测试和代码审查共同覆盖。

## 5. 功能三：只提示新增环境 API

### 5.1 “新增”的正式定义

自动扫描候选只有满足以下条件时才算新增：

1. 不是已导入的同 Provider + 同环境变量名账户；
2. 不是同 Provider 下已经存在的相同 API Key；
3. 对无法自动分类 Provider 的候选，如果其环境变量名或 Key 已对应任何现有账户，也不算新增；
4. 同一 Provider、同一变量发生 Key 轮换不算新增，因为现有环境账户会动态读取新值；
5. 同一 Key 同时能从已有账户与另一个变量读取时不算新增；若原变量已移除，因旧账户已失效，新变量可视为需要重新关联的新来源；
6. 只有真正未被现有账户代表的凭据才进入自动提示列表。

Key 比较必须：

- 先使用 `ApiKeySanitizer.Clean` 规范化；
- 仅在当前进程内存中比较；
- 不写入 JSON、日志、异常、通知、测试快照或 PR 文本；
- 不新增持久化 Key 哈希。

### 5.2 自动扫描与手动扫描分离

自动扫描：

- 扫描后先过滤为新增候选。
- 新增候选数为 0：不构造、不显示导入窗口，不显示“无新增”对话框。
- 新增候选数大于 0：只把新增候选传给导入窗口。
- 用户仍需手动勾选并确认；不得自动添加账户。

手动点击“扫描环境”：

- 保持显示完整扫描结果。
- 已导入项仍以禁选状态展示。
- 允许用户检查支持情况并给未分类候选选择 Provider。

### 5.3 普通启动

- 无新增：主界面正常显示，不弹窗。
- 有新增：主窗口加载后弹出环境导入窗口。
- 同一次窗口加载只弹一次，避免状态刷新重复触发。

### 5.4 静默启动

- 无新增：完全静默。
- 有新增：发送普通 Windows 通知，只包含新增数量与“打开 Balance Island 处理”，不包含变量值、Key、Key 后缀或敏感标识。
- Windows 原生通知不可用时，回退托盘气泡。
- 不显示主窗口或导入窗口。
- 用户之后从托盘打开主界面时重新扫描；若新增仍存在，则再弹出导入窗口。
- 如果用户打开前已经移除环境变量，重新扫描没有新增，则不弹窗。

### 5.5 通知实现

扩展现有通知服务以发送普通应用通知：

- 标题建议：“发现新的环境 API”。
- 正文建议：“发现 N 个尚未导入的环境凭据。打开 Balance Island 进行确认。”
- 使用普通 Toast，不使用余额临界的 urgent 场景。
- 原生发送失败时由 `App` 执行一次托盘回退。
- 不改变余额告警通知的高优先级和紧急场景行为。

### 5.6 测试要求

先写失败测试，至少覆盖：

- 空扫描结果返回空新增集合。
- 同 Provider + 同变量名不新增。
- 同 Provider + 不同变量名 + 相同规范化 Key 不新增。
- 同 Provider + 不同变量名 + 不同 Key 为新增。
- 凭据管理器中同 Provider + 相同 Key 不新增。
- 未分类候选与已有环境变量名相同不新增。
- 未分类候选与已有 Key 相同不新增。
- 同变量 Key 轮换不提示，但现有账户刷新仍读取轮换后的值。
- 自动扫描无新增时“不弹窗、不通知”。
- 普通启动有新增时“弹窗”。
- 静默启动有新增时“通知但不弹窗”。
- 普通应用通知 XML 正确转义且不含 urgent 场景。
- 通知正文不出现候选变量值或完整 Key。

## 6. 建议代码边界

优先沿用现有文件和模式，避免无关重构。预计涉及：

- `src/BalanceIsland.Windows/TaskbarFloatingPlacement.cs`
- `src/BalanceIsland.Windows/TaskbarEmbedder.cs`
- `src/BalanceIsland.Windows/TaskbarIslandWindow.xaml.cs`
- `src/BalanceIsland.Windows/App.xaml.cs`
- `src/BalanceIsland.Windows/MainWindow.xaml`
- `src/BalanceIsland.Windows/MainWindow.xaml.cs`
- `src/BalanceIsland.Windows/Models.cs`
- `src/BalanceIsland.Windows/BalanceCoordinator.cs`
- `src/BalanceIsland.Windows/WindowsNotificationService.cs`
- 可新增纯逻辑文件：`StartupBehavior.cs`、`EnvironmentImportPlanner.cs`
- 对应测试文件及必要的新测试文件。

推荐边界：

- `StartupBehavior`：命令行与持久化设置的启动决策。
- `EnvironmentImportPlanner`：候选去重和自动提示决策，不显示 UI。
- `App`：启动顺序、静默通知和托盘回退。
- `MainWindow`：普通启动/打开主界面后的导入窗口，以及手动扫描。
- `BalanceCoordinator`：持久化设置、读取现有凭据并调用纯过滤逻辑。

## 7. TDD 与提交顺序

禁止先写生产实现再补测试。每轮保存 RED 与 GREEN 证据。

建议提交顺序：

1. `test: require Win11 taskbar vertical centering`
2. `fix: center Win11 island within taskbar height`
3. `test: define silent startup behavior`
4. `feat: add persisted and command-line silent startup`
5. `test: define new environment credential prompting`
6. `feat: notify only for new environment credentials`
7. `docs: document silent startup and environment scan behavior`

若本地没有 .NET SDK，每个 RED/GREEN 提交通过远端 Windows CI 验证：

- RED 必须因预期新接口缺失或行为不符而失败，不能是拼写、Restore 或测试环境错误。
- GREEN 必须让新增测试和全部既有测试通过。

## 8. 完整验证

最终分支必须执行：

```powershell
dotnet test BalanceIsland-Windows.sln --configuration Release
dotnet build BalanceIsland-Windows.sln --configuration Release --no-restore
dotnet publish src/BalanceIsland.Windows/BalanceIsland.Windows.csproj `
  --configuration Release --runtime win-x64 --self-contained false
```

CI 应确认：

- 全部测试通过，0 failed。
- Release build 0 errors；新增 warning 必须处理。
- win-x64 Publish 成功。
- artifact 名称继续使用 v0.3.0。
- PR 只包含本任务相关源代码、测试和文档。

## 9. 实机验收矩阵

### 9.1 垂直居中

- Windows 11 24H2，开始按钮居中/左对齐各一次。
- Widgets 开启/关闭各一次。
- 四种状态横向位置与 PR #20 完全一致。
- 浮岛上下留白近似相等，无明显偏上。
- 重复点击任务栏和桌面，浮岛不被盖住、不漂移。
- 调整任务栏缩放或显示器 DPI 后仍居中。

### 9.2 静默启动

- 设置关闭、无参数：启动显示主窗口。
- 设置开启：启动不显示主窗口，托盘与浮岛存在。
- 设置关闭、带 `--silent`：本次不显示；下次无参数仍显示。
- 静默后通过托盘打开主窗口成功。
- 退出后不残留进程或托盘图标。

### 9.3 环境 API 扫描

- 无新 Key：普通启动不弹窗；静默启动不通知。
- 新增 Key：普通启动弹一次导入窗口。
- 新增 Key：静默启动只通知，不显示窗口。
- 静默后打开主界面：若 Key 仍存在，弹出导入窗口。
- 已导入同变量后重启：不再提示。
- 复制同 Key 到第二变量且原变量仍存在：不再提示；移除原变量后，新变量可提示以重新关联。
- 同变量 Key 轮换：不提示新增，刷新使用新值。
- 手动“扫描环境”始终能打开完整结果窗口。

## 10. 安全与回归检查

- 使用 `rg` 检查完整 API Key 未进入源码、测试输出、日志、文档和状态 JSON。
- 通知只显示数量，不显示变量名、Key 后缀或 Provider 明细。
- 不读取或输出 GitHub、签名或系统凭据秘密。
- 不直接推送 `main`。
- 新分支经 PR 合入；合并前要求 Windows Build 成功并完成实机验收。
- 本轮不创建版本标签或 GitHub Release，除非用户另行明确授权。

## 11. 完成定义

只有同时满足以下条件才可宣告完成：

- 三项功能均有先失败后通过的自动化测试证据。
- 完整测试、Release build、Publish 与 artifact 上传成功。
- 代码审查无 Critical/Important 问题。
- Windows 11 实机验证四种布局仍正确且垂直居中。
- 静默启动与环境新 Key 提示按本任务书通过。
- PR 基于 `main@284f2604`，版本仍为 `v0.3.0`，不包含 v0.4.0/Codex 工作。
