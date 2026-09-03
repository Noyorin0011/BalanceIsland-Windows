namespace BalanceIsland.Windows;

public static class StartupBehavior
{
    public static bool ShouldStartSilent(bool persistedSilent, IEnumerable<string>? args)
    {
        if (persistedSilent) return true;
        return args?.Any(argument =>
            string.Equals(argument?.Trim(), "--silent", StringComparison.OrdinalIgnoreCase)) == true;
    }
}
