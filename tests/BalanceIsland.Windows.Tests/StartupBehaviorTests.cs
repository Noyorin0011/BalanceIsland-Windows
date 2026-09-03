using Xunit;

namespace BalanceIsland.Windows.Tests;

public sealed class StartupBehaviorTests
{
    [Theory]
    [InlineData(false, new string[0], false)]
    [InlineData(true, new string[0], true)]
    [InlineData(false, new[] { "--silent" }, true)]
    [InlineData(false, new[] { "--SILENT" }, true)]
    [InlineData(false, new[] { "--unknown" }, false)]
    public void Startup_decision_combines_persisted_setting_and_command_line(
        bool persisted,
        string[] args,
        bool expected)
    {
        Assert.Equal(expected, StartupBehavior.ShouldStartSilent(persisted, args));
    }

    [Fact]
    public void Silent_startup_setting_is_persisted()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "BalanceIsland.Tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AppDataStore(directory);
            using (var coordinator = new BalanceCoordinator(
                       store,
                       new WindowsCredentialStore(),
                       new ProviderClient()))
            {
                coordinator.SetSilentStartup(true);
            }

            Assert.True(new AppDataStore(directory).LoadResult().State.SilentStartupEnabled);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
