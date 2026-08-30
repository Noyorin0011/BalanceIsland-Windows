using Xunit;
using System.Text.Json;
using System.Text.Json.Serialization;
using BalanceIsland.Windows;

namespace BalanceIsland.Windows.Tests;

public sealed class AppStateNormalizerTests
{
    [Fact]
    public void Normalize_v021_state_uses_safe_v030_defaults()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
        var state = JsonSerializer.Deserialize<AppState>("{\"accounts\":[]}", options)!;
        AppStateNormalizer.Normalize(state);

        Assert.Equal(AppThemeMode.System, state.ThemeMode);
        Assert.Equal(IslandColorTheme.Classic, state.IslandColorTheme);
        Assert.Empty(state.DisplayGroups);
        Assert.Null(state.ActiveDisplayGroupId);
        Assert.True(state.NotifyWarning15);
        Assert.True(state.NotifyCritical);
        Assert.True(state.NotifyAnomaly);
    }

    [Fact]
    public void Normalize_removes_missing_accounts_and_invalid_active_group()
    {
        var state = new AppState
        {
            DisplayGroups = [new() { Id = "g", Name = "  Team  ", AccountIds = ["missing"] }],
            ActiveDisplayGroupId = "missing-group"
        };
        AppStateNormalizer.Normalize(state);

        Assert.Equal("Team", state.DisplayGroups[0].Name);
        Assert.Empty(state.DisplayGroups[0].AccountIds);
        Assert.Null(state.ActiveDisplayGroupId);
    }

    [Fact]
    public void Legacy_drop_step_is_retained_only_for_state_round_trip_compatibility()
    {
        var state = new AppState { Accounts = [new() { Id = "account", DropStep = 7.5 }] };

        AppStateNormalizer.Normalize(state);

        Assert.Equal(7.5, state.Accounts[0].DropStep);
    }
}
