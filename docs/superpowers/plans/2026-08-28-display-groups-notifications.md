# Balance Island Windows v0.3.0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add selectable application themes, four-state island colors, rotation and same-Provider aggregate groups, confirmed environment credential import, a centralized searchable Provider catalog, and urgent Windows notifications with complete in-app and README guidance.

**Architecture:** Move deterministic decisions out of `MainWindow` and `BalanceCoordinator` into small testable services: state normalization, Provider catalog/matching, visual/alert classification, display grouping/aggregation, and toast payload creation. WPF remains responsible for rendering and user confirmation; `BalanceCoordinator` remains the persistence and refresh orchestrator. Native WinRT toast delivery is wrapped behind `INotificationService`, with the existing tray balloon retained only as a fallback.

**Tech Stack:** C# 12, .NET 8, WPF, Win32/WinRT notifications, System.Text.Json, xUnit, GitHub Actions on `windows-latest`.

**Spec:** `docs/superpowers/specs/2026-08-28-display-groups-notifications-design.md`

## Global Constraints

- Target Windows 10 1809+ and Windows 11 with `net8.0-windows10.0.17763.0`.
- Keep the existing ZIP deployment and framework-dependent .NET 8 publish; do not add the full Windows App SDK Runtime or MSIX.
- Keep `state.json` backward compatible and never persist, display, log, or notify with a complete API Key.
- Preserve current island position/size presets, edit mode, click-through behavior, fullscreen event handling, 80ms settle check, and 5-second fallback.
- Use `warningLine < balance <= warningLine * 1.15` for the warning band, with exact inclusive/exclusive boundaries.
- Aggregate groups accept one Provider only and never add different currencies.
- A scan never imports a credential until the user explicitly checks it and confirms.
- Windows and organization notification policy remains authoritative; fallback balloons must be reported as degraded delivery.
- Do not merge, tag, or publish without a separate explicit user request.

---

## File Structure

### New production files

- `src/BalanceIsland.Windows/AppStateNormalizer.cs` — safe defaults and cleanup for old/null state.
- `src/BalanceIsland.Windows/ProviderCatalog.cs` — single source of Provider names, aliases, capabilities, environment names, keywords, and unique Key prefixes.
- `src/BalanceIsland.Windows/BalanceStateEvaluator.cs` — pure visual-state and alert transition decisions.
- `src/BalanceIsland.Windows/IslandColorPalettes.cs` — built-in palettes and custom color validation.
- `src/BalanceIsland.Windows/IslandDisplayGroups.cs` — group validation, selection, and aggregation.
- `src/BalanceIsland.Windows/EnvironmentImportWindow.xaml(.cs)` — masked, opt-in environment credential selection.
- `src/BalanceIsland.Windows/DesktopNotificationIdentity.cs` — unpackaged desktop AppUserModelID and Start Menu shortcut registration.
- `src/BalanceIsland.Windows/WindowsNotificationService.cs` — urgent Toast delivery, status, test notification, and failure result.

### New test files

- `tests/BalanceIsland.Windows.Tests/BalanceIsland.Windows.Tests.csproj`
- `tests/BalanceIsland.Windows.Tests/AppStateNormalizerTests.cs`
- `tests/BalanceIsland.Windows.Tests/TestFactory.cs`
- `tests/BalanceIsland.Windows.Tests/ProviderCatalogTests.cs`
- `tests/BalanceIsland.Windows.Tests/EnvironmentCredentialDiscoveryTests.cs`
- `tests/BalanceIsland.Windows.Tests/BalanceStateEvaluatorTests.cs`
- `tests/BalanceIsland.Windows.Tests/IslandColorPalettesTests.cs`
- `tests/BalanceIsland.Windows.Tests/IslandDisplayGroupsTests.cs`
- `tests/BalanceIsland.Windows.Tests/ToastPayloadBuilderTests.cs`

### Existing files changed

- `Models.cs`, `AppDataStore.cs`, `BalanceCoordinator.cs`, `IslandAccountSelection.cs`
- `SystemThemeManager.cs`, `TaskbarIslandWindow.xaml(.cs)`
- `MainWindow.xaml(.cs)`, `App.xaml.cs`, `App.xaml`
- solution/project/workflow files, `README.md`, and `CHANGELOG.md`

---

### Task 1: Add the test project and backward-compatible state normalization

**Files:**
- Create: `tests/BalanceIsland.Windows.Tests/BalanceIsland.Windows.Tests.csproj`
- Create: `tests/BalanceIsland.Windows.Tests/AppStateNormalizerTests.cs`
- Create: `tests/BalanceIsland.Windows.Tests/TestFactory.cs`
- Create: `src/BalanceIsland.Windows/AppStateNormalizer.cs`
- Modify: `BalanceIsland-Windows.sln`
- Modify: `src/BalanceIsland.Windows/Models.cs`
- Modify: `src/BalanceIsland.Windows/AppDataStore.cs`

**Interfaces:**
- Produces `AppThemeMode`, `IslandColorTheme`, `IslandGroupMode`, `IslandDisplayGroup`, and new `AppState` fields.
- Produces `AppStateNormalizer.Normalize(AppState state): AppState`.
- Produces `AppDataLoadResult(AppState State, bool LoadedFromDisk, string? Error)` and `AppDataStore.LoadResult()`.

- [ ] **Step 1: Create the xUnit project and add it to the solution**

Use this project definition:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.17763.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="coverlet.collector" Version="6.0.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\BalanceIsland.Windows\BalanceIsland.Windows.csproj" />
  </ItemGroup>
</Project>
```

Run: `dotnet sln BalanceIsland-Windows.sln add tests/BalanceIsland.Windows.Tests/BalanceIsland.Windows.Tests.csproj`

- [ ] **Step 2: Write failing migration/default tests**

```csharp
[Fact]
public void Normalize_v021_state_uses_safe_v030_defaults()
{
    var options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    var state = JsonSerializer.Deserialize<AppState>("{\"accounts\":[]}", options)!;
    AppStateNormalizer.Normalize(state);

    Assert.Equal(AppThemeMode.System, state.ThemeMode);
    Assert.Equal(IslandColorTheme.Classic, state.IslandColorTheme);
    Assert.Empty(state.DisplayGroups);
    Assert.Null(state.ActiveDisplayGroupId);
    Assert.True(state.NotifyWarning15);
    Assert.True(state.NotifyCritical);
    Assert.True(state.NotifyAnomaly);
}

[Fact]
public void Normalize_removes_missing_accounts_and_invalid_active_group()
{
    var state = new AppState
    {
        DisplayGroups = [new() { Id = "g", Name = "  Team  ", AccountIds = ["missing"] }],
        ActiveDisplayGroupId = "missing-group"
    };
    AppStateNormalizer.Normalize(state);
    Assert.Equal("Team", state.DisplayGroups[0].Name);
    Assert.Empty(state.DisplayGroups[0].AccountIds);
    Assert.Null(state.ActiveDisplayGroupId);
}
```

- [ ] **Step 3: Run the tests and verify RED**

Run: `dotnet test tests/BalanceIsland.Windows.Tests/BalanceIsland.Windows.Tests.csproj --filter AppStateNormalizerTests`

Expected: compilation fails because the new enums, group model, fields, and normalizer do not exist.

- [ ] **Step 4: Add models and minimal normalization**

Add enum-backed JSON fields to `AppState` with defaults and this group model:

```csharp
public sealed class IslandDisplayGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新分组";
    public IslandGroupMode Mode { get; set; } = IslandGroupMode.Rotation;
    public Provider? AggregateProvider { get; set; }
    public List<string> AccountIds { get; set; } = [];
}
```

Implement normalization to replace null collections, trim/non-empty group names, deduplicate IDs, remove missing account IDs, clear invalid active group IDs, and validate colors through Task 3's future public validator without depending on it yet (use classic defaults at this stage).

Change `AppDataStore.Load()` into `LoadResult()`. On malformed JSON return a default normalized state with `LoadedFromDisk=false` and the exception message; do not write from `LoadResult()`. Keep atomic `.tmp` replacement in `Save()`.

Add an internal test helper used by later tasks:

```csharp
internal static class TestFactory
{
    public static BalanceCoordinator CreateCoordinator(AppState? initial = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "BalanceIsland.Tests", Guid.NewGuid().ToString("N"));
        var store = new AppDataStore(directory);
        if (initial is not null) store.Save(initial);
        return new BalanceCoordinator(store, new WindowsCredentialStore(), new ProviderClient());
    }
}
```

Add `AppDataStore(string directory)` for this helper while keeping the parameterless production constructor. Update the coordinator constructor to consume `LoadResult()` and not perform its current unconditional startup save when `LoadedFromDisk` is false because parsing failed; the next explicit setting change or successful refresh may persist the recovered state.

- [ ] **Step 5: Run state tests and the full solution**

Run: `dotnet test tests/BalanceIsland.Windows.Tests/BalanceIsland.Windows.Tests.csproj --filter AppStateNormalizerTests`

Expected: PASS.

Run: `dotnet build BalanceIsland-Windows.sln -c Release`

Expected: PASS with no warnings introduced by the new models.

- [ ] **Step 6: Commit**

```bash
git add BalanceIsland-Windows.sln src/BalanceIsland.Windows/Models.cs src/BalanceIsland.Windows/AppDataStore.cs src/BalanceIsland.Windows/AppStateNormalizer.cs tests/BalanceIsland.Windows.Tests
git commit -m "test: add v0.3 state migration coverage"
```

---

### Task 2: Centralize Provider metadata and deterministic environment matching

**Files:**
- Create: `src/BalanceIsland.Windows/ProviderCatalog.cs`
- Create: `tests/BalanceIsland.Windows.Tests/ProviderCatalogTests.cs`
- Create: `tests/BalanceIsland.Windows.Tests/EnvironmentCredentialDiscoveryTests.cs`
- Modify: `src/BalanceIsland.Windows/Models.cs`
- Modify: `src/BalanceIsland.Windows/EnvironmentCredentialDiscovery.cs`

**Interfaces:**
- Produces `ProviderDefinition` with `Provider`, `DisplayName`, `Aliases`, `IconResourceKey`, `DefaultCurrency`, `Capability`, `RecommendedRefreshMinutes`, `EnvironmentVariableNames`, `EnvironmentNameKeywords`, `UniqueKeyPrefixes`, and `Limitations`.
- Produces `ProviderCatalog.All`, `ProviderCatalog.Get(Provider)`, and `ProviderCatalog.Search(string)`.
- Produces `EnvironmentVariableEntry`, `EnvironmentCredentialCandidate`, `EnvironmentCredentialDiscovery.Scan(IEnumerable<EnvironmentVariableEntry>)`, and system `Scan()`.

- [ ] **Step 1: Write failing catalog completeness and search tests**

```csharp
[Fact]
public void Every_provider_has_exactly_one_definition()
{
    Assert.Equal(Enum.GetValues<Provider>().Order(), ProviderCatalog.All.Select(x => x.Provider).Order());
    Assert.Equal(ProviderCatalog.All.Count, ProviderCatalog.All.Select(x => x.Provider).Distinct().Count());
}

[Theory]
[InlineData("kimi", Provider.Moonshot)]
[InlineData("grok", Provider.XAI)]
[InlineData("google", Provider.Gemini)]
public void Search_matches_names_and_aliases(string query, Provider expected)
{
    Assert.Contains(ProviderCatalog.Search(query), x => x.Provider == expected);
}
```

- [ ] **Step 2: Write failing environment matcher tests**

```csharp
[Theory]
[InlineData("WHATEVER", "sk-or-v1-abc1234", Provider.OpenRouter)]
[InlineData("WHATEVER", "sk-ant-api03-abc1234", Provider.Anthropic)]
[InlineData("WHATEVER", "AIzaSyExample1234", Provider.Gemini)]
[InlineData("WHATEVER", "xai-example1234", Provider.XAI)]
[InlineData("MY_KIMI_SECRET", "sk-example1234", Provider.Moonshot)]
public void Scan_matches_unique_prefix_or_name_keyword(string name, string value, Provider provider)
{
    var candidate = Assert.Single(EnvironmentCredentialDiscovery.Scan(
        [new EnvironmentVariableEntry(name, value, "User")]));
    Assert.Equal(provider, candidate.Provider);
    Assert.Equal("••••1234", candidate.MaskedKey);
}

[Fact]
public void Generic_sk_key_without_provider_name_is_not_guessed()
{
    Assert.Empty(EnvironmentCredentialDiscovery.Scan(
        [new EnvironmentVariableEntry("MY_API_KEY", "sk-example1234", "User")]));
}
```

- [ ] **Step 3: Run both test classes and verify RED**

Run: `dotnet test tests/BalanceIsland.Windows.Tests/BalanceIsland.Windows.Tests.csproj --filter "ProviderCatalogTests|EnvironmentCredentialDiscoveryTests"`

Expected: compilation fails because the catalog and pure scan overload do not exist.

- [ ] **Step 4: Implement the single catalog and delegate old extension methods**

Populate all nine existing Providers. Change `ProviderInfo.DisplayName`, `DefaultCurrency`, `Capability`, and `RecommendedRefreshMinutes` to delegate to `ProviderCatalog.Get(value)` so old callers remain source-compatible.

Implement case-insensitive search across display name, enum name, and aliases. Use unique prefixes only for OpenRouter (`sk-or-`), Anthropic (`sk-ant-`), OpenAI (`sk-admin-`, `sk-proj-`, `sk-svcacct-`), Gemini (`AIza`), and xAI (`xai-`). Do not register plain `sk-` as unique.

- [ ] **Step 5: Implement pure scanning and system enumeration**

The pure overload sanitizes values, matches standard name then keyword then unique prefix, returns only masked display metadata plus the private runtime value, and deduplicates identical variable name/scope pairs. The parameterless overload enumerates Process, User, and Machine scopes with Process precedence, catching access errors per scope.

- [ ] **Step 6: Run tests and build**

Run: `dotnet test tests/BalanceIsland.Windows.Tests/BalanceIsland.Windows.Tests.csproj --filter "ProviderCatalogTests|EnvironmentCredentialDiscoveryTests"`

Expected: PASS.

Run: `dotnet build BalanceIsland-Windows.sln -c Release`

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/BalanceIsland.Windows/Models.cs src/BalanceIsland.Windows/ProviderCatalog.cs src/BalanceIsland.Windows/EnvironmentCredentialDiscovery.cs tests/BalanceIsland.Windows.Tests
git commit -m "feat: centralize provider discovery metadata"
```

---

### Task 3: Implement exact visual states, palettes, and alert transitions

**Files:**
- Create: `src/BalanceIsland.Windows/BalanceStateEvaluator.cs`
- Create: `src/BalanceIsland.Windows/IslandColorPalettes.cs`
- Create: `tests/BalanceIsland.Windows.Tests/BalanceStateEvaluatorTests.cs`
- Create: `tests/BalanceIsland.Windows.Tests/IslandColorPalettesTests.cs`
- Modify: `src/BalanceIsland.Windows/Models.cs`
- Modify: `src/BalanceIsland.Windows/AppStateNormalizer.cs`

**Interfaces:**
- Produces `BalanceVisualState { Normal, Warning15, Anomaly, Critical }`.
- Produces `BalanceAlertKind { Warning15, Critical, Anomaly }`.
- Produces `BalanceEvaluation(BalanceVisualState VisualState, IReadOnlyList<BalanceAlertKind> EnteredAlerts, BalanceAlertState NextState)`.
- Produces `BalanceStateEvaluator.Evaluate(Account, BalanceSnapshot, BalanceAlertState, DateTimeOffset)`.
- Produces `IslandColorPalettes.Resolve(AppState)` and `TryNormalizeColor(string, out string)`.

- [ ] **Step 1: Write failing boundary and priority tests**

```csharp
[Theory]
[InlineData(23.01, BalanceVisualState.Normal)]
[InlineData(23.00, BalanceVisualState.Warning15)]
[InlineData(20.01, BalanceVisualState.Warning15)]
[InlineData(20.00, BalanceVisualState.Critical)]
public void Warning_band_uses_exact_fifteen_percent_boundary(double balance, BalanceVisualState expected)
{
    var result = Evaluate(balance, warningLine: 20, anomaly: false);
    Assert.Equal(expected, result.VisualState);
}

[Fact]
public void Critical_has_priority_over_anomaly_and_warning()
{
    var result = Evaluate(balance: 19, warningLine: 20, anomaly: true);
    Assert.Equal(BalanceVisualState.Critical, result.VisualState);
}

private static BalanceEvaluation Evaluate(
    double balance,
    double warningLine,
    bool anomaly,
    BalanceAlertState? previous = null)
{
    var account = new Account
    {
        WarningLine = warningLine,
        AlertEnabled = true,
        AnomalyEnabled = anomaly,
        AnomalyThreshold = 1,
        AnomalyPercentThreshold = 1,
        AnomalyMode = AnomalyMode.Both,
        AnomalyCooldownMinutes = 60
    };
    previous ??= new BalanceAlertState();
    if (anomaly && previous.LastSeenAmount is null) previous.LastSeenAmount = balance + 10;
    var snapshot = new BalanceSnapshot
    {
        BalanceAmount = balance,
        Status = SnapshotStatus.Ok,
        UpdatedAt = DateTimeOffset.Parse("2026-08-28T00:00:00Z")
    };
    return BalanceStateEvaluator.Evaluate(account, snapshot, previous, snapshot.UpdatedAt);
}
```

- [ ] **Step 2: Write failing transition/deduplication tests**

```csharp
[Fact]
public void Warning_notifies_on_entry_not_while_remaining_and_again_after_recovery()
{
    var state = new BalanceAlertState();
    var first = Evaluate(22, 20, false, state);
    Assert.Equal([BalanceAlertKind.Warning15], first.EnteredAlerts);
    var still = Evaluate(21, 20, false, first.NextState);
    Assert.Empty(still.EnteredAlerts);
    var recovered = Evaluate(30, 20, false, still.NextState);
    var reentered = Evaluate(22, 20, false, recovered.NextState);
    Assert.Equal([BalanceAlertKind.Warning15], reentered.EnteredAlerts);
}

[Fact]
public void Moving_from_warning_to_critical_notifies_critical()
{
    var warning = Evaluate(22, 20, false, new BalanceAlertState());
    var critical = Evaluate(20, 20, false, warning.NextState);
    Assert.Equal([BalanceAlertKind.Critical], critical.EnteredAlerts);
}
```

- [ ] **Step 3: Write failing palette validation tests**

```csharp
[Theory]
[InlineData("#12ABef", "#FF12ABEF")]
[InlineData("#8012ABEF", "#8012ABEF")]
public void Normalize_accepts_rgb_and_argb(string input, string expected)
{
    Assert.True(IslandColorPalettes.TryNormalizeColor(input, out var actual));
    Assert.Equal(expected, actual);
}

[Theory]
[InlineData("red")]
[InlineData("#12345")]
[InlineData("#GG0000")]
public void Normalize_rejects_non_hex_colors(string input) =>
    Assert.False(IslandColorPalettes.TryNormalizeColor(input, out _));
```

- [ ] **Step 4: Run evaluator and palette tests and verify RED**

Run: `dotnet test tests/BalanceIsland.Windows.Tests/BalanceIsland.Windows.Tests.csproj --filter "BalanceStateEvaluatorTests|IslandColorPalettesTests"`

Expected: compilation fails for missing evaluator and palette APIs.

- [ ] **Step 5: Implement minimal pure logic**

Store the last classified balance band separately from anomaly cooldown in `BalanceAlertState`. `Evaluate` computes the band with `1.15`, detects anomaly using the existing absolute/percent/mode/cooldown fields, emits only newly entered enabled events, resets the band to Normal after recovery, and returns a copied next state. Critical wins visually; anomaly wins over Warning15. Do not emit an event when the snapshot has no amount or is Error.

Implement five immutable palettes and normalized custom colors. Update `AppStateNormalizer` to replace invalid custom values with classic defaults.

- [ ] **Step 6: Run focused and complete tests**

Run: `dotnet test tests/BalanceIsland.Windows.Tests/BalanceIsland.Windows.Tests.csproj --filter "BalanceStateEvaluatorTests|IslandColorPalettesTests"`

Expected: PASS.

Run: `dotnet test BalanceIsland-Windows.sln -c Release`

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/BalanceIsland.Windows/Models.cs src/BalanceIsland.Windows/AppStateNormalizer.cs src/BalanceIsland.Windows/BalanceStateEvaluator.cs src/BalanceIsland.Windows/IslandColorPalettes.cs tests/BalanceIsland.Windows.Tests
git commit -m "feat: classify balance states and color palettes"
```

---

### Task 4: Add rotation and same-Provider aggregate groups

**Files:**
- Create: `src/BalanceIsland.Windows/IslandDisplayGroups.cs`
- Create: `tests/BalanceIsland.Windows.Tests/IslandDisplayGroupsTests.cs`
- Modify: `src/BalanceIsland.Windows/IslandAccountSelection.cs`
- Modify: `src/BalanceIsland.Windows/BalanceCoordinator.cs`
- Modify: `src/BalanceIsland.Windows/Models.cs`

**Interfaces:**
- Produces `IslandDisplayGroups.Create`, `Update`, `Delete`, `SetActive`, and `RemoveAccount` operating on `AppState`.
- Produces `IslandDisplayItem` with Provider/icon/title/primary/secondary/visual state.
- Produces `IslandAccountSelection.VisibleItems(BalanceCoordinator)` replacing UI use of `VisibleSnapshots` while retaining the old method temporarily for compatibility.

- [ ] **Step 1: Write failing group validation tests**

```csharp
[Fact]
public void Rotation_group_accepts_mixed_providers()
{
    var state = StateWith(DeepSeekAccount("a"), OpenAiAccount("b"));
    var group = IslandDisplayGroups.Create(state, "Mixed", IslandGroupMode.Rotation, ["a", "b"]);
    Assert.Equal(2, group.AccountIds.Count);
}

[Fact]
public void Aggregate_group_rejects_mixed_providers()
{
    var state = StateWith(DeepSeekAccount("a"), OpenAiAccount("b"));
    var error = Assert.Throws<ArgumentException>(() =>
        IslandDisplayGroups.Create(state, "Bad", IslandGroupMode.Aggregate, ["a", "b"]));
    Assert.Contains("同一 Provider", error.Message);
}
```

- [ ] **Step 2: Write failing aggregation and cleanup tests**

```csharp
[Fact]
public void Aggregate_sums_balance_and_today_usage_for_same_currency()
{
    var item = Aggregate([Snapshot("a", 10, 2, "USD"), Snapshot("b", 5, 1, "USD")]);
    Assert.Equal(15, item.BalanceAmount);
    Assert.Equal(3, item.TodayUsedAmount);
}

[Fact]
public void Aggregate_refuses_mixed_snapshot_currencies()
{
    var item = Aggregate([Snapshot("a", 10, 2, "USD"), Snapshot("b", 5, 1, "CNY")]);
    Assert.Null(item.BalanceAmount);
    Assert.Contains("币种不一致", item.SecondaryText);
}

[Fact]
public void Removing_account_cleans_all_groups_and_deleting_active_group_clears_selection()
{
    var state = StateWith(DeepSeekAccount("a"));
    var group = IslandDisplayGroups.Create(state, "One", IslandGroupMode.Rotation, ["a"]);
    state.ActiveDisplayGroupId = group.Id;
    IslandDisplayGroups.RemoveAccount(state, "a");
    Assert.Empty(group.AccountIds);
    IslandDisplayGroups.Delete(state, group.Id);
    Assert.Null(state.ActiveDisplayGroupId);
}

private static Account DeepSeekAccount(string id) =>
    new() { Id = id, Provider = Provider.DeepSeek, IsEnabled = true };

private static Account OpenAiAccount(string id) =>
    new() { Id = id, Provider = Provider.OpenAI, IsEnabled = true };

private static AppState StateWith(params Account[] accounts) =>
    new() { Accounts = accounts.ToList() };

private static BalanceSnapshot Snapshot(
    string id, double balance, double used, string currency) => new()
{
    CredentialId = id,
    Provider = Provider.DeepSeek,
    BalanceAmount = balance,
    TodayUsedAmount = used,
    CurrencyCode = currency,
    Status = SnapshotStatus.Ok
};

private static IslandDisplayItem Aggregate(IEnumerable<BalanceSnapshot> snapshots) =>
    IslandDisplayGroups.Aggregate(
        new IslandDisplayGroup
        {
            Id = "g",
            Name = "Team",
            Mode = IslandGroupMode.Aggregate,
            AggregateProvider = Provider.DeepSeek,
            AccountIds = snapshots.Select(x => x.CredentialId).ToList()
        },
        snapshots.ToDictionary(x => x.CredentialId));
```

- [ ] **Step 3: Run group tests and verify RED**

Run: `dotnet test tests/BalanceIsland.Windows.Tests/BalanceIsland.Windows.Tests.csproj --filter IslandDisplayGroupsTests`

Expected: compilation fails for missing group service/display item.

- [ ] **Step 4: Implement group service and display item projection**

Rotation projects enabled group accounts in stored order. Aggregate verifies one Provider, partitions numeric values by currency, refuses a mixed set, sums numeric balance/usage when possible, and otherwise counts healthy versus Error/NotConfigured keys. Visual state is the maximum by the Task 3 priority mapping.

With no active group, project enabled accounts whose `ShowInIsland` is true exactly as v0.2.1. With an active group, membership is authoritative and `ShowInIsland` is ignored. Remove deleted accounts from groups inside `BalanceCoordinator.RemoveAccount` before save.

- [ ] **Step 5: Run tests and build**

Run: `dotnet test tests/BalanceIsland.Windows.Tests/BalanceIsland.Windows.Tests.csproj --filter IslandDisplayGroupsTests`

Expected: PASS.

Run: `dotnet build BalanceIsland-Windows.sln -c Release`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/BalanceIsland.Windows/Models.cs src/BalanceIsland.Windows/IslandDisplayGroups.cs src/BalanceIsland.Windows/IslandAccountSelection.cs src/BalanceIsland.Windows/BalanceCoordinator.cs tests/BalanceIsland.Windows.Tests
git commit -m "feat: add island rotation and aggregate groups"
```

---

### Task 5: Apply selectable application themes and status colors to WPF

**Files:**
- Modify: `src/BalanceIsland.Windows/SystemThemeManager.cs`
- Modify: `src/BalanceIsland.Windows/App.xaml.cs`
- Modify: `src/BalanceIsland.Windows/TaskbarIslandWindow.xaml`
- Modify: `src/BalanceIsland.Windows/TaskbarIslandWindow.xaml.cs`
- Modify: `src/BalanceIsland.Windows/BalanceCoordinator.cs`

**Interfaces:**
- Consumes `AppThemeMode`, `IslandColorPalettes.Resolve`, and `IslandDisplayItem`.
- Produces `SystemThemeManager.SetMode(AppThemeMode)` and coordinator setters `SetThemeMode`, `SetIslandColorTheme`, and `SetCustomIslandColors`.

- [ ] **Step 1: Add a failing coordinator persistence test**

```csharp
[Fact]
public void Appearance_setters_update_state_and_emit_change()
{
    using var coordinator = TestFactory.CreateCoordinator();
    var changes = 0;
    coordinator.StateChanged += (_, _) => changes++;
    coordinator.SetThemeMode(AppThemeMode.Dark);
    coordinator.SetIslandColorTheme(IslandColorTheme.Sky);
    coordinator.SetCustomIslandColors("#FFFFFF", "#D778FF", "#FFB340", "#FF5C6C");
    Assert.Equal(AppThemeMode.Dark, coordinator.State.ThemeMode);
    Assert.True(changes >= 3);
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run: `dotnet test tests/BalanceIsland.Windows.Tests/BalanceIsland.Windows.Tests.csproj --filter Appearance_setters`

Expected: compilation fails because the setters do not exist.

- [ ] **Step 3: Implement appearance setters and theme manager mode**

Construct the coordinator before `SystemThemeManager`, pass `State.ThemeMode`, and make `SetMode` reapply resources/title bars immediately. `UserPreferenceChanged` only changes light/dark selection when mode is System; high contrast remains the first branch.

- [ ] **Step 4: Render display items and palette brushes**

Change `TaskbarIslandWindow.Render` to consume `VisibleItems`. Set both title and usage foreground brushes from the active palette and item visual state. Keep the current shadow, icon backgrounds, icon visual-size normalization, dimensions, DPI positioning, edit chrome, and fullscreen behavior unchanged.

- [ ] **Step 5: Run tests and parse/build XAML**

Run: `dotnet test BalanceIsland-Windows.sln -c Release`

Expected: PASS.

Run: `dotnet build BalanceIsland-Windows.sln -c Release`

Expected: PASS, proving modified XAML loads at compile time.

- [ ] **Step 6: Commit**

```bash
git add src/BalanceIsland.Windows/SystemThemeManager.cs src/BalanceIsland.Windows/App.xaml.cs src/BalanceIsland.Windows/TaskbarIslandWindow.xaml src/BalanceIsland.Windows/TaskbarIslandWindow.xaml.cs src/BalanceIsland.Windows/BalanceCoordinator.cs tests/BalanceIsland.Windows.Tests
git commit -m "feat: apply selectable themes and island colors"
```

---

### Task 6: Build the opt-in environment import workflow and Provider search/list UI

**Files:**
- Create: `src/BalanceIsland.Windows/EnvironmentImportWindow.xaml`
- Create: `src/BalanceIsland.Windows/EnvironmentImportWindow.xaml.cs`
- Modify: `src/BalanceIsland.Windows/BalanceCoordinator.cs`
- Modify: `src/BalanceIsland.Windows/MainWindow.xaml`
- Modify: `src/BalanceIsland.Windows/MainWindow.xaml.cs`

**Interfaces:**
- Consumes `ProviderCatalog.Search`, `ProviderCatalog.All`, and environment candidates from Task 2.
- Produces `BalanceCoordinator.ImportEnvironmentAccounts(IEnumerable<EnvironmentCredentialCandidate>)`.
- Produces `EnvironmentImportWindow.SelectedCandidates` containing checked rows only.

- [ ] **Step 1: Write failing selected-import tests**

```csharp
[Fact]
public void Import_only_adds_selected_candidates_and_deduplicates_variable()
{
    using var coordinator = TestFactory.CreateCoordinator();
    var selected = new EnvironmentCredentialCandidate(
        Provider.OpenRouter, "ROUTER_KEY", "sk-or-v1-abc1234", "User", "Key prefix");
    var ignored = new EnvironmentCredentialCandidate(
        Provider.Gemini, "GEMINI_KEY", "AIza-example5678", "User", "Key prefix");
    coordinator.ImportEnvironmentAccounts([selected]);
    coordinator.ImportEnvironmentAccounts([selected]);
    Assert.Single(coordinator.State.Accounts);
    Assert.DoesNotContain(coordinator.State.Accounts, x => x.Provider == ignored.Provider);
    Assert.Equal("ROUTER_KEY", coordinator.State.Accounts[0].EnvironmentVariableName);
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run: `dotnet test tests/BalanceIsland.Windows.Tests/BalanceIsland.Windows.Tests.csproj --filter Import_only_adds_selected`

Expected: compilation fails because the selected-candidate overload does not exist.

- [ ] **Step 3: Implement selected import and remove silent background import**

Change `SetEnvironmentAutoImport` to persist “scan on startup” only. Remove imports from the timer and coordinator constructor. Import the supplied candidate list only, skip existing environment variable/provider pairs and explicit identical credentials, create masked accounts, then refresh only newly added enabled accounts.

- [ ] **Step 4: Create the import dialog**

Use a read-only `DataGrid` with a writable checkbox column and columns for variable name, scope, matched Provider, masked suffix, match reason, and state (`可导入`/`已导入`). Initialize every checkbox false. Disable selection for already imported rows. Cancel returns no candidates; Import returns checked candidates.

- [ ] **Step 5: Replace the environment page and Provider selector**

Add `ProviderSearchBox` above the existing Provider `ComboBox`; `TextChanged` assigns `ProviderCatalog.Search(text)` and preserves selection when possible. Replace `EnvironmentVariablesText` with a DataGrid bound to catalog definitions. “扫描环境” opens the dialog and imports only after `ShowDialog() == true`. When startup scanning is enabled, schedule the same dialog after the main window `Loaded` event; never show it from a background timer.

- [ ] **Step 6: Run tests and build**

Run: `dotnet test BalanceIsland-Windows.sln -c Release`

Expected: PASS.

Run: `dotnet build BalanceIsland-Windows.sln -c Release`

Expected: PASS with the new WPF dialog compiled.

- [ ] **Step 7: Commit**

```bash
git add src/BalanceIsland.Windows/EnvironmentImportWindow.xaml src/BalanceIsland.Windows/EnvironmentImportWindow.xaml.cs src/BalanceIsland.Windows/BalanceCoordinator.cs src/BalanceIsland.Windows/MainWindow.xaml src/BalanceIsland.Windows/MainWindow.xaml.cs tests/BalanceIsland.Windows.Tests
git commit -m "feat: add confirmed environment credential import"
```

---

### Task 7: Deliver urgent native Windows notifications with fallback

**Files:**
- Create: `src/BalanceIsland.Windows/DesktopNotificationIdentity.cs`
- Create: `src/BalanceIsland.Windows/WindowsNotificationService.cs`
- Create: `tests/BalanceIsland.Windows.Tests/ToastPayloadBuilderTests.cs`
- Modify: `src/BalanceIsland.Windows/BalanceCoordinator.cs`
- Modify: `src/BalanceIsland.Windows/App.xaml.cs`
- Modify: `src/BalanceIsland.Windows/app.manifest`

**Interfaces:**
- Produces `INotificationService.Send(BalanceNotification): NotificationDeliveryResult` and `SendTest()`.
- Produces `NotificationChannelStatus` and `ToastPayloadBuilder.Build(BalanceNotification, bool urgentSupported)`.
- Changes `BalanceAlertEventArgs` to carry `BalanceAlertKind Kind`, title, message, account ID, and masked label.

- [ ] **Step 1: Write failing toast payload tests**

```csharp
[Fact]
public void Windows11_payload_is_urgent_and_contains_no_full_key()
{
    var xml = ToastPayloadBuilder.Build(
        new BalanceNotification(BalanceAlertKind.Critical, "DeepSeek · Prod", "¥19.00，已到达警戒线", "••••1234"),
        urgentSupported: true);
    Assert.Contains("scenario=\"urgent\"", xml);
    Assert.Contains("••••1234", xml);
    Assert.DoesNotContain("sk-secret", xml);
}

[Fact]
public void Windows10_payload_omits_unsupported_urgent_scenario()
{
    var xml = ToastPayloadBuilder.Build(
        new BalanceNotification(BalanceAlertKind.Warning15, "OpenAI · Dev", "$22.00，接近警戒线", "••••5678"),
        urgentSupported: false);
    Assert.DoesNotContain("scenario=", xml);
}
```

- [ ] **Step 2: Run toast tests and verify RED**

Run: `dotnet test tests/BalanceIsland.Windows.Tests/BalanceIsland.Windows.Tests.csproj --filter ToastPayloadBuilderTests`

Expected: compilation fails because notification types/payload builder do not exist.

- [ ] **Step 3: Implement safe payload creation**

Use XML APIs rather than string interpolation for text nodes. The root toast uses `scenario="urgent"` only on Windows build 22546+, and each `ToastNotification` sets `Priority = ToastNotificationPriority.High`. Include app name, event kind, masked account, amount/change, and trigger reason only.

- [ ] **Step 4: Register unpackaged desktop notification identity**

Call `SetCurrentProcessExplicitAppUserModelID("Noyorin.BalanceIsland")`. Ensure a Start Menu shortcut under `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Balance Island.lnk` targets `Environment.ProcessPath` and has `PKEY_AppUserModel_ID` set to the same ID through `IShellLinkW`/`IPropertyStore`. Recreate it only when target or AppUserModelID differs. Return a status object rather than terminating startup on COM failure.

- [ ] **Step 5: Implement delivery and app fallback**

Create `ToastNotificationManager.CreateToastNotifier(AppUserModelId)`, show the toast, and return NativeToast. On registration/show exceptions return Failed with a sanitized message. In `App`, send through the service; only a Failed result invokes the existing NotifyIcon balloon and updates notification status to TrayFallback.

Wire Task 3 evaluation into `BalanceCoordinator`: update stored visual state, emit enabled newly entered events with kind, preserve anomaly cooldown, and never send during state migration or test notifications.

- [ ] **Step 6: Run tests and build**

Run: `dotnet test BalanceIsland-Windows.sln -c Release`

Expected: PASS.

Run: `dotnet build BalanceIsland-Windows.sln -c Release`

Expected: PASS against Windows SDK projections without a Windows App SDK package.

- [ ] **Step 7: Commit**

```bash
git add src/BalanceIsland.Windows/DesktopNotificationIdentity.cs src/BalanceIsland.Windows/WindowsNotificationService.cs src/BalanceIsland.Windows/BalanceCoordinator.cs src/BalanceIsland.Windows/App.xaml.cs src/BalanceIsland.Windows/app.manifest tests/BalanceIsland.Windows.Tests
git commit -m "feat: send urgent Windows balance notifications"
```

---

### Task 8: Complete display/group/notification settings UI

**Files:**
- Modify: `src/BalanceIsland.Windows/MainWindow.xaml`
- Modify: `src/BalanceIsland.Windows/MainWindow.xaml.cs`
- Modify: `src/BalanceIsland.Windows/App.xaml`
- Modify: `src/BalanceIsland.Windows/BalanceCoordinator.cs`

**Interfaces:**
- Consumes all services and setters from Tasks 1–7.
- Produces UI handlers for theme, palettes/custom colors, group CRUD/member selection, notification toggles/status/test, and contextual help.

- [ ] **Step 1: Add coordinator tests for group and notification settings persistence**

```csharp
[Fact]
public void Notification_settings_persist_independently()
{
    using var coordinator = TestFactory.CreateCoordinator();
    coordinator.SetNotificationSettings(warning15: false, critical: true, anomaly: false);
    Assert.False(coordinator.State.NotifyWarning15);
    Assert.True(coordinator.State.NotifyCritical);
    Assert.False(coordinator.State.NotifyAnomaly);
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run: `dotnet test tests/BalanceIsland.Windows.Tests/BalanceIsland.Windows.Tests.csproj --filter Notification_settings_persist`

Expected: compilation fails because the setter does not exist.

- [ ] **Step 3: Implement minimal settings methods**

Add validated coordinator methods for group create/update/delete/activate, theme/palette/custom colors, and the three notification flags. Every successful method saves once and raises `StateChanged` once; invalid group/color input throws a Chinese actionable `ArgumentException` without saving.

- [ ] **Step 4: Rebuild “显示与外观” into four cards**

Add application theme ComboBox; palette ComboBox; four custom color TextBoxes and preview swatches; group selector/name/mode; account multi-select list; New/Save/Delete/Set Active buttons; and retain the current position/size/edit card. Disable custom fields unless Custom is selected. Disable Save for an invalid aggregate selection and show “聚合分组只能包含同一 Provider”.

- [ ] **Step 5: Complete “刷新与通知”**

Add three independent CheckBoxes, channel status text (`Windows 重要通知` / `普通 Windows 通知` / `托盘回退` / `不可用`), an explanation that Windows settings remain authoritative, a “发送测试通知” button, and a button that launches `ms-settings:notifications` with `Process.Start(new ProcessStartInfo { FileName = ..., UseShellExecute = true })`.

- [ ] **Step 6: Bind controls without recursive saves**

Reuse `_loadingControls`; all `RefreshRows` population sets it true, while user handlers return immediately when true. Preserve selected account/group IDs during refresh. Track newly opened dialogs through `App.TrackWindow` so native title bars follow the selected mode.

- [ ] **Step 7: Run tests and XAML build**

Run: `dotnet test BalanceIsland-Windows.sln -c Release`

Expected: PASS.

Run: `dotnet build BalanceIsland-Windows.sln -c Release`

Expected: PASS with all event-handler names resolved and no XAML parse errors.

- [ ] **Step 8: Commit**

```bash
git add src/BalanceIsland.Windows/MainWindow.xaml src/BalanceIsland.Windows/MainWindow.xaml.cs src/BalanceIsland.Windows/App.xaml src/BalanceIsland.Windows/BalanceCoordinator.cs tests/BalanceIsland.Windows.Tests
git commit -m "feat: add display group and notification settings UI"
```

---

### Task 9: Add tutorials, versioning, CI tests, and final verification

**Files:**
- Modify: `README.md`
- Modify: `CHANGELOG.md`
- Modify: `src/BalanceIsland.Windows/BalanceIsland.Windows.csproj`
- Modify: `src/BalanceIsland.Windows/MainWindow.xaml`
- Modify: `.github/workflows/windows-build.yml`

**Interfaces:**
- Consumes final user-facing behavior from Tasks 1–8.
- Produces v0.3.0 artifact naming and documented Win10/Win11 test procedure.

- [ ] **Step 1: Update version and workflow test gate**

Set `<Version>0.3.0</Version>`, update the visible subtitle to `v0.3.0`, and rename the artifact to `BalanceIsland-Windows-v0.3.0-win-x64`. Insert before Build:

```yaml
- name: Test
  run: dotnet test BalanceIsland-Windows.sln --configuration Release --no-restore
```

- [ ] **Step 2: Write six complete README tutorials**

Document exact navigation and steps for: theme mode; four-state palette/custom color; rotation and aggregate groups; environment scan/confirmation/privacy; Provider search/support list and future catalog synchronization; Windows important notifications, permission settings, test button, deduplication, and Windows 10 fallback.

Add a two-machine manual checklist labeled `win11-24h2-vm` and `win10-22h2-vm`. Remove “custom rotation groups” and “complete anomaly UI” from the old future-work list because they are delivered.

- [ ] **Step 3: Update CHANGELOG and in-app descriptions**

Add a `0.3.0` section with all six features, 1.15 boundary, migration defaults, urgent/ordinary toast distinction, and tests. Ensure every new card has a purpose sentence and a one-line usage hint; no card refers to implementation classes.

- [ ] **Step 4: Run final automated verification**

Run: `dotnet restore BalanceIsland-Windows.sln`

Run: `dotnet test BalanceIsland-Windows.sln -c Release --no-restore`

Run: `dotnet build BalanceIsland-Windows.sln -c Release --no-restore`

Run: `dotnet publish src/BalanceIsland.Windows/BalanceIsland.Windows.csproj -c Release -r win-x64 --self-contained false -o artifacts/win-x64 -p:PublishSingleFile=true`

Expected: every command exits 0; test output has zero failed tests; publish contains `BalanceIsland.exe` and required runtime metadata without a Windows App SDK runtime dependency.

- [ ] **Step 5: Run static safety checks**

Run: `git diff --check`

Run: `rg -n "sk-[A-Za-z0-9_-]{12,}|AIza[A-Za-z0-9_-]{12,}|xai-[A-Za-z0-9_-]{12,}" --glob '!docs/superpowers/**' --glob '!tests/**'`

Expected: no whitespace errors; no real-looking complete API Key literals outside deliberate masked test fixtures.

- [ ] **Step 6: Commit documentation/release metadata**

```bash
git add README.md CHANGELOG.md src/BalanceIsland.Windows/BalanceIsland.Windows.csproj src/BalanceIsland.Windows/MainWindow.xaml .github/workflows/windows-build.yml
git commit -m "docs: add v0.3.0 setup and usage guide"
```

- [ ] **Step 7: Push, create PR, and wait for Windows Build**

```bash
git push -u origin feature/display-groups-notifications-v030
```

Create a PR into `main` titled `feat: add display groups and important notifications`. Include behavior summary, migration notes, automated verification, and the Win10/Win11 manual matrix. Wait for `Windows Build / Build Windows app`; if it fails, read the exact failing job log, add a regression test when behavior-related, fix on the same branch, and rerun until green.

- [ ] **Step 8: Hand off without merging**

Report branch, commits, PR URL, CI run URL/state, artifact name, remaining VM checks, and that no merge/tag/Release occurred.
