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

public enum IslandDisplayMode
{
    Floating,
    TaskbarEmbedded
}

public static class ProviderInfo
{
    public static string DisplayName(this Provider value) => value switch
    {
        Provider.DeepSeek => "DeepSeek",
        Provider.OpenAI => "OpenAI",
        Provider.OpenRouter => "OpenRouter",
        Provider.SiliconFlow => "SiliconFlow",
        Provider.Moonshot => "Kimi / Moonshot",
        Provider.MiMo => "Xiaomi MiMo",
        Provider.Anthropic => "Anthropic",
        Provider.Gemini => "Google Gemini",
        Provider.XAI => "xAI / Grok",
        _ => value.ToString()
    };

    public static string DefaultCurrency(this Provider value) => value is
        Provider.DeepSeek or Provider.SiliconFlow or Provider.Moonshot or Provider.MiMo
        ? "CNY" : "USD";

    public static BalanceCapability Capability(this Provider value) => value switch
    {
        Provider.DeepSeek or Provider.OpenRouter or Provider.SiliconFlow or Provider.Moonshot
            => BalanceCapability.DirectBalance,
        Provider.OpenAI => BalanceCapability.UsageOrLimit,
        _ => BalanceCapability.KeyCheckOnly
    };

    public static int RecommendedRefreshMinutes(this Provider value) => value switch
    {
        Provider.DeepSeek => 1,
        Provider.OpenAI => 5,
        Provider.OpenRouter or Provider.SiliconFlow or Provider.Moonshot => 2,
        _ => 15
    };
}

public sealed class Account
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Provider Provider { get; set; }
    public string Label { get; set; } = "";
    public string KeySuffix { get; set; } = "";
    public int RefreshIntervalMinutes { get; set; }
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

    [JsonIgnore]
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? $"••••{KeySuffix}" : Label;
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
        ? $"••••{KeySuffix}" : AccountLabel;
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
    public double? LastSeenAmount { get; set; }
    public DateTimeOffset? LastAnomalyAt { get; set; }
}

public sealed class AppState
{
    public List<Account> Accounts { get; set; } = [];
    public Dictionary<string, BalanceSnapshot> Snapshots { get; set; } = [];
    public Dictionary<string, ScheduleState> Schedules { get; set; } = [];
    public Dictionary<string, DailyUsageState> DailyUsage { get; set; } = [];
    public Dictionary<string, BalanceAlertState> Alerts { get; set; } = [];
    public bool IslandEnabled { get; set; } = true;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IslandDisplayMode IslandDisplayMode { get; set; } = IslandDisplayMode.Floating;
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
