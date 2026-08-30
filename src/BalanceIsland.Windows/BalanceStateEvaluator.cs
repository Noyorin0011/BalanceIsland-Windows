namespace BalanceIsland.Windows;

public sealed record BalanceEvaluation(
    BalanceVisualState VisualState,
    IReadOnlyList<BalanceAlertKind> EnteredAlerts,
    BalanceAlertState NextState);

public static class BalanceStateEvaluator
{
    private const double WarningBandMultiplier = 1.15d;

    public static BalanceEvaluation Evaluate(
        Account account,
        BalanceSnapshot snapshot,
        BalanceAlertState previous,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(previous);

        var next = Copy(previous);
        if (snapshot.Status == SnapshotStatus.Error || snapshot.BalanceAmount is not { } amount)
        {
            next.LastVisualState = BalanceVisualState.Normal;
            return new BalanceEvaluation(BalanceVisualState.Normal, [], next);
        }

        var balanceBand = ClassifyBalanceBand(amount, Math.Max(0d, account.WarningLine));
        var anomaly = IsAnomaly(account, previous, amount);
        var visualState = balanceBand == BalanceVisualState.Critical
            ? BalanceVisualState.Critical
            : anomaly
                ? BalanceVisualState.Anomaly
                : balanceBand;

        var enteredAlerts = new List<BalanceAlertKind>();
        if (account.AlertEnabled)
        {
            if (balanceBand == BalanceVisualState.Warning15 &&
                previous.LastBalanceBand != BalanceVisualState.Warning15)
                enteredAlerts.Add(BalanceAlertKind.Warning15);
            else if (balanceBand == BalanceVisualState.Critical &&
                     previous.LastBalanceBand != BalanceVisualState.Critical)
                enteredAlerts.Add(BalanceAlertKind.Critical);

            if (anomaly && !IsCoolingDown(account, previous, now))
            {
                enteredAlerts.Add(BalanceAlertKind.Anomaly);
                next.LastAnomalyAt = now;
            }
        }

        next.LastBalanceBand = balanceBand;
        next.LastLevel = balanceBand switch
        {
            BalanceVisualState.Critical => 2,
            BalanceVisualState.Warning15 => 1,
            _ => 0
        };
        next.LastVisualState = visualState;
        next.LastSeenAmount = amount;
        return new BalanceEvaluation(visualState, enteredAlerts, next);
    }

    private static BalanceVisualState ClassifyBalanceBand(double amount, double warningLine) =>
        amount <= warningLine
            ? BalanceVisualState.Critical
            : amount <= warningLine * WarningBandMultiplier
                ? BalanceVisualState.Warning15
                : BalanceVisualState.Normal;

    private static bool IsAnomaly(Account account, BalanceAlertState previous, double amount)
    {
        if (!account.AnomalyEnabled || previous.LastSeenAmount is not { } last || amount == last)
            return false;

        var change = Math.Abs(amount - last);
        var absolute = change >= Math.Max(0d, account.AnomalyThreshold);
        var percent = last > 0d
            ? change >= last * Math.Max(0d, account.AnomalyPercentThreshold) / 100d
            : absolute;
        return account.AnomalyMode switch
        {
            AnomalyMode.Absolute => absolute,
            AnomalyMode.Percent => percent,
            _ => absolute || percent
        };
    }

    private static bool IsCoolingDown(Account account, BalanceAlertState previous, DateTimeOffset now) =>
        previous.LastAnomalyAt is { } lastAt &&
        now - lastAt < TimeSpan.FromMinutes(Math.Max(0, account.AnomalyCooldownMinutes));

    private static BalanceAlertState Copy(BalanceAlertState state) => new()
    {
        LastNotifiedAmount = state.LastNotifiedAmount,
        LastLevel = state.LastLevel,
        LastBalanceBand = state.LastBalanceBand,
        LastVisualState = state.LastVisualState,
        LastSeenAmount = state.LastSeenAmount,
        LastAnomalyAt = state.LastAnomalyAt
    };
}
