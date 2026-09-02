namespace BalanceIsland.Windows;

public enum CodexPlanWindowKind
{
    FiveHour,
    Weekly,
    Unknown
}

public enum CodexPlanReadError
{
    Auth,
    RateLimit,
    Network,
    Http,
    Parse,
    Runtime
}

public sealed class CodexPlanQuotaWindow
{
    public int RemainingPercent { get; set; }
    public long? ResetAtUnixSeconds { get; set; }
    public long? WindowSeconds { get; set; }
}

public sealed class CodexPlanUsage
{
    public string PlanType { get; set; } = "";
    public CodexPlanQuotaWindow? Primary { get; set; }
    public CodexPlanQuotaWindow? Secondary { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CodexPlanReadState
{
    public CodexPlanReadError? LastError { get; set; }
    public bool AutoRefreshPaused { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? LastSuccessfulAt { get; set; }
}
