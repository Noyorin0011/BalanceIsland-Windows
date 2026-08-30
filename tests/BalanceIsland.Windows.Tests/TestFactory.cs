using BalanceIsland.Windows;

namespace BalanceIsland.Windows.Tests;

internal static class TestFactory
{
    public static BalanceCoordinator CreateCoordinator(AppState? initial = null, ProviderClient? client = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "BalanceIsland.Tests", Guid.NewGuid().ToString("N"));
        var store = new AppDataStore(directory);
        if (initial is not null) store.Save(initial);
        return new BalanceCoordinator(store, new WindowsCredentialStore(), client ?? new ProviderClient());
    }
}
