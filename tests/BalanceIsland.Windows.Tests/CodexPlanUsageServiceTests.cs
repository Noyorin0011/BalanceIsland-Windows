using Xunit;
using BalanceIsland.Windows;

namespace BalanceIsland.Windows.Tests;

public sealed class CodexPlanUsageServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-01T12:00:00Z");

    private static CodexPlanUsage Usage(int remainingPercent) => new()
    {
        PlanType = "plus",
        Primary = new CodexPlanQuotaWindow { RemainingPercent = remainingPercent, WindowSeconds = 18_000 },
        UpdatedAt = Now
    };

    private static (CodexPlanUsageService Service, FakeCodexPlanBrowser Browser, FakeClock Clock, FakeCodexPlanTimer Timer, BalanceCoordinator Coordinator) CreateService()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BalanceIsland.Tests", Guid.NewGuid().ToString("N"));
        var store = new AppDataStore(directory);
        var coordinator = new BalanceCoordinator(store, new WindowsCredentialStore(), new ProviderClient());
        coordinator.SetCodexPlanConsent(true);
        coordinator.SetCodexPlanSettings(true, true, true);
        var browser = new FakeCodexPlanBrowser();
        var clock = new FakeClock(Now);
        var timer = new FakeCodexPlanTimer();
        var service = new CodexPlanUsageService(coordinator, browser, clock, timer);
        return (service, browser, clock, timer, coordinator);
    }

    [Fact]
    public async Task Concurrent_refreshes_issue_one_browser_request()
    {
        var (service, browser, _, _, _) = CreateService();
        await using (service)
        {
            var first = service.RefreshAsync(manual: true, CancellationToken.None);
            var second = service.RefreshAsync(manual: true, CancellationToken.None);
            browser.CompleteSuccess(Usage(82));
            await Task.WhenAll(first, second);
            Assert.Equal(1, browser.ReadCount);
        }
    }

    [Fact]
    public async Task Manual_refresh_before_five_minutes_does_not_hit_browser()
    {
        var (service, browser, clock, _, _) = CreateService();
        await using (service)
        {
            await service.RefreshAsync(true, CancellationToken.None);
            browser.CompleteSuccess(Usage(82));
            await Task.Delay(10);
            clock.Advance(TimeSpan.FromMinutes(4).Add(TimeSpan.FromSeconds(59)));
            var result = await service.RefreshAsync(true, CancellationToken.None);
            Assert.Equal(CodexPlanRefreshOutcome.TooSoon, result.Outcome);
            Assert.Equal(1, browser.ReadCount);
        }
    }

    [Fact]
    public async Task Rate_limit_pauses_scheduler_and_keeps_snapshot()
    {
        var (service, browser, _, timer, coordinator) = CreateService();
        await using (service)
        {
            await service.RefreshAsync(true, CancellationToken.None);
            browser.CompleteSuccess(Usage(82));
            await Task.Delay(10);
            browser.EnqueueFailure(429);
            await service.RefreshAsync(true, CancellationToken.None);
            Assert.Equal(82, coordinator.State.CodexPlanUsage!.Primary!.RemainingPercent);
            Assert.True(coordinator.State.CodexPlanReadState.AutoRefreshPaused);
            Assert.False(timer.IsScheduled);
        }
    }

    [Fact]
    public async Task Disconnect_invalidates_in_flight_result()
    {
        var (service, browser, _, _, coordinator) = CreateService();
        await using (service)
        {
            var read = service.RefreshAsync(true, CancellationToken.None);
            await service.DisconnectAsync(CancellationToken.None);
            browser.CompleteSuccess(Usage(99));
            await read;
            Assert.Null(coordinator.State.CodexPlanUsage);
        }
    }

    [Fact]
    public async Task First_read_succeeds_and_persists_snapshot()
    {
        var (service, browser, _, _, coordinator) = CreateService();
        await using (service)
        {
            var result = await service.RefreshAsync(true, CancellationToken.None);
            Assert.Equal(CodexPlanRefreshOutcome.Success, result.Outcome);
            Assert.Equal(82, coordinator.State.CodexPlanUsage!.Primary!.RemainingPercent);
        }
    }

    [Fact]
    public async Task Browser_initialization_failure_maps_to_runtime_error()
    {
        var (service, browser, _, _, coordinator) = CreateService();
        browser.FailInitialize = true;
        await using (service)
        {
            var result = await service.RefreshAsync(true, CancellationToken.None);
            Assert.Equal(CodexPlanRefreshOutcome.Failed, result.Outcome);
            Assert.Equal(CodexPlanReadError.Runtime, coordinator.State.CodexPlanReadState.LastError);
        }
    }

    [Fact]
    public async Task Untrusted_origin_does_not_consume_the_five_minute_attempt()
    {
        var (service, browser, _, _, coordinator) = CreateService();
        browser.OnTrustedOrigin = false;
        await using (service)
        {
            var result = await service.RefreshAsync(true, CancellationToken.None);
            Assert.Equal(CodexPlanRefreshOutcome.NotReady, result.Outcome);
            Assert.Null(coordinator.State.CodexPlanReadState.LastAttemptAt);
        }
    }
}

internal sealed class FakeCodexPlanBrowser : ICodexPlanBrowser
{
    private readonly TaskCompletionSource<CodexPlanBrowserResult> _pending = new();
    private readonly Queue<int> _failures = new();
    public int ReadCount { get; private set; }
    public bool FailInitialize { get; set; }
    public bool OnTrustedOrigin { get; set; } = true;

    public void CompleteSuccess(CodexPlanUsage usage)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { plan_type = "plus", rate_limit = new { primary_window = new { used_percent = 18, limit_window_seconds = 18000 } } });
        _pending.TrySetResult(new CodexPlanBrowserResult(200, json));
    }

    public void EnqueueFailure(int statusCode) => _failures.Enqueue(statusCode);

    public Task InitializeAsync(CancellationToken cancellationToken) =>
        FailInitialize
            ? Task.FromException(new InvalidOperationException("browser init failed"))
            : Task.CompletedTask;

    public bool IsOnTrustedUsageOrigin => OnTrustedOrigin;

    public Task<CodexPlanBrowserResult> ReadFilteredUsageAsync(CancellationToken cancellationToken)
    {
        ReadCount++;
        if (_failures.TryDequeue(out var statusCode))
            return Task.FromResult(new CodexPlanBrowserResult(statusCode, "{}"));
        return _pending.Task;
    }

    public Task ClearProfileAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now;
    public FakeClock(DateTimeOffset now) => _now = now;
    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    public override DateTimeOffset GetUtcNow() => _now;
}

internal sealed class FakeCodexPlanTimer : ICodexPlanTimer
{
    public bool IsScheduled { get; private set; }
    public TimeSpan? Delay { get; private set; }
    public void Schedule(TimeSpan delay, Func<Task> callback)
    {
        Delay = delay;
        IsScheduled = true;
        _ = callback();
    }
    public void Cancel() => IsScheduled = false;
}
