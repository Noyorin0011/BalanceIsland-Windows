using System.Text.Json.Serialization;

namespace BalanceIsland.Windows;

public enum BalanceCapability
{
    DirectBalance,
    UsageOrLimit,
    KeyCheckOnly
}

public enum Provider
{
    DeepSeek,
    OpenAI,
    OpenRouter,
    SiliconFlow,
    Moonshot,
    MiMo,
    Anthropic,
    Gemini,
    XAI
}

public enum SnapshotStatus
{
    Ok,
    Warning,
    Critical,
    Error,
    NotConfigured
}

public enum AnomalyMode
{
    Absolute,
    Percent,
    Both
}

public enum BalanceVisualState
{
    Normal,
    Warning15,
    Anomaly,
    Critical
}

public enum BalanceAlertKind
{
    Warning15,
    Critical,
    Anomaly
}

public enum AppThemeMode
{
    System,
    Light,
    Dark
}

public enum IslandColorTheme
{
    Classic,
    Mint,
    Sky,
    Coral,
    Lime,
    Custom
}

public enum IslandGroupMode
{
    Rotation,
    Aggregate
}

public enum IslandDisplayMode
{
    Floating,
    TaskbarEmbedded
}

public enum IslandPositionPreset
{
    Left,
    Center,
    Right,
    Custom
}

public enum IslandSizePreset
{
    Compact,
    Standard,
    Large,
    Custom
}

public enum CredentialSource
{
    WindowsCredentialManager,
    EnvironmentVariable
}

public static class ProviderInfo
{
    public static string DisplayName(this Provider value) => ProviderCatalog.Get(value).DisplayName;

    public static string DefaultCurrency(this Provider value) => ProviderCatalog.Get(value).DefaultCurrency;

    public static BalanceCapability Capability(this Provider value) => ProviderCatalog.Get(value).Capability;

    public static int RecommendedRefreshMinutes(this Provider value) => ProviderCatalog.Get(value).RecommendedRefreshMinutes;
}

public sealed class Account
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Provider Provider { get; set; }
    public string Label { get; set; } = "";
    public string KeySuffix { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public int RefreshIntervalMinutes { get; set; }
    public bool ShowInIsland { get; set; } = true;
    public bool AlertEnabled { get; set; } = true;
    public double WarningLine { get; set; } = 20;
    public double DropStep { get; set; } = 5;
    public double? ManualBalance { get; set; }
    public bool AnomalyEnabled { get; set; }
    public double AnomalyThreshold { get; set; } = 50;
    public double AnomalyPercentThreshold { get; set; } = 50;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AnomalyMode AnomalyMode { get; set; } = AnomalyMode.Both;
    public int AnomalyCooldownMinutes { get; set; } = 1440;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CredentialSource CredentialSource { get; set; } = CredentialSource.WindowsCredentialManager;
    public string? EnvironmentVariableName { get; set; }

    [JsonIgnore]
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? ApiKeySanitizer.MaskSuffix(KeySuffix) : Label;
    [JsonIgnore]
    public string CredentialSourceLabel => CredentialSource == CredentialSource.EnvironmentVariable
        ? $"环境变量 · {EnvironmentVariableName}" : "Windows 凭据管理器";
    [JsonIgnore]
    public int EffectiveRefreshMinutes => RefreshIntervalMinutes > 0
        ? Math.Clamp(RefreshIntervalMinutes, 1, 1440)
        : Provider.RecommendedRefreshMinutes();
}

public sealed class BalanceSnapshot
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Provider Provider { get; set; }
    public string CredentialId { get; set; } = "";
    public string AccountLabel { get; set; } = "";
    public string KeySuffix { get; set; } = "";
    public string PrimaryText { get; set; } = "等待刷新";
    public string SecondaryText { get; set; } = "";
    public double? BalanceAmount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public bool IsManualBalance { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SnapshotStatus Status { get; set; } = SnapshotStatus.NotConfigured;
    public DateTimeOffset UpdatedAt { get; set; }
    public double? TodayUsedAmount { get; set; }
    public bool TodayUsageIsEstimated { get; set; }

    [JsonIgnore]
    public string AccountDisplayLabel => string.IsNullOrWhiteSpace(AccountLabel)
        ? ApiKeySanitizer.MaskSuffix(KeySuffix) : AccountLabel;
    [JsonIgnore]
    public string IslandText
    {
        get
        {
            var today = TodayUsedAmount is null ? "" : $" · 今日 {CurrencySymbol(CurrencyCode)}{TodayUsedAmount:0.00}";
            return $"{Provider.DisplayName()}  {AccountDisplayLabel}  {PrimaryText}{today}";
        }
    }

    public static string CurrencySymbol(string code) => code.ToUpperInvariant() switch
    {
        "CNY" or "RMB" => "¥",
        "EUR" => "€",
        "GBP" => "£",
        _ => "$"
    };
}

public sealed class ScheduleState
{
    public DateTimeOffset? LastAttempt { get; set; }
    public DateTimeOffset? NextScheduled { get; set; }
    public DateTimeOffset? RateLimitUntil { get; set; }
    public int BackoffLevel { get; set; }
}

public sealed class DailyUsageState
{
    public DateOnly Date { get; set; }
    public double OpeningBalance { get; set; }
    public double LastBalance { get; set; }
    public double ObservedTopUps { get; set; }
    public double UsedToday { get; set; }
}

public sealed class BalanceAlertState
{
    public double? LastNotifiedAmount { get; set; }
    public int LastLevel { get; set; }
    // This tracks only the balance band (Normal, Warning15, or Critical), never a transient anomaly.
    public BalanceVisualState LastBalanceBand { get; set; }
    // This is the most recently evaluated island state and may include a transient Anomaly.
    public BalanceVisualState LastVisualState { get; set; }
    public double? LastSeenAmount { get; set; }
    public DateTimeOffset? LastAnomalyAt { get; set; }
}

public sealed class IslandDisplayGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新分组";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IslandGroupMode Mode { get; set; } = IslandGroupMode.Rotation;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Provider? AggregateProvider { get; set; }
    public List<string> AccountIds { get; set; } = [];
}

public sealed class IslandDisplayItem
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Provider? Provider { get; init; }
    public string? IconResourceKey { get; init; }
    public string Title { get; init; } = "";
    // A compact account identifier (note or masked key suffix) used for the vertical-taskbar
    // island layout's first line.
    public string DetailLabel { get; init; } = "";
    public string PrimaryText { get; init; } = "";
    public string SecondaryText { get; init; } = "";
    public double? BalanceAmount { get; init; }
    public double? TodayUsedAmount { get; init; }
    public string CurrencyCode { get; init; } = "USD";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BalanceVisualState VisualState { get; init; }
}

public sealed class AppState
{
    public int? SafeKeySuffixVersion { get; set; }
    public List<Account> Accounts { get; set; } = [];
    public Dictionary<string, BalanceSnapshot> Snapshots { get; set; } = [];
    public Dictionary<string, ScheduleState> Schedules { get; set; } = [];
    public Dictionary<string, DailyUsageState> DailyUsage { get; set; } = [];
    public Dictionary<string, BalanceAlertState> Alerts { get; set; } = [];
    public bool IslandEnabled { get; set; } = true;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IslandDisplayMode IslandDisplayMode { get; set; } = IslandDisplayMode.Floating;
    public bool IslandEditMode { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IslandPositionPreset IslandPositionPreset { get; set; } = IslandPositionPreset.Left;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IslandSizePreset IslandSizePreset { get; set; } = IslandSizePreset.Standard;
    public double IslandWidth { get; set; } = 225;
    public double IslandHeight { get; set; } = 38;
    public double IslandCustomLeftDip { get; set; }
    public double IslandCustomTopDip { get; set; }
    public double IslandEditLeft { get; set; } = double.NaN;
    public double IslandEditTop { get; set; } = double.NaN;
    public int IslandLayoutVersion { get; set; }
    public bool SilentStartupEnabled { get; set; }
    public bool EnvironmentAutoImportEnabled { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IslandColorTheme IslandColorTheme { get; set; } = IslandColorTheme.Classic;
    public string CustomNormalColor { get; set; } = "#FFFFFFFF";
    public string CustomAnomalyColor { get; set; } = "#FFD778FF";
    public string CustomWarning15Color { get; set; } = "#FFFFB340";
    public string CustomCriticalColor { get; set; } = "#FFFF5C6C";
    public List<IslandDisplayGroup> DisplayGroups { get; set; } = [];
    public string? ActiveDisplayGroupId { get; set; }
    public bool NotifyWarning15 { get; set; } = true;
    public bool NotifyCritical { get; set; } = true;
    public bool NotifyAnomaly { get; set; } = true;
}

public sealed record ApiCredential(Account Account, string ApiKey);

public sealed class ProviderApiException(
    string message,
    int? statusCode = null,
    TimeSpan? retryAfter = null) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
