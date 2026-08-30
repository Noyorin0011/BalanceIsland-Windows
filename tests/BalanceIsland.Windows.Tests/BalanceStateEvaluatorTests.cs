using Xunit;
using BalanceIsland.Windows;

namespace BalanceIsland.Windows.Tests;

public sealed class BalanceStateEvaluatorTests
{
    [Theory]
    [InlineData(23.01, BalanceVisualState.Normal)]
    [InlineData(23.00, BalanceVisualState.Warning15)]
    [InlineData(20.01, BalanceVisualState.Warning15)]
    [InlineData(20.00, BalanceVisualState.Critical)]
    public void Warning_band_uses_exact_fifteen_percent_boundary(double balance, BalanceVisualState expected)
    {
        var result = Evaluate(balance, warningLine: 20, anomaly: false);

        Assert.Equal(expected, result.VisualState);
    }

    [Fact]
    public void Critical_has_priority_over_anomaly_and_warning()
    {
        var result = Evaluate(balance: 19, warningLine: 20, anomaly: true);

        Assert.Equal(BalanceVisualState.Critical, result.VisualState);
    }

    [Fact]
    public void Anomaly_has_priority_over_warning()
    {
        var result = Evaluate(balance: 22, warningLine: 20, anomaly: true);

        Assert.Equal(BalanceVisualState.Anomaly, result.VisualState);
        Assert.Equal(BalanceVisualState.Anomaly, result.NextState.LastVisualState);
    }

    [Fact]
    public void Warning_notifies_on_entry_not_while_remaining_and_again_after_recovery()
    {
        var state = new BalanceAlertState();
        var first = Evaluate(22, 20, false, state);
        Assert.Equal([BalanceAlertKind.Warning15], first.EnteredAlerts);

        var still = Evaluate(21, 20, false, first.NextState);
        Assert.Empty(still.EnteredAlerts);

        var recovered = Evaluate(30, 20, false, still.NextState);
        var reentered = Evaluate(22, 20, false, recovered.NextState);
        Assert.Equal([BalanceAlertKind.Warning15], reentered.EnteredAlerts);
    }

    [Fact]
    public void Moving_from_warning_to_critical_notifies_critical()
    {
        var warning = Evaluate(22, 20, false, new BalanceAlertState());
        var critical = Evaluate(20, 20, false, warning.NextState);

        Assert.Equal([BalanceAlertKind.Critical], critical.EnteredAlerts);
    }

    [Fact]
    public void Remaining_critical_does_not_repeat_after_an_obsolete_drop_step()
    {
        var account = new Account
        {
            AlertEnabled = true,
            WarningLine = 20,
            DropStep = 0.01
        };
        var first = BalanceStateEvaluator.Evaluate(
            account,
            new BalanceSnapshot { BalanceAmount = 20, Status = SnapshotStatus.Ok },
            new BalanceAlertState(),
            DateTimeOffset.Parse("2026-08-28T00:00:00Z"));

        var lower = BalanceStateEvaluator.Evaluate(
            account,
            new BalanceSnapshot { BalanceAmount = 10, Status = SnapshotStatus.Ok },
            first.NextState,
            DateTimeOffset.Parse("2026-08-28T00:05:00Z"));

        Assert.Empty(lower.EnteredAlerts);
    }

    [Fact]
    public void Anomaly_respects_its_cooldown_but_keeps_the_balance_band_independent()
    {
        var state = new BalanceAlertState { LastSeenAmount = 30 };
        var first = Evaluate(22, 20, true, state, DateTimeOffset.Parse("2026-08-28T00:00:00Z"));
        Assert.Equal(BalanceVisualState.Anomaly, first.VisualState);
        Assert.Equal([BalanceAlertKind.Warning15, BalanceAlertKind.Anomaly], first.EnteredAlerts);

        var cooldown = Evaluate(18, 20, true, first.NextState, DateTimeOffset.Parse("2026-08-28T00:30:00Z"));
        Assert.Equal(BalanceVisualState.Critical, cooldown.VisualState);
        Assert.Equal([BalanceAlertKind.Critical], cooldown.EnteredAlerts);

        var afterCooldown = Evaluate(25, 20, true, cooldown.NextState, DateTimeOffset.Parse("2026-08-28T01:01:00Z"));
        Assert.Equal([BalanceAlertKind.Anomaly], afterCooldown.EnteredAlerts);
    }

    [Fact]
    public void Missing_amount_or_error_does_not_emit_balance_alerts_or_mutate_state()
    {
        var previous = new BalanceAlertState
        {
            LastBalanceBand = BalanceVisualState.Warning15,
            LastSeenAmount = 22,
            LastAnomalyAt = DateTimeOffset.Parse("2026-08-27T00:00:00Z")
        };
        var account = new Account { AlertEnabled = true, WarningLine = 20, AnomalyEnabled = true };

        var missing = BalanceStateEvaluator.Evaluate(
            account,
            new BalanceSnapshot { Status = SnapshotStatus.Ok },
            previous,
            DateTimeOffset.Parse("2026-08-28T00:00:00Z"));
        var error = BalanceStateEvaluator.Evaluate(
            account,
            new BalanceSnapshot { BalanceAmount = 10, Status = SnapshotStatus.Error },
            previous,
            DateTimeOffset.Parse("2026-08-28T00:00:00Z"));

        Assert.Empty(missing.EnteredAlerts);
        Assert.Equal(BalanceVisualState.Normal, missing.VisualState);
        Assert.Equal(previous.LastBalanceBand, missing.NextState.LastBalanceBand);
        Assert.Empty(error.EnteredAlerts);
        Assert.Equal(BalanceVisualState.Normal, error.VisualState);
        Assert.Equal(previous.LastSeenAmount, error.NextState.LastSeenAmount);
    }

    [Fact]
    public void Evaluation_returns_a_copy_of_the_alert_state()
    {
        var previous = new BalanceAlertState { LastSeenAmount = 30 };

        var result = Evaluate(22, 20, true, previous);

        Assert.NotSame(previous, result.NextState);
        Assert.Equal(30, previous.LastSeenAmount);
    }

    [Fact]
    public void Disabled_account_keeps_classification_without_emitting_alerts()
    {
        var account = new Account { AlertEnabled = false, WarningLine = 20 };
        var snapshot = new BalanceSnapshot { BalanceAmount = 20, Status = SnapshotStatus.Ok };

        var result = BalanceStateEvaluator.Evaluate(account, snapshot, new BalanceAlertState(), DateTimeOffset.UtcNow);

        Assert.Equal(BalanceVisualState.Critical, result.VisualState);
        Assert.Empty(result.EnteredAlerts);
        Assert.Equal(BalanceVisualState.Critical, result.NextState.LastBalanceBand);
    }

    [Fact]
    public void Normalizer_migrates_persisted_alert_band_using_the_fifteen_percent_boundary()
    {
        var state = new AppState
        {
            Accounts = [new() { Id = "account", WarningLine = 20 }],
            Snapshots = new Dictionary<string, BalanceSnapshot>
            {
                ["account"] = new() { BalanceAmount = 23, Status = SnapshotStatus.Ok }
            },
            Alerts = new Dictionary<string, BalanceAlertState>
            {
                ["account"] = new() { LastLevel = 1 }
            }
        };

        AppStateNormalizer.Normalize(state);

        Assert.Equal(BalanceVisualState.Warning15, state.Alerts["account"].LastBalanceBand);
    }

    [Theory]
    [InlineData(23d, BalanceVisualState.Warning15, 1)]
    [InlineData(20d, BalanceVisualState.Critical, 2)]
    public void Normalizer_migrates_legacy_band_from_manual_balance_before_snapshot(
        double manualBalance,
        BalanceVisualState expectedBand,
        int expectedLevel)
    {
        var state = new AppState
        {
            Accounts =
            [
                new()
                {
                    Id = "account",
                    WarningLine = 20,
                    ManualBalance = manualBalance
                }
            ],
            Snapshots = new Dictionary<string, BalanceSnapshot>
            {
                ["account"] = new() { BalanceAmount = 100, Status = SnapshotStatus.Ok }
            },
            Alerts = new Dictionary<string, BalanceAlertState>
            {
                ["account"] = new() { LastLevel = 0 }
            }
        };

        AppStateNormalizer.Normalize(state);

        Assert.Equal(expectedBand, state.Alerts["account"].LastBalanceBand);
        Assert.Equal(expectedLevel, state.Alerts["account"].LastLevel);
    }

    private static BalanceEvaluation Evaluate(
        double balance,
        double warningLine,
        bool anomaly,
        BalanceAlertState? previous = null,
        DateTimeOffset? now = null)
    {
        var account = new Account
        {
            WarningLine = warningLine,
            AlertEnabled = true,
            AnomalyEnabled = anomaly,
            AnomalyThreshold = 1,
            AnomalyPercentThreshold = 1,
            AnomalyMode = AnomalyMode.Both,
            AnomalyCooldownMinutes = 60
        };
        previous ??= new BalanceAlertState();
        if (anomaly && previous.LastSeenAmount is null) previous.LastSeenAmount = balance + 10;
        var timestamp = now ?? DateTimeOffset.Parse("2026-08-28T00:00:00Z");
        var snapshot = new BalanceSnapshot
        {
            BalanceAmount = balance,
            Status = SnapshotStatus.Ok,
            UpdatedAt = timestamp
        };
        return BalanceStateEvaluator.Evaluate(account, snapshot, previous, timestamp);
    }
}
