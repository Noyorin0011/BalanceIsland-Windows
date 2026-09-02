using System.Text.Json;

namespace BalanceIsland.Windows;

internal static class AppStateSemanticValidator
{
    private static readonly HashSet<string> EnumPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "provider", "status", "anomalyMode", "credentialSource", "lastBalanceBand", "lastVisualState",
        "mode", "aggregateProvider", "islandDisplayMode", "islandPositionPreset", "islandSizePreset",
        "themeMode", "islandColorTheme", "lastError"
    };

    internal static void ValidateJsonTokens(string json)
    {
        using var document = JsonDocument.Parse(json);
        RejectIntegerEnums(document.RootElement);
    }

    internal static void Validate(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateDefined(state.IslandDisplayMode, nameof(state.IslandDisplayMode));
        ValidateDefined(state.IslandPositionPreset, nameof(state.IslandPositionPreset));
        ValidateDefined(state.IslandSizePreset, nameof(state.IslandSizePreset));
        ValidateDefined(state.ThemeMode, nameof(state.ThemeMode));
        ValidateDefined(state.IslandColorTheme, nameof(state.IslandColorTheme));
        RequireFinite(state.IslandWidth, nameof(state.IslandWidth));
        RequireFinite(state.IslandHeight, nameof(state.IslandHeight));
        RequireFinite(state.IslandCustomLeftDip, nameof(state.IslandCustomLeftDip));
        RequireFinite(state.IslandCustomTopDip, nameof(state.IslandCustomTopDip));
        RequireFiniteOrNaNSentinel(state.IslandEditLeft, nameof(state.IslandEditLeft));
        RequireFiniteOrNaNSentinel(state.IslandEditTop, nameof(state.IslandEditTop));
        ValidateAccounts(state.Accounts);
        ValidateGroups(state.DisplayGroups);
        ValidateSnapshots(state.Snapshots);
        ValidateDailyUsage(state.DailyUsage);
        ValidateAlerts(state.Alerts);
        ValidateCodexPlanUsage(state.CodexPlanUsage);
        ValidateCodexPlanReadState(state.CodexPlanReadState);
    }

    private static void ValidateCodexPlanUsage(CodexPlanUsage? usage)
    {
        if (usage is null) return;
        if (usage.UpdatedAt == default)
            throw Invalid("CodexPlanUsage.UpdatedAt 不能是默认时间。");
        ValidateCodexPlanWindow(usage.Primary, nameof(CodexPlanUsage.Primary));
        ValidateCodexPlanWindow(usage.Secondary, nameof(CodexPlanUsage.Secondary));
    }

    private static void ValidateCodexPlanWindow(CodexPlanQuotaWindow? window, string name)
    {
        if (window is null) return;
        if (window.RemainingPercent is < 0 or > 100)
            throw Invalid($"CodexPlanQuotaWindow.{name}.RemainingPercent 必须在 0..100 范围。");
        if (window.WindowSeconds is { } windowSeconds && windowSeconds <= 0)
            throw Invalid($"CodexPlanQuotaWindow.{name}.WindowSeconds 必须是正数。");
        if (window.ResetAtUnixSeconds is { } resetAt && resetAt <= 0)
            throw Invalid($"CodexPlanQuotaWindow.{name}.ResetAtUnixSeconds 必须是正数。");
    }

    private static void ValidateCodexPlanReadState(CodexPlanReadState? readState)
    {
        if (readState is null) return;
        if (readState.LastError is { } error) ValidateDefined(error, nameof(readState.LastError));
    }

    private static void ValidateAccounts(IEnumerable<Account>? accounts)
    {
        if (accounts is null) return;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var account in accounts.Cast<Account?>())
        {
            if (account is null) throw Invalid("账户列表包含 null 项。");
            if (string.IsNullOrWhiteSpace(account.Id)) throw Invalid("账户 ID 不能为空。");
            if (!ids.Add(account.Id)) throw Invalid("账户 ID 不能重复。");
            ValidateDefined(account.Provider, nameof(account.Provider));
            ValidateDefined(account.AnomalyMode, nameof(account.AnomalyMode));
            ValidateDefined(account.CredentialSource, nameof(account.CredentialSource));
            RequireFinite(account.WarningLine, nameof(account.WarningLine));
            RequireFinite(account.DropStep, nameof(account.DropStep));
            RequireFinite(account.ManualBalance, nameof(account.ManualBalance));
            RequireFinite(account.AnomalyThreshold, nameof(account.AnomalyThreshold));
            RequireFinite(account.AnomalyPercentThreshold, nameof(account.AnomalyPercentThreshold));
        }
    }

    private static void ValidateGroups(IEnumerable<IslandDisplayGroup>? groups)
    {
        if (groups is null) return;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups.Cast<IslandDisplayGroup?>())
        {
            if (group is null) throw Invalid("显示分组列表包含 null 项。");
            if (string.IsNullOrWhiteSpace(group.Id)) throw Invalid("显示分组 ID 不能为空。");
            if (!ids.Add(group.Id)) throw Invalid("显示分组 ID 不能重复。");
            ValidateDefined(group.Mode, nameof(group.Mode));
            if (group.AggregateProvider is { } provider) ValidateDefined(provider, nameof(group.AggregateProvider));
        }
    }

    private static void ValidateSnapshots(IReadOnlyDictionary<string, BalanceSnapshot>? snapshots)
    {
        if (snapshots is null) return;
        foreach (var snapshot in snapshots.Values.OfType<BalanceSnapshot>())
        {
            ValidateDefined(snapshot.Provider, nameof(snapshot.Provider));
            ValidateDefined(snapshot.Status, nameof(snapshot.Status));
            RequireFinite(snapshot.BalanceAmount, nameof(snapshot.BalanceAmount));
            RequireFinite(snapshot.TodayUsedAmount, nameof(snapshot.TodayUsedAmount));
        }
    }

    private static void ValidateDailyUsage(IReadOnlyDictionary<string, DailyUsageState>? dailyUsage)
    {
        if (dailyUsage is null) return;
        foreach (var usage in dailyUsage.Values.OfType<DailyUsageState>())
        {
            RequireFinite(usage.OpeningBalance, nameof(usage.OpeningBalance));
            RequireFinite(usage.LastBalance, nameof(usage.LastBalance));
            RequireFinite(usage.ObservedTopUps, nameof(usage.ObservedTopUps));
            RequireFinite(usage.UsedToday, nameof(usage.UsedToday));
        }
    }

    private static void ValidateAlerts(IReadOnlyDictionary<string, BalanceAlertState>? alerts)
    {
        if (alerts is null) return;
        foreach (var alert in alerts.Values.OfType<BalanceAlertState>())
        {
            ValidateDefined(alert.LastBalanceBand, nameof(alert.LastBalanceBand));
            ValidateDefined(alert.LastVisualState, nameof(alert.LastVisualState));
            RequireFinite(alert.LastNotifiedAmount, nameof(alert.LastNotifiedAmount));
            RequireFinite(alert.LastSeenAmount, nameof(alert.LastSeenAmount));
        }
    }

    private static void RejectIntegerEnums(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (EnumPropertyNames.Contains(property.Name) && property.Value.ValueKind == JsonValueKind.Number)
                    throw Invalid($"枚举字段 {property.Name} 必须使用字符串值。");
                RejectIntegerEnums(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) RejectIntegerEnums(item);
        }
    }

    private static void ValidateDefined<TEnum>(TEnum value, string name) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw Invalid($"枚举字段 {name} 包含未知值。");
    }

    private static void RequireFinite(double? value, string name)
    {
        if (value is { } number && !double.IsFinite(number)) throw Invalid($"数字字段 {name} 必须是有限值。");
    }

    private static void RequireFiniteOrNaNSentinel(double value, string name)
    {
        if (!double.IsFinite(value) && !double.IsNaN(value))
            throw Invalid($"数字字段 {name} 必须是有限值或 NaN 位置哨兵。");
    }

    private static JsonException Invalid(string message) => new($"状态文件语义无效：{message}");
}
