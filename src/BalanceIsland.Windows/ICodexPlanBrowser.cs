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

public sealed class DispatcherCodexPlanTimer : ICodexPlanTimer
{
    private readonly System.Windows.Threading.Dispatcher _dispatcher;
    private System.Windows.Threading.DispatcherTimer? _timer;
    private Func<Task>? _callback;

    public DispatcherCodexPlanTimer(System.Windows.Threading.Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public bool IsScheduled { get; private set; }

    public void Schedule(TimeSpan delay, Func<Task> callback)
    {
        Cancel();
        _callback = callback;
        _timer = new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(
            Math.Max(1, delay.TotalMilliseconds)), System.Windows.Threading.DispatcherPriority.Background,
            (_, _) => RunCallback(), _dispatcher);
        IsScheduled = true;
    }

    public void Cancel()
    {
        _timer?.Stop();
        _timer = null;
        IsScheduled = false;
    }

    private void RunCallback()
    {
        IsScheduled = false;
        var callback = _callback;
        _callback = null;
        if (callback is not null) _ = callback();
    }
}
