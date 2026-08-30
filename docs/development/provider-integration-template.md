# 新 Provider 接入标准模板

本文用于后续向 Balance Island Windows 增加 AI Provider。目标是让 Provider 的元数据、环境变量发现、Key 安全、API 行为、图标/UI 与测试保持一致，避免只在某一个入口“硬编码支持”。

## 1. 先确定能力类型

新增 Provider 前先明确它属于哪一种能力：

- `DirectBalance`：官方接口可直接读取余额。
- `UsageOrLimit`：可读取消费、用量或额度上限，但不一定存在直接余额。
- `KeyCheckOnly`：官方只允许验证 Key；余额需要手动填写。

不要通过非公开网页接口模拟“官方余额查询”。如果 Provider 没有稳定公开接口，应选择 `KeyCheckOnly` 并在限制说明中明确写出。

## 2. ProviderCatalog 是元数据单一入口

在 `ProviderCatalog.cs` 增加一条 `ProviderDefinition`，必须一次填完整：

```csharp
new(
    Provider.Example,
    "Example AI",
    ["example", "exampleai"],
    "ProviderIcon.Example",
    "USD",
    BalanceCapability.DirectBalance,
    5,
    ["EXAMPLE_API_KEY"],
    ["EXAMPLE"],
    ["ex-"],
    "支持官方余额查询。")
```

字段检查表：

| 字段 | 要求 |
| --- | --- |
| `Provider` | `Models.cs` 中唯一枚举值 |
| `DisplayName` | 用户看到的正式名称 |
| `Aliases` | 搜索可用的常见别名，不放模糊通用词 |
| `IconResourceKey` | `ProviderIcons.xaml` 中真实存在的资源键 |
| `DefaultCurrency` | API 没返回币种时的安全默认值 |
| `Capability` | 只声明官方实际支持的能力 |
| `RecommendedRefreshMinutes` | 避免过于频繁地撞限流 |
| `EnvironmentVariableNames` | 官方/常用标准变量名 |
| `EnvironmentNameKeywords` | 可用于变量名匹配的唯一关键词 |
| `UniqueKeyPrefixes` | 只有真正能唯一归属 Provider 的前缀才能填写 |
| `Limitations` | 中文说明余额、权限、Key 类型等限制 |

通用前缀（例如只有 `sk-`）不得作为唯一归属证据；这类环境变量必须让用户在导入界面手动确认 Provider。

## 3. ProviderClient 只实现官方能力

在 `ProviderClient.FetchAsync` 增加明确分支，并优先复用已有验证辅助方法。

### 直接余额示例

```csharp
Provider.Example => FetchExampleAsync(credential, token),
```

`FetchExampleAsync` 应：

1. 仅调用官方 HTTPS API。
2. 使用 `CancellationToken`，不要绕开现有 `HttpClient` 超时和错误处理。
3. 缺少必要响应字段时抛 `ProviderApiException`，不要把错误响应伪装成 `0` 余额。
4. 将余额、币种、今日用量（若官方有）转换为 `BalanceSnapshot`。
5. 不在异常、日志或 UI 文本中拼接完整 API Key。

### 仅验证 Key 示例

如果官方没有余额接口，优先复用：

```csharp
Provider.Example => VerifyBearerAsync(
    credential,
    "https://api.example.com/v1/models",
    token),
```

如果鉴权头不是 Bearer，再增加一个小型专用验证方法。不要为了显示“余额”去抓网页或猜测套餐数据。

## 4. Key 清洗与环境导入

新增 Provider 时同步检查：

- `ApiKeySanitizer` 是否需要处理 Provider 特有的复制格式、引号或前缀。
- `EnvironmentCredentialDiscovery` 是否能通过 Catalog 元数据发现变量。
- 唯一 Key 前缀是否真的不会与其他 Provider 冲突。
- 轮换环境变量后，刷新必须重新读取并清洗新值。
- UI、状态文件、错误文本、Toast 均只能显示安全后缀/不可逆占位符。

任何新 Key 类型都必须先回答“能否唯一识别 Provider”。不能唯一识别时宁可让用户选择，也不要自动猜。

## 5. 图标与 UI

在 `ProviderIcons.xaml` 添加与现有图标风格一致的矢量资源：

- 资源键必须与 Catalog 的 `IconResourceKey` 完全一致。
- 尽量使用官方品牌几何，不引入外部图片文件依赖。
- 在水平浮岛和 Win10 垂直布局都检查尺寸与裁剪。
- 特殊黑/白底图标需要像现有 OpenAI/Moonshot 一样显式处理背景与 padding。

通常不应在 MainWindow 的 Provider 列表再写一份硬编码名称；Provider 搜索和支持列表应从 `ProviderCatalog` 自动获得新条目。

## 6. 最低自动化测试集

每个新 Provider 至少补以下覆盖：

### ProviderCatalogTests

- `ProviderCatalog.Get` 能返回定义。
- 名称、别名、变量名、关键词、唯一前缀可以按预期搜索。
- 能力、币种和推荐刷新周期符合设计。

### EnvironmentCredentialDiscoveryTests / EnvironmentImportTests

- 标准环境变量能被识别。
- 唯一前缀能正确分类；模糊前缀不会误分类。
- 同一来源重复扫描不会生成重复账户。
- Key 更新后安全后缀同步更新。

### ProviderClient 行为

能抽成纯解析逻辑时优先单元测试；涉及真实外部 API 的测试不要放入默认 CI。至少覆盖：

- 正常响应。
- 缺字段/错误响应。
- 币种与数值解析。
- API 错误内容不会泄露 Key。

### UI/显示

- `ProviderIcon.<Name>` 资源存在。
- 余额型 Provider 聚合时数值正确。
- `KeyCheckOnly` Provider 不伪造余额。
- 垂直浮岛能显示图标、账户标签、余额/验证状态和今日使用（如有）。

## 7. README 与 CHANGELOG

新增 Provider 的同一 PR 应同步：

- README 的 Provider 行为表。
- 当前支持 Provider 列表。
- 限制说明（例如需要 Admin Key、仅验证、无官方余额 API）。
- CHANGELOG 的 Unreleased 项。

如果用户需要额外环境变量名、特殊 API Key 类型或权限范围，教程中也要明确写出。

## 8. 安全复审

合并前逐项确认：

- [ ] 没有完整 API Key 写入 JSON、日志、异常、Toast 或 PR 测试快照。
- [ ] API endpoint 为 Provider 官方域名及公开接口。
- [ ] 没有把鉴权失败转换成余额 0。
- [ ] 没有把不同币种直接相加。
- [ ] 429/超时继续走现有刷新退避机制。
- [ ] 环境导入仍是用户明确选择后才创建账户。
- [ ] `KeyCheckOnly` 不宣称余额查询能力。

## 9. 推荐实施顺序

1. `Models.cs`：新增 `Provider` 枚举值（若需要）。
2. `ProviderCatalog.cs`：一次性登记完整元数据。
3. `ProviderIcons.xaml`：增加图标资源。
4. `ProviderClient.cs`：实现官方余额/用量或 Key 验证。
5. 必要时扩展 `ApiKeySanitizer`。
6. 补 Catalog、环境导入、解析/状态测试。
7. 更新 README / CHANGELOG。
8. `dotnet test` → `dotnet build` → `dotnet publish`。
9. 在 Win11 24H2 和 Win10 22H2 至少各做一次新增账户、刷新、浮岛显示和通知/错误文本复验。

每次 Provider 接入应单独分支/PR；不要和无关 UI 重构、任务栏定位或发布操作捆绑，以便快速回滚。
