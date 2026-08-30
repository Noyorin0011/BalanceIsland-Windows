namespace BalanceIsland.Windows;

public static class TaskbarFloatingPlacement
{
    public static int PreferredLeft(
        int fallbackLeft,
        int? widgetsLeft,
        int? widgetsRight,
        int islandWidth,
        int gap)
    {
        var safeFallback = fallbackLeft;
        var safeWidth = Math.Max(1, islandWidth);
        var safeGap = Math.Max(0, gap);

        if (widgetsLeft is null || widgetsRight is null || widgetsRight <= widgetsLeft)
            return safeFallback;

        return Math.Max(safeFallback, widgetsLeft.Value - safeGap - safeWidth);
    }
}
