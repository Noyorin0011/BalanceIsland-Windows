namespace BalanceIsland.Windows;

public static class DisplayGroupSelection
{
    public static string? Resolve(
        IEnumerable<IslandDisplayGroup> groups,
        string? currentId,
        string? preferredId,
        string? activeId)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var available = groups.ToArray();

        return Find(preferredId) ?? Find(currentId) ?? Find(activeId) ?? available.FirstOrDefault()?.Id;

        string? Find(string? id) => !string.IsNullOrWhiteSpace(id) &&
            available.Any(group => group.Id == id) ? id : null;
    }
}

public static class DisplayGroupEditorValidation
{
    public static bool HasMixedProviders(object? selectedValue, IEnumerable<Provider?> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        return selectedValue is IslandGroupMode.Aggregate && providers
            .Where(provider => provider is not null)
            .Distinct()
            .Skip(1)
            .Any();
    }
}

public static class IslandDisplayGroups
{
    public static IslandDisplayGroup Create(
        AppState state,
        string name,
        IslandGroupMode mode,
        IEnumerable<string> accountIds)
    {
        ArgumentNullException.ThrowIfNull(state);
        var group = BuildGroup(state, name, mode, accountIds);
        state.DisplayGroups.Add(group);
        return group;
    }

    public static IslandDisplayGroup Update(
        AppState state,
        string groupId,
        string name,
        IslandGroupMode mode,
        IEnumerable<string> accountIds)
    {
        ArgumentNullException.ThrowIfNull(state);
        var group = FindGroup(state, groupId);
        var validated = BuildGroup(state, name, mode, accountIds);

        group.Name = validated.Name;
        group.Mode = validated.Mode;
        group.AggregateProvider = validated.AggregateProvider;
        group.AccountIds = validated.AccountIds;
        return group;
    }

    public static void Delete(AppState state, string groupId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(groupId)) return;

        state.DisplayGroups.RemoveAll(group => group.Id == groupId);
        if (state.ActiveDisplayGroupId == groupId)
            state.ActiveDisplayGroupId = null;
    }

    public static void SetActive(AppState state, string? groupId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(groupId))
        {
            state.ActiveDisplayGroupId = null;
            return;
        }

        _ = FindGroup(state, groupId);
        state.ActiveDisplayGroupId = groupId;
    }

    public static void RemoveAccount(AppState state, string accountId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(accountId)) return;

        foreach (var group in state.DisplayGroups)
            group.AccountIds.RemoveAll(id => id == accountId);
    }

    public static IslandDisplayItem Aggregate(
        IslandDisplayGroup group,
        IReadOnlyDictionary<string, BalanceSnapshot> snapshots,
        IReadOnlyDictionary<string, BalanceVisualState>? visualStates = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(snapshots);

        var items = group.AccountIds
            .Distinct(StringComparer.Ordinal)
            .Where(snapshots.ContainsKey)
            .Select(id => snapshots[id])
            .ToArray();
        var provider = group.AggregateProvider ?? items.FirstOrDefault()?.Provider;
        var visualState = MaximumVisualState(items, visualStates);
        var hasProviderMismatch = items.Select(item => item.Provider).Distinct().Skip(1).Any() ||
            group.AggregateProvider is { } configuredProvider &&
            items.Any(item => item.Provider != configuredProvider);
        if (hasProviderMismatch)
        {
            return DisplayItem(
                provider,
                group.Name,
                "无法汇总",
                "Provider 不一致，无法汇总",
                null,
                null,
                "",
                visualState);
        }

        var monetaryItems = items
            .Where(item => item.BalanceAmount is not null || item.TodayUsedAmount is not null)
            .ToArray();

        if (monetaryItems.Length == 0)
        {
            var healthy = items.Count(item => item.Status is not SnapshotStatus.Error and not SnapshotStatus.NotConfigured);
            return DisplayItem(
                provider,
                group.Name,
                $"有效 {healthy} 个 Key · 错误 {items.Length - healthy} 个 Key",
                "",
                null,
                null,
                provider is { } fallbackProvider ? fallbackProvider.DefaultCurrency() : "USD",
                visualState);
        }

        var currencies = monetaryItems
            .Select(item => item.CurrencyCode.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (currencies.Length != 1)
        {
            return DisplayItem(
                provider,
                group.Name,
                "无法汇总",
                "币种不一致，无法汇总",
                null,
                null,
                "",
                visualState);
        }

        var currency = currencies[0];
        var balanceItems = monetaryItems.Where(item => item.BalanceAmount is not null).ToArray();
        var balance = balanceItems.Length == 0 ? (double?)null : balanceItems.Sum(item => item.BalanceAmount!.Value);
        var usageItems = monetaryItems.Where(item => item.TodayUsedAmount is not null).ToArray();
        var usage = usageItems.Length == 0 ? (double?)null : usageItems.Sum(item => item.TodayUsedAmount!.Value);
        var secondary = usage is null
            ? ""
            : $"今日 {BalanceSnapshot.CurrencySymbol(currency)}{usage:0.00}";
        return DisplayItem(
            provider,
            group.Name,
            balance is null
                ? $"有效 {items.Count(item => item.Status is not SnapshotStatus.Error and not SnapshotStatus.NotConfigured)} 个 Key"
                : $"{BalanceSnapshot.CurrencySymbol(currency)}{balance:0.00}",
            secondary,
            balance,
            usage,
            currency,
            visualState);
    }

    internal static IslandDisplayItem FromSnapshot(
        BalanceSnapshot snapshot,
        BalanceVisualState? evaluatedVisualState = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var today = snapshot.TodayUsedAmount is null
            ? ""
            : $" · 今日 {BalanceSnapshot.CurrencySymbol(snapshot.CurrencyCode)}{snapshot.TodayUsedAmount:0.00}";
        var secondary = today.Length == 0 && !string.IsNullOrWhiteSpace(snapshot.SecondaryText)
            ? snapshot.SecondaryText
            : today.TrimStart();
        return DisplayItem(
            snapshot.Provider,
            $"{snapshot.Provider.DisplayName()} · {snapshot.AccountDisplayLabel}",
            snapshot.PrimaryText,
            secondary,
            snapshot.BalanceAmount,
            snapshot.TodayUsedAmount,
            snapshot.CurrencyCode,
            ProjectVisualState(snapshot, evaluatedVisualState));
    }

    private static IslandDisplayGroup BuildGroup(
        AppState state,
        string name,
        IslandGroupMode mode,
        IEnumerable<string> accountIds)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("分组名称不能为空", nameof(name));
        ArgumentNullException.ThrowIfNull(accountIds);

        var ids = accountIds.ToArray();
        if (ids.Length == 0)
            throw new ArgumentException("分组至少包含一个账户", nameof(accountIds));
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ArgumentException("分组成员必须是不同的账户", nameof(accountIds));

        var accounts = ids.Select(id => state.Accounts.FirstOrDefault(account => account.Id == id)
            ?? throw new ArgumentException("分组成员账户不存在", nameof(accountIds))).ToArray();
        var aggregateProvider = mode == IslandGroupMode.Aggregate && accounts.Length > 0
            ? accounts[0].Provider
            : (Provider?)null;
        if (mode == IslandGroupMode.Aggregate && accounts.Any(account => account.Provider != aggregateProvider))
            throw new ArgumentException("聚合分组只能包含同一 Provider 的账户", nameof(accountIds));

        return new IslandDisplayGroup
        {
            Name = name.Trim(),
            Mode = mode,
            AggregateProvider = aggregateProvider,
            AccountIds = ids.ToList()
        };
    }

    private static IslandDisplayGroup FindGroup(AppState state, string groupId) =>
        state.DisplayGroups.FirstOrDefault(group => group.Id == groupId)
        ?? throw new ArgumentException("分组不存在", nameof(groupId));

    private static IslandDisplayItem DisplayItem(
        Provider? provider,
        string title,
        string primaryText,
        string secondaryText,
        double? balanceAmount,
        double? todayUsedAmount,
        string currencyCode,
        BalanceVisualState visualState) => new()
        {
            Provider = provider,
            IconResourceKey = provider is { } value ? ProviderCatalog.Get(value).IconResourceKey : null,
            Title = title,
            PrimaryText = primaryText,
            SecondaryText = secondaryText,
            BalanceAmount = balanceAmount,
            TodayUsedAmount = todayUsedAmount,
            CurrencyCode = currencyCode,
            VisualState = visualState
        };

    private static BalanceVisualState MaximumVisualState(
        IEnumerable<BalanceSnapshot> snapshots,
        IReadOnlyDictionary<string, BalanceVisualState>? visualStates) =>
        snapshots.Select(snapshot => ProjectVisualState(
            snapshot,
            visualStates is not null && visualStates.TryGetValue(snapshot.CredentialId, out var state)
                ? state
                : null)).DefaultIfEmpty(BalanceVisualState.Normal).Max();

    private static BalanceVisualState ProjectVisualState(
        BalanceSnapshot snapshot,
        BalanceVisualState? persistedVisualState) =>
        IsEvaluableBalanceSnapshot(snapshot)
            ? persistedVisualState ?? VisualStateFor(snapshot)
            : BalanceVisualState.Normal;

    private static bool IsEvaluableBalanceSnapshot(BalanceSnapshot snapshot) =>
        snapshot.Status is not SnapshotStatus.Error and not SnapshotStatus.NotConfigured &&
        snapshot.BalanceAmount is not null;

    private static BalanceVisualState VisualStateFor(BalanceSnapshot snapshot) => snapshot.Status switch
    {
        SnapshotStatus.Critical => BalanceVisualState.Critical,
        SnapshotStatus.Warning => BalanceVisualState.Warning15,
        _ => BalanceVisualState.Normal
    };
}
