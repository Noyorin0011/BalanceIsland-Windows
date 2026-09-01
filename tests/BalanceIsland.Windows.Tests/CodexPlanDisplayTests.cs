using Xunit;
using BalanceIsland.Windows;

namespace BalanceIsland.Windows.Tests;

public sealed class CodexPlanDisplayTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-01T12:00:00Z");

    private static Account Account(string id) =>
        new() { Id = id, Provider = Provider.OpenAI, IsEnabled = true, ShowInIsland = true };

    private static CodexPlanUsage Plan(int remaining = 82) => new()
    {
        PlanType = "plus",
        Primary = new CodexPlanQuotaWindow { RemainingPercent = remaining, WindowSeconds = 18_000 },
        UpdatedAt = Now
    };

    private static BalanceCoordinator CoordinatorWithAccountAndPlan()
    {
        var account = Account("account-1");
        var state = new AppState
        {
            Accounts = [account],
            Snapshots = new Dictionary<string, BalanceSnapshot>
            {
                [account.Id] = new()
                {
                    CredentialId = account.Id,
                    Provider = Provider.OpenAI,
                    BalanceAmount = 10,
                    TodayUsedAmount = 2,
                    CurrencyCode = "USD",
                    Status = SnapshotStatus.Ok
                }
            },
            CodexPlanUsage = Plan(),
            CodexPlanShowInIsland = true
        };
        var coordinator = TestFactory.CreateCoordinator(state);
        coordinator.SaveCodexPlanUsage(Plan());
        return coordinator;
    }

    private static AppState StateWithPlan() => new()
    {
        CodexPlanUsage = Plan(),
        CodexPlanShowInIsland = true
    };

    private static AppState StateWithPlanAndAccount()
    {
        var account = Account("account-1");
        return new AppState
        {
            Accounts = [account],
            CodexPlanUsage = Plan(),
            CodexPlanShowInIsland = true
        };
    }

    private static BalanceCoordinator CoordinatorWithPlanGroupButNoSnapshot()
    {
        // Group requests the plan source but no snapshot has been read yet.
        var state = new AppState
        {
            CodexPlanUsage = null,
            CodexPlanShowInIsland = true
        };
        state.DisplayGroups.Add(new IslandDisplayGroup
        {
            Id = "plan-group",
            Name = "仅套餐",
            Mode = IslandGroupMode.Rotation,
            IncludeCodexPlanUsage = true
        });
        state.ActiveDisplayGroupId = "plan-group";
        var coordinator = TestFactory.CreateCoordinator(state);
        return coordinator;
    }

    [Fact]
    public void Default_rotation_appends_plan_after_api_accounts()
    {
        using var coordinator = CoordinatorWithAccountAndPlan();
        var items = IslandAccountSelection.VisibleItems(coordinator);
        Assert.Equal(2, items.Count);
        Assert.Equal("ProviderIcon.OpenAI", items[1].IconResourceKey);
        Assert.Equal("ChatGPT/Codex 套餐", items[1].Title);
    }

    [Fact]
    public void Rotation_group_can_contain_only_plan_usage()
    {
        var state = StateWithPlan();
        var group = IslandDisplayGroups.Create(state, "仅套餐", IslandGroupMode.Rotation, [], includeCodexPlanUsage: true);
        Assert.Empty(group.AccountIds);
        Assert.True(group.IncludeCodexPlanUsage);
    }

    [Fact]
    public void Aggregate_group_rejects_plan_usage() =>
        Assert.Throws<ArgumentException>(() => IslandDisplayGroups.Create(
            StateWithPlanAndAccount(), "错误聚合", IslandGroupMode.Aggregate,
            ["account-1"], includeCodexPlanUsage: true));

    [Fact]
    public void Selected_plan_source_is_not_rendered_without_snapshot()
    {
        using var coordinator = CoordinatorWithPlanGroupButNoSnapshot();
        Assert.Empty(IslandAccountSelection.VisibleItems(coordinator));
    }

    [Fact]
    public void From_codex_plan_usage_projects_openai_icon_and_texts()
    {
        var item = IslandDisplayGroups.FromCodexPlanUsage(Plan(), Now);
        Assert.Equal(Provider.OpenAI, item.Provider);
        Assert.Equal("ProviderIcon.OpenAI", item.IconResourceKey);
        Assert.Equal("ChatGPT/Codex 套餐", item.Title);
        Assert.Null(item.BalanceAmount);
        Assert.Null(item.TodayUsedAmount);
        Assert.Contains("82%", item.PrimaryText);
    }
}
