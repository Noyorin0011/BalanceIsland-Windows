namespace BalanceIsland.Windows;

public static class IslandAccountSelection
{
    public static IReadOnlyList<IslandDisplayItem> VisibleItems(BalanceCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        var snapshots = coordinator.CurrentSnapshots;
        var snapshotsById = snapshots.ToDictionary(snapshot => snapshot.CredentialId, StringComparer.Ordinal);
        var visualStates = coordinator.State.Alerts.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.LastVisualState,
            StringComparer.Ordinal);
        var activeGroup = coordinator.State.ActiveDisplayGroupId is { } activeGroupId
            ? coordinator.State.DisplayGroups.FirstOrDefault(group => group.Id == activeGroupId)
            : null;

        if (activeGroup is null)
        {
            var visibleIds = coordinator.State.Accounts
                .Where(account => account.IsEnabled && account.ShowInIsland)
                .Select(account => account.Id)
                .ToHashSet(StringComparer.Ordinal);
            return snapshots
                .Where(snapshot => visibleIds.Contains(snapshot.CredentialId))
                .Select(snapshot => IslandDisplayGroups.FromSnapshot(
                    snapshot,
                    visualStates.TryGetValue(snapshot.CredentialId, out var visualState)
                        ? visualState
                        : null))
                .ToArray();
        }

        var enabledIds = coordinator.State.Accounts
            .Where(account => account.IsEnabled)
            .Select(account => account.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (activeGroup.Mode == IslandGroupMode.Aggregate)
        {
            var enabledSnapshots = activeGroup.AccountIds
                .Where(enabledIds.Contains)
                .Where(snapshotsById.ContainsKey)
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(id => id, id => snapshotsById[id], StringComparer.Ordinal);
            return [IslandDisplayGroups.Aggregate(activeGroup, enabledSnapshots, visualStates)];
        }

        return activeGroup.AccountIds
            .Where(enabledIds.Contains)
            .Where(snapshotsById.ContainsKey)
            .Distinct(StringComparer.Ordinal)
            .Select(id => IslandDisplayGroups.FromSnapshot(
                snapshotsById[id],
                visualStates.TryGetValue(id, out var visualState) ? visualState : null))
            .ToArray();
    }

    public static IReadOnlyList<BalanceSnapshot> VisibleSnapshots(BalanceCoordinator coordinator)
    {
        var visibleIds = coordinator.State.Accounts
            .Where(account => account.IsEnabled && account.ShowInIsland)
            .Select(account => account.Id)
            .ToHashSet(StringComparer.Ordinal);
        return coordinator.CurrentSnapshots
            .Where(snapshot => visibleIds.Contains(snapshot.CredentialId))
            .ToArray();
    }

    public static void SetVisible(BalanceCoordinator coordinator, string accountId, bool visible)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        coordinator.SetAccountShowInIsland(accountId, visible);
    }
}
