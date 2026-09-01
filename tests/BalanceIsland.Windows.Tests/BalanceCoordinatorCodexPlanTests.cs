using Xunit;
using BalanceIsland.Windows;

namespace BalanceIsland.Windows.Tests;

public sealed class BalanceCoordinatorCodexPlanTests
{
    private static CodexPlanUsage Usage(int remainingPercent) => new()
    {
        PlanType = "plus",
        Primary = new CodexPlanQuotaWindow { RemainingPercent = remainingPercent, WindowSeconds = 18_000 },
        UpdatedAt = DateTimeOffset.Parse("2026-09-01T12:00:00Z")
    };

    private static BalanceCoordinator CreateCoordinator()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BalanceIsland.Tests", Guid.NewGuid().ToString("N"));
        var store = new AppDataStore(directory);
        return new BalanceCoordinator(store, new WindowsCredentialStore(), new ProviderClient());
    }

    private static BalanceCoordinator ConfiguredCoordinator()
    {
        var coordinator = CreateCoordinator();
        coordinator.SetCodexPlanConsent(true);
        coordinator.SetCodexPlanSettings(true, true, true);
        return coordinator;
    }

    [Fact]
    public void Rate_limit_failure_keeps_last_success_and_pauses_auto_refresh()
    {
        using var coordinator = CreateCoordinator();
        coordinator.SetCodexPlanConsent(true);
        coordinator.SetCodexPlanSettings(true, true, true);
        var usage = Usage(82);
        coordinator.SaveCodexPlanUsage(usage);
        coordinator.MarkCodexPlanFailure(CodexPlanReadError.RateLimit);
        Assert.Same(usage, coordinator.State.CodexPlanUsage);
        Assert.Equal(CodexPlanReadError.RateLimit, coordinator.State.CodexPlanReadState.LastError);
        Assert.True(coordinator.State.CodexPlanReadState.AutoRefreshPaused);
    }

    [Fact]
    public void Disconnect_clears_consent_snapshot_and_scheduler_state()
    {
        using var coordinator = ConfiguredCoordinator();
        coordinator.ClearCodexPlanData(profileCleanupPending: true);
        Assert.Equal(0, coordinator.State.CodexPlanConsentVersion);
        Assert.False(coordinator.State.CodexPlanEnabled);
        Assert.Null(coordinator.State.CodexPlanUsage);
        Assert.True(coordinator.State.CodexPlanProfileCleanupPending);
    }

    [Fact]
    public void Consent_accept_enables_feature_defaults()
    {
        using var coordinator = CreateCoordinator();
        coordinator.SetCodexPlanConsent(true);
        Assert.Equal(1, coordinator.State.CodexPlanConsentVersion);
    }

    [Fact]
    public void Consent_revoke_disables_feature_and_auto_refresh()
    {
        using var coordinator = ConfiguredCoordinator();
        coordinator.SetCodexPlanConsent(false);
        Assert.Equal(0, coordinator.State.CodexPlanConsentVersion);
        Assert.False(coordinator.State.CodexPlanEnabled);
        Assert.False(coordinator.State.CodexPlanAutoRefreshEnabled);
    }

    [Fact]
    public void Successful_save_clears_error_and_pause()
    {
        using var coordinator = ConfiguredCoordinator();
        coordinator.MarkCodexPlanFailure(CodexPlanReadError.RateLimit);
        coordinator.SaveCodexPlanUsage(Usage(90));
        Assert.Null(coordinator.State.CodexPlanReadState.LastError);
        Assert.False(coordinator.State.CodexPlanReadState.AutoRefreshPaused);
    }
}
