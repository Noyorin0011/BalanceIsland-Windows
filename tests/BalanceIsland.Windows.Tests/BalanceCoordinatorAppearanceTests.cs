using Xunit;
using BalanceIsland.Windows;

namespace BalanceIsland.Windows.Tests;

public sealed class BalanceCoordinatorAppearanceTests
{
    [Fact]
    public void Notification_settings_persist_independently()
    {
        using var coordinator = TestFactory.CreateCoordinator();

        coordinator.SetNotificationSettings(warning15: false, critical: true, anomaly: false);

        Assert.False(coordinator.State.NotifyWarning15);
        Assert.True(coordinator.State.NotifyCritical);
        Assert.False(coordinator.State.NotifyAnomaly);
    }

    [Fact]
    public void Display_group_settings_create_update_activate_and_delete()
    {
        using var coordinator = TestFactory.CreateCoordinator(new AppState
        {
            Accounts =
            [
                new Account { Id = "first", Provider = Provider.OpenAI },
                new Account { Id = "second", Provider = Provider.OpenAI }
            ]
        });

        var group = coordinator.CreateDisplayGroup("Production", IslandGroupMode.Rotation, ["first"]);
        coordinator.UpdateDisplayGroup(group.Id, "Production total", IslandGroupMode.Aggregate, ["first", "second"]);
        coordinator.SetActiveDisplayGroup(group.Id);

        Assert.Equal("Production total", coordinator.State.DisplayGroups.Single().Name);
        Assert.Equal(IslandGroupMode.Aggregate, coordinator.State.DisplayGroups.Single().Mode);
        Assert.Equal(group.Id, coordinator.State.ActiveDisplayGroupId);

        coordinator.SetActiveDisplayGroup(null);

        Assert.Single(coordinator.State.DisplayGroups);
        Assert.Null(coordinator.State.ActiveDisplayGroupId);

        coordinator.DeleteDisplayGroup(group.Id);

        Assert.Empty(coordinator.State.DisplayGroups);
        Assert.Null(coordinator.State.ActiveDisplayGroupId);
    }

    [Fact]
    public void Display_group_selection_prefers_the_newly_created_group_over_an_older_group()
    {
        var older = new IslandDisplayGroup { Id = "older", Name = "Older" };
        var created = new IslandDisplayGroup { Id = "created", Name = "Created" };

        var selected = DisplayGroupSelection.Resolve([older, created], "older", created.Id, null);

        Assert.Equal(created.Id, selected);
        Assert.Equal(created.Id, DisplayGroupSelection.Resolve([older, created], created.Id, null, older.Id));
        Assert.Equal(older.Id, DisplayGroupSelection.Resolve([older], created.Id, null, older.Id));
    }

    [Fact]
    public void Appearance_setters_update_state_and_emit_change()
    {
        using var coordinator = TestFactory.CreateCoordinator();
        var changes = 0;
        coordinator.StateChanged += (_, _) => changes++;

        coordinator.SetThemeMode(AppThemeMode.Dark);
        coordinator.SetIslandColorTheme(IslandColorTheme.Sky);
        coordinator.SetCustomIslandColors("#FFFFFF", "#D778FF", "#FFB340", "#FF5C6C");

        Assert.Equal(AppThemeMode.Dark, coordinator.State.ThemeMode);
        Assert.Equal(IslandColorTheme.Sky, coordinator.State.IslandColorTheme);
        Assert.Equal("#FFFFFFFF", coordinator.State.CustomNormalColor);
        Assert.Equal("#FFD778FF", coordinator.State.CustomAnomalyColor);
        Assert.Equal("#FFFFB340", coordinator.State.CustomWarning15Color);
        Assert.Equal("#FFFF5C6C", coordinator.State.CustomCriticalColor);
        Assert.True(changes >= 3);
    }

    [Theory]
    [InlineData("#112233", "invalid", "#445566", "#778899")]
    [InlineData("#112233", "#445566", "invalid", "#778899")]
    [InlineData("#112233", "#445566", "#778899", "invalid")]
    public void SetCustomIslandColors_rejects_invalid_later_input_without_partial_mutation(
        string normal,
        string anomaly,
        string warning15,
        string critical)
    {
        using var coordinator = TestFactory.CreateCoordinator(new AppState
        {
            CustomNormalColor = "#FF010203",
            CustomAnomalyColor = "#FF040506",
            CustomWarning15Color = "#FF070809",
            CustomCriticalColor = "#FF0A0B0C"
        });
        var changes = 0;
        coordinator.StateChanged += (_, _) => changes++;

        Assert.Throws<ArgumentException>(() =>
            coordinator.SetCustomIslandColors(normal, anomaly, warning15, critical));

        Assert.Equal("#FF010203", coordinator.State.CustomNormalColor);
        Assert.Equal("#FF040506", coordinator.State.CustomAnomalyColor);
        Assert.Equal("#FF070809", coordinator.State.CustomWarning15Color);
        Assert.Equal("#FF0A0B0C", coordinator.State.CustomCriticalColor);
        Assert.Equal(0, changes);
    }
}
