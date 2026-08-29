using Xunit;
using BalanceIsland.Windows;

namespace BalanceIsland.Windows.Tests;

public sealed class IslandDisplayGroupsTests
{
    [Fact]
    public void Rotation_group_accepts_mixed_providers()
    {
        var state = StateWith(DeepSeekAccount("a"), OpenAiAccount("b"));

        var group = IslandDisplayGroups.Create(state, "Mixed", IslandGroupMode.Rotation, ["a", "b"]);

        Assert.Equal(["a", "b"], group.AccountIds);
    }

    [Fact]
    public void Aggregate_group_rejects_mixed_providers()
    {
        var state = StateWith(DeepSeekAccount("a"), OpenAiAccount("b"));

        var error = Assert.Throws<ArgumentException>(() =>
            IslandDisplayGroups.Create(state, "Bad", IslandGroupMode.Aggregate, ["a", "b"]));

        Assert.Contains("同一 Provider", error.Message);
    }

    [Fact]
    public void Aggregate_group_rejects_empty_membership()
    {
        var state = StateWith(DeepSeekAccount("a"));

        var error = Assert.Throws<ArgumentException>(() =>
            IslandDisplayGroups.Create(state, "Empty", IslandGroupMode.Aggregate, []));

        Assert.Contains("至少", error.Message);
    }

    [Fact]
    public void Aggregate_sums_balance_and_today_usage_for_same_currency()
    {
        var item = Aggregate([Snapshot("a", 10, 2, "USD"), Snapshot("b", 5, 1, "USD")]);

        Assert.Equal(15, item.BalanceAmount);
        Assert.Equal(3, item.TodayUsedAmount);
        Assert.Equal("USD", item.CurrencyCode);
    }

    [Fact]
    public void Aggregate_refuses_mixed_snapshot_currencies()
    {
        var item = Aggregate([Snapshot("a", 10, 2, "USD"), Snapshot("b", 5, 1, "CNY")]);

        Assert.Null(item.BalanceAmount);
        Assert.Contains("币种不一致", item.SecondaryText);
    }

    [Fact]
    public void Aggregate_refuses_persisted_members_from_different_providers()
    {
        var group = new IslandDisplayGroup
        {
            Name = "Damaged",
            Mode = IslandGroupMode.Aggregate,
            AggregateProvider = Provider.DeepSeek,
            AccountIds = ["a", "b"]
        };
        var snapshots = new Dictionary<string, BalanceSnapshot>
        {
            ["a"] = Snapshot("a", 10, 2, "USD"),
            ["b"] = new()
            {
                CredentialId = "b",
                Provider = Provider.OpenAI,
                BalanceAmount = 5,
                TodayUsedAmount = 1,
                CurrencyCode = "USD",
                Status = SnapshotStatus.Ok
            }
        };

        var item = IslandDisplayGroups.Aggregate(group, snapshots);

        Assert.Null(item.BalanceAmount);
        Assert.Null(item.TodayUsedAmount);
        Assert.Contains("Provider", item.SecondaryText);
    }

    [Fact]
    public void Aggregate_sums_usage_when_balances_are_unavailable()
    {
        var snapshots = new[]
        {
            UsageOnlySnapshot("a", 2, "USD"),
            UsageOnlySnapshot("b", 1, "USD")
        };

        var item = Aggregate(snapshots);

        Assert.Null(item.BalanceAmount);
        Assert.Equal(3, item.TodayUsedAmount);
        Assert.Contains("今日", item.SecondaryText);
    }

    [Fact]
    public void Aggregate_without_numeric_values_counts_healthy_and_failed_keys()
    {
        var snapshots = new[]
        {
            new BalanceSnapshot { CredentialId = "a", Provider = Provider.DeepSeek, Status = SnapshotStatus.Ok },
            new BalanceSnapshot { CredentialId = "b", Provider = Provider.DeepSeek, Status = SnapshotStatus.Error }
        };

        var item = Aggregate(snapshots);

        Assert.Contains("有效 1", item.PrimaryText);
        Assert.Contains("错误 1", item.PrimaryText);
        Assert.Null(item.BalanceAmount);
        Assert.Null(item.TodayUsedAmount);
    }

    [Fact]
    public void Removing_account_cleans_all_groups_and_deleting_active_group_clears_selection()
    {
        var state = StateWith(DeepSeekAccount("a"));
        var group = IslandDisplayGroups.Create(state, "One", IslandGroupMode.Rotation, ["a"]);
        state.ActiveDisplayGroupId = group.Id;

        IslandDisplayGroups.RemoveAccount(state, "a");
        IslandDisplayGroups.Delete(state, group.Id);

        Assert.Empty(group.AccountIds);
        Assert.Null(state.ActiveDisplayGroupId);
    }

    [Fact]
    public void Account_visibility_setter_immediately_changes_default_visible_items()
    {
        var account = DeepSeekAccount("a");
        using var coordinator = TestFactory.CreateCoordinator(new AppState
        {
            Accounts = [account],
            Snapshots = new Dictionary<string, BalanceSnapshot>
            {
                [account.Id] = Snapshot(account.Id, 10, 2, "USD")
            }
        });

        Assert.Single(IslandAccountSelection.VisibleItems(coordinator));

        coordinator.SetAccountShowInIsland(account.Id, false);

        Assert.False(coordinator.State.Accounts.Single().ShowInIsland);
        Assert.Empty(IslandAccountSelection.VisibleItems(coordinator));
    }

    [Fact]
    public void Active_group_membership_remains_authoritative_over_account_visibility()
    {
        var account = DeepSeekAccount("a");
        account.ShowInIsland = false;
        using var coordinator = TestFactory.CreateCoordinator(new AppState
        {
            Accounts = [account],
            Snapshots = new Dictionary<string, BalanceSnapshot>
            {
                [account.Id] = Snapshot(account.Id, 10, 2, "USD")
            },
            DisplayGroups =
            [
                new()
                {
                    Id = "group",
                    Name = "Team",
                    Mode = IslandGroupMode.Rotation,
                    AccountIds = [account.Id]
                }
            ],
            ActiveDisplayGroupId = "group"
        });

        Assert.Single(IslandAccountSelection.VisibleItems(coordinator));
    }

    [Fact]
    public void Aggregate_editor_validation_unboxes_selected_value_and_rejects_mixed_providers()
    {
        object selectedValue = IslandGroupMode.Aggregate;

        Assert.True(DisplayGroupEditorValidation.HasMixedProviders(
            selectedValue,
            [Provider.DeepSeek, Provider.OpenAI]));
        Assert.False(DisplayGroupEditorValidation.HasMixedProviders(
            (object)IslandGroupMode.Rotation,
            [Provider.DeepSeek, Provider.OpenAI]));
        Assert.False(DisplayGroupEditorValidation.HasMixedProviders(
            selectedValue,
            [Provider.DeepSeek, Provider.DeepSeek]));
    }

    private static Account DeepSeekAccount(string id) =>
        new() { Id = id, Provider = Provider.DeepSeek, IsEnabled = true };

    private static Account OpenAiAccount(string id) =>
        new() { Id = id, Provider = Provider.OpenAI, IsEnabled = true };

    private static AppState StateWith(params Account[] accounts) =>
        new() { Accounts = accounts.ToList() };

    private static BalanceSnapshot Snapshot(
        string id, double balance, double used, string currency) => new()
        {
            CredentialId = id,
            Provider = Provider.DeepSeek,
            BalanceAmount = balance,
            TodayUsedAmount = used,
            CurrencyCode = currency,
            Status = SnapshotStatus.Ok
        };

    private static BalanceSnapshot UsageOnlySnapshot(string id, double used, string currency) => new()
    {
        CredentialId = id,
        Provider = Provider.DeepSeek,
        TodayUsedAmount = used,
        CurrencyCode = currency,
        Status = SnapshotStatus.Ok
    };

    private static IslandDisplayItem Aggregate(IEnumerable<BalanceSnapshot> snapshots) =>
        IslandDisplayGroups.Aggregate(
            new IslandDisplayGroup
            {
                Id = "g",
                Name = "Team",
                Mode = IslandGroupMode.Aggregate,
                AggregateProvider = Provider.DeepSeek,
                AccountIds = snapshots.Select(x => x.CredentialId).ToList()
            },
            snapshots.ToDictionary(x => x.CredentialId));
}
