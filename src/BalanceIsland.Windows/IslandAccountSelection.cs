namespace BalanceIsland.Windows;

public static class IslandAccountSelection
{
    public static IReadOnlyList<BalanceSnapshot> VisibleSnapshots(BalanceCoordinator coordinator)
    {
        var visibleIds = coordinator.State.Accounts
            .Where(account => account.ShowInIsland)
            .Select(account => account.Id)
            .ToHashSet(StringComparer.Ordinal);
        return coordinator.CurrentSnapshots
            .Where(snapshot => visibleIds.Contains(snapshot.CredentialId))
            .ToArray();
    }

    public static void SetVisible(BalanceCoordinator coordinator, string accountId, bool visible)
    {
        var account = coordinator.State.Accounts.FirstOrDefault(item => item.Id == accountId)
            ?? throw new ArgumentException("账户不存在", nameof(accountId));
        if (account.ShowInIsland == visible) return;
        account.ShowInIsland = visible;

        // Reuse the coordinator's normal persistence + StateChanged path without exposing
        // an additional mutable store API. SetIslandEnabled is intentionally idempotent.
        coordinator.SetIslandEnabled(coordinator.State.IslandEnabled);
    }
}
