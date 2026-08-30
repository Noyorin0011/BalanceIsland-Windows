namespace BalanceIsland.Windows;

public static class AppStateNormalizer
{
    public static AppState Normalize(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.Accounts ??= [];
        state.Snapshots ??= [];
        state.Schedules ??= [];
        state.DailyUsage ??= [];
        state.Alerts ??= [];
        state.DisplayGroups ??= [];

        RemoveNullAndOrphanDictionaryEntries(state);
        NormalizeNullableStrings(state);
        NormalizePersistedKeySuffixes(state);

        if (!Enum.IsDefined(state.ThemeMode)) state.ThemeMode = AppThemeMode.System;
        if (!Enum.IsDefined(state.IslandColorTheme)) state.IslandColorTheme = IslandColorTheme.Classic;
        state.CustomNormalColor = NormalizeColorOrClassic(state.CustomNormalColor, IslandColorPalettes.Classic.Normal);
        state.CustomAnomalyColor = NormalizeColorOrClassic(state.CustomAnomalyColor, IslandColorPalettes.Classic.Anomaly);
        state.CustomWarning15Color = NormalizeColorOrClassic(state.CustomWarning15Color, IslandColorPalettes.Classic.Warning15);
        state.CustomCriticalColor = NormalizeColorOrClassic(state.CustomCriticalColor, IslandColorPalettes.Classic.Critical);
        NormalizePersistedBalanceBands(state);

        var accountIds = state.Accounts
            .Select(account => account.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        var seenGroupIds = new HashSet<string>(StringComparer.Ordinal);
        var groups = new List<IslandDisplayGroup>();

        foreach (var group in state.DisplayGroups)
        {
            if (group is null) continue;

            var id = group.Id?.Trim();
            if (string.IsNullOrWhiteSpace(id)) id = Guid.NewGuid().ToString("N");
            if (!seenGroupIds.Add(id)) continue;

            group.Id = id;
            group.Name = string.IsNullOrWhiteSpace(group.Name) ? "新分组" : group.Name.Trim();
            if (!Enum.IsDefined(group.Mode)) group.Mode = IslandGroupMode.Rotation;
            if (group.AggregateProvider is { } provider && !Enum.IsDefined(provider))
                group.AggregateProvider = null;
            group.AccountIds ??= [];
            group.AccountIds = group.AccountIds
                .Where(id => !string.IsNullOrWhiteSpace(id) && accountIds.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            groups.Add(group);
        }

        state.DisplayGroups = groups;
        var activeId = state.ActiveDisplayGroupId?.Trim();
        state.ActiveDisplayGroupId = !string.IsNullOrWhiteSpace(activeId) && seenGroupIds.Contains(activeId)
            ? activeId
            : null;
        return state;
    }

    private static string NormalizeColorOrClassic(string? color, string classic)
    {
        return IslandColorPalettes.TryNormalizeColor(color, out var normalized) ? normalized : classic;
    }

    private static void NormalizePersistedKeySuffixes(AppState state)
    {
        if (state.SafeKeySuffixVersion != 1)
        {
            foreach (var account in state.Accounts.OfType<Account>())
                account.KeySuffix = "";
            foreach (var snapshot in state.Snapshots.Values.OfType<BalanceSnapshot>())
                snapshot.KeySuffix = "";
            state.SafeKeySuffixVersion = 1;
            return;
        }

        foreach (var account in state.Accounts.OfType<Account>())
            account.KeySuffix = account.KeySuffix?.Length == 4 ? account.KeySuffix : "";
        foreach (var snapshot in state.Snapshots.Values.OfType<BalanceSnapshot>())
            snapshot.KeySuffix = snapshot.KeySuffix?.Length == 4 ? snapshot.KeySuffix : "";
    }

    private static void RemoveNullAndOrphanDictionaryEntries(AppState state)
    {
        var accountIds = state.Accounts
            .OfType<Account>()
            .Where(account => !string.IsNullOrWhiteSpace(account.Id))
            .Select(account => account.Id)
            .ToHashSet(StringComparer.Ordinal);

        state.Snapshots = state.Snapshots
            .Where(entry => accountIds.Contains(entry.Key) && entry.Value is not null)
            .ToDictionary(entry => entry.Key, entry => entry.Value!, StringComparer.Ordinal);
        state.Schedules = state.Schedules
            .Where(entry => accountIds.Contains(entry.Key) && entry.Value is not null)
            .ToDictionary(entry => entry.Key, entry => entry.Value!, StringComparer.Ordinal);
        state.DailyUsage = state.DailyUsage
            .Where(entry => accountIds.Contains(entry.Key) && entry.Value is not null)
            .ToDictionary(entry => entry.Key, entry => entry.Value!, StringComparer.Ordinal);
        state.Alerts = state.Alerts
            .Where(entry => accountIds.Contains(entry.Key) && entry.Value is not null)
            .ToDictionary(entry => entry.Key, entry => entry.Value!, StringComparer.Ordinal);
    }

    private static void NormalizeNullableStrings(AppState state)
    {
        foreach (var account in state.Accounts.OfType<Account>())
        {
            account.Label ??= "";
            account.KeySuffix ??= "";
        }

        foreach (var entry in state.Snapshots)
        {
            entry.Value.CredentialId = entry.Key;
            entry.Value.AccountLabel ??= "";
            entry.Value.KeySuffix ??= "";
            entry.Value.PrimaryText ??= "";
            entry.Value.SecondaryText ??= "";
            if (string.IsNullOrWhiteSpace(entry.Value.CurrencyCode))
                entry.Value.CurrencyCode = entry.Value.Provider.DefaultCurrency();
        }
    }

    private static void NormalizePersistedBalanceBands(AppState state)
    {
        foreach (var account in state.Accounts)
        {
            if (string.IsNullOrWhiteSpace(account.Id) ||
                !state.Alerts.TryGetValue(account.Id, out var alert) ||
                alert is null ||
                !state.Snapshots.TryGetValue(account.Id, out var snapshot) ||
                snapshot is null ||
                snapshot.Status == SnapshotStatus.Error ||
                (account.ManualBalance ?? snapshot.BalanceAmount) is not { } amount) continue;

            var warningLine = Math.Max(0d, account.WarningLine);
            alert.LastBalanceBand = amount <= warningLine
                ? BalanceVisualState.Critical
                : amount <= warningLine * 1.15d
                    ? BalanceVisualState.Warning15
                    : BalanceVisualState.Normal;
            alert.LastLevel = alert.LastBalanceBand switch
            {
                BalanceVisualState.Critical => 2,
                BalanceVisualState.Warning15 => 1,
                _ => 0
            };
            // Persisted versions before v0.3 did not retain a transient visual state.
            // Preserve a real saved Anomaly; only infer legacy/default Normal values.
            if (alert.LastVisualState == BalanceVisualState.Normal &&
                alert.LastBalanceBand != BalanceVisualState.Normal)
                alert.LastVisualState = alert.LastBalanceBand;
        }
    }
}
