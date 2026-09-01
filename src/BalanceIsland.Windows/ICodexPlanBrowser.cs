namespace BalanceIsland.Windows;

public sealed record CodexPlanBrowserResult(int StatusCode, string BodyJson);

public enum CodexPlanRefreshOutcome
{
    Success,
    InFlight,
    TooSoon,
    Paused,
    NotReady,
    Failed
}

public sealed record CodexPlanRefreshResult(CodexPlanRefreshOutcome Outcome, TimeSpan? RetryAfter);

public interface ICodexPlanBrowser : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken);
    bool IsOnTrustedUsageOrigin { get; }
    Task<CodexPlanBrowserResult> ReadFilteredUsageAsync(CancellationToken cancellationToken);
    Task ClearProfileAsync(CancellationToken cancellationToken);
}

public interface ICodexPlanTimer
{
    void Schedule(TimeSpan delay, Func<Task> callback);
    void Cancel();
    bool IsScheduled { get; }
}
