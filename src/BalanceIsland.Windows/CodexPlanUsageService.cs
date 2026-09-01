using System.Text.Json;

namespace BalanceIsland.Windows;

public sealed class CodexPlanUsageService : IAsyncDisposable
{
    private readonly BalanceCoordinator _coordinator;
    private readonly ICodexPlanBrowser _browser;
    private readonly TimeProvider _clock;
    private readonly ICodexPlanTimer _timer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private long _generation;
    private bool _disposed;

    public CodexPlanUsageService(
        BalanceCoordinator coordinator,
        ICodexPlanBrowser browser,
        TimeProvider clock,
        ICodexPlanTimer timer)
    {
        _coordinator = coordinator;
        _browser = browser;
        _clock = clock;
        _timer = timer;
    }

    public void Start()
    {
        if (!CanRun(out _)) return;
        ScheduleNext();
    }

    public async Task<CodexPlanRefreshResult> RefreshAsync(bool manual, CancellationToken cancellationToken)
    {
        if (_disposed) return new CodexPlanRefreshResult(CodexPlanRefreshOutcome.Failed, null);
        if (!CanRun(out var reason))
            return new CodexPlanRefreshResult(reason, null);
        if (!manual && _coordinator.State.CodexPlanReadState.AutoRefreshPaused)
            return new CodexPlanRefreshResult(CodexPlanRefreshOutcome.Paused, null);

        var now = _clock.GetUtcNow();
        var lastAttempt = _coordinator.State.CodexPlanReadState.LastAttemptAt;
        if (manual && !CodexPlanRefreshPolicy.CanAttempt(now, lastAttempt))
            return new CodexPlanRefreshResult(
                CodexPlanRefreshOutcome.TooSoon,
                CodexPlanRefreshPolicy.NextDelay(now, lastAttempt));

        if (!await _gate.WaitAsync(0, cancellationToken))
            return new CodexPlanRefreshResult(CodexPlanRefreshOutcome.InFlight, null);
        try
        {
            var generation = _generation;
            try
            {
                await _browser.InitializeAsync(cancellationToken);
            }
            catch (Exception)
            {
                _coordinator.MarkCodexPlanFailure(CodexPlanReadError.Runtime);
                return new CodexPlanRefreshResult(CodexPlanRefreshOutcome.Failed, null);
            }
            if (!_browser.IsOnTrustedUsageOrigin)
                return new CodexPlanRefreshResult(CodexPlanRefreshOutcome.NotReady, null);

            _coordinator.MarkCodexPlanAttempt(now);
            CodexPlanBrowserResult result;
            try
            {
                result = await _browser.ReadFilteredUsageAsync(cancellationToken);
            }
            catch (Exception)
            {
                _coordinator.MarkCodexPlanFailure(CodexPlanReadError.Runtime);
                return new CodexPlanRefreshResult(CodexPlanRefreshOutcome.Failed, null);
            }
            if (generation != _generation)
                return new CodexPlanRefreshResult(CodexPlanRefreshOutcome.Failed, null);

            if (result.StatusCode is 401 or 403)
            {
                _coordinator.MarkCodexPlanFailure(CodexPlanReadError.Auth);
                return new CodexPlanRefreshResult(CodexPlanRefreshOutcome.Failed, null);
            }
            if (result.StatusCode == 429)
            {
                _coordinator.MarkCodexPlanFailure(CodexPlanReadError.RateLimit);
                _timer.Cancel();
                return new CodexPlanRefreshResult(CodexPlanRefreshOutcome.Paused, null);
            }
            if (result.StatusCode < 200 || result.StatusCode >= 300)
            {
                _coordinator.MarkCodexPlanFailure(CodexPlanReadError.Http);
                return new CodexPlanRefreshResult(CodexPlanRefreshOutcome.Failed, null);
            }

            try
            {
                var usage = CodexPlanUsageParser.Parse(result.BodyJson, _clock.GetUtcNow());
                _coordinator.SaveCodexPlanUsage(usage);
                if (!_coordinator.State.CodexPlanReadState.AutoRefreshPaused)
                    ScheduleNext();
                return new CodexPlanRefreshResult(CodexPlanRefreshOutcome.Success, null);
            }
            catch (FormatException)
            {
                _coordinator.MarkCodexPlanFailure(CodexPlanReadError.Parse);
                return new CodexPlanRefreshResult(CodexPlanRefreshOutcome.Failed, null);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CodexPlanRefreshResult> ResumeAndRefreshAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return new CodexPlanRefreshResult(CodexPlanRefreshOutcome.Failed, null);
        if (!_coordinator.State.CodexPlanEnabled)
            return new CodexPlanRefreshResult(CodexPlanRefreshOutcome.NotReady, null);
        var now = _clock.GetUtcNow();
        var lastAttempt = _coordinator.State.CodexPlanReadState.LastAttemptAt;
        if (!CodexPlanRefreshPolicy.CanAttempt(now, lastAttempt))
            return new CodexPlanRefreshResult(
                CodexPlanRefreshOutcome.TooSoon,
                CodexPlanRefreshPolicy.NextDelay(now, lastAttempt));

        _coordinator.State.CodexPlanReadState.AutoRefreshPaused = false;
        return await RefreshAsync(manual: true, cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _generation);
        _timer.Cancel();
        _coordinator.ClearCodexPlanData(profileCleanupPending: true);
        await _browser.ClearProfileAsync(cancellationToken);
    }

    private bool CanRun(out CodexPlanRefreshOutcome reason)
    {
        if (_coordinator.State.CodexPlanConsentVersion != 1)
        {
            reason = CodexPlanRefreshOutcome.NotReady;
            return false;
        }
        if (!_coordinator.State.CodexPlanEnabled)
        {
            reason = CodexPlanRefreshOutcome.NotReady;
            return false;
        }
        if (_coordinator.State.CodexPlanReadState.AutoRefreshPaused)
        {
            reason = CodexPlanRefreshOutcome.Paused;
            return false;
        }
        reason = CodexPlanRefreshOutcome.Success;
        return true;
    }

    private void ScheduleNext()
    {
        if (_disposed) return;
        var now = _clock.GetUtcNow();
        var lastAttempt = _coordinator.State.CodexPlanReadState.LastAttemptAt;
        var delay = CodexPlanRefreshPolicy.NextDelay(now, lastAttempt);
        _timer.Cancel();
        _timer.Schedule(delay, () => RefreshAsync(manual: false, CancellationToken.None));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Cancel();
        await _browser.DisposeAsync();
        _gate.Dispose();
    }
}
