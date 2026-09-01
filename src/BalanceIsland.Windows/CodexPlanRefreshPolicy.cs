namespace BalanceIsland.Windows;

public static class CodexPlanRefreshPolicy
{
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(5);

    public static bool CanAttempt(DateTimeOffset now, DateTimeOffset? lastAttemptAt) =>
        lastAttemptAt is null || now - lastAttemptAt.Value >= MinimumInterval;

    public static TimeSpan NextDelay(DateTimeOffset now, DateTimeOffset? lastAttemptAt)
    {
        if (lastAttemptAt is null) return TimeSpan.Zero;
        var elapsed = now - lastAttemptAt.Value;
        return elapsed >= MinimumInterval ? TimeSpan.Zero : MinimumInterval - elapsed;
    }

    public static bool ShouldPause(CodexPlanReadError error) =>
        error is CodexPlanReadError.Auth or CodexPlanReadError.RateLimit;
}
