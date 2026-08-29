using Xunit;
using BalanceIsland.Windows;

namespace BalanceIsland.Windows.Tests;

public sealed class AppDataStoreSemanticValidationTests
{
    public static IEnumerable<object[]> UnrepairableStates()
    {
        yield return ["null account", """{"accounts":[null]}"""];
        yield return ["empty account id", """{"accounts":[{"id":"","provider":"DeepSeek"}]}"""];
        yield return ["duplicate account id", """{"accounts":[{"id":"a","provider":"DeepSeek"},{"id":"a","provider":"OpenAI"}]}"""];
        yield return ["null group", """{"accounts":[],"displayGroups":[null]}"""];
        yield return ["empty group id", """{"accounts":[],"displayGroups":[{"id":"","name":"Empty"}]}"""];
        yield return ["duplicate group id", """{"accounts":[],"displayGroups":[{"id":"g","name":"One"},{"id":"g","name":"Two"}]}"""];
        yield return ["integer enum token", """{"accounts":[{"id":"a","provider":0}]}"""];
        yield return ["unknown enum", """{"accounts":[{"id":"a","provider":"UnknownProvider"}]}"""];
        yield return ["non-finite account value", """{"accounts":[{"id":"a","provider":"DeepSeek","warningLine":"NaN"}]}"""];
    }

    [Theory]
    [MemberData(nameof(UnrepairableStates))]
    public void Unrepairable_semantic_state_is_not_loaded_or_overwritten_on_construction(
        string _,
        string json)
    {
        var directory = CreateStateDirectory(json);
        var path = Path.Combine(directory, "state.json");
        var original = File.ReadAllBytes(path);
        var store = new AppDataStore(directory);

        var result = store.LoadResult();
        using var coordinator = new BalanceCoordinator(store, new WindowsCredentialStore(), new ProviderClient());

        Assert.False(result.LoadedFromDisk);
        Assert.NotNull(result.Error);
        Assert.Empty(coordinator.State.Accounts);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void Load_safely_normalizes_null_collections_and_orphan_dictionary_entries()
    {
        var directory = CreateStateDirectory("""
            {
              "accounts": [{"id":"a","provider":"deepseek"}],
              "snapshots": {"orphan": null},
              "schedules": null,
              "dailyUsage": {"orphan": {}},
              "alerts": {"orphan": null},
              "displayGroups": null
            }
            """);

        var result = new AppDataStore(directory).LoadResult();

        Assert.True(result.LoadedFromDisk);
        Assert.Equal(Provider.DeepSeek, Assert.Single(result.State.Accounts).Provider);
        Assert.Empty(result.State.Snapshots);
        Assert.Empty(result.State.Schedules);
        Assert.Empty(result.State.DailyUsage);
        Assert.Empty(result.State.Alerts);
        Assert.Empty(result.State.DisplayGroups);
    }

    [Fact]
    public void Load_preserves_v021_case_insensitive_string_enums_and_nan_edit_position_sentinel()
    {
        var directory = CreateStateDirectory("""
            {
              "accounts": [{"id":"a","provider":"dEePsEeK","anomalyMode":"bOtH"}],
              "themeMode":"sYsTeM",
              "islandEditLeft":"NaN",
              "islandEditTop":"NaN"
            }
            """);

        var result = new AppDataStore(directory).LoadResult();

        Assert.True(result.LoadedFromDisk);
        Assert.Equal(Provider.DeepSeek, Assert.Single(result.State.Accounts).Provider);
        Assert.Equal(AnomalyMode.Both, result.State.Accounts[0].AnomalyMode);
        Assert.Equal(AppThemeMode.System, result.State.ThemeMode);
        Assert.True(double.IsNaN(result.State.IslandEditLeft));
        Assert.True(double.IsNaN(result.State.IslandEditTop));
    }

    private static string CreateStateDirectory(string json)
    {
        var directory = Path.Combine(Path.GetTempPath(), "BalanceIsland.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "state.json"), json);
        return directory;
    }
}
