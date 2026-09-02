namespace BalanceIsland.Windows;

public static class TaskbarFloatingPlacement
{
    public static int PreferredLeft(
        int fallbackLeft,
        bool centered,
        int? startLeft,
        int? startRight,
        int? widgetsLeft,
        int? widgetsRight,
        int islandWidth,
        int gap)
    {
        var safeFallback = fallbackLeft;
        var safeWidth = Math.Max(1, islandWidth);
        var safeGap = Math.Max(0, gap);

        if (widgetsLeft is { } wl && widgetsRight is { } wr && wr > wl)
        {
            // Widgets are present: place the island just left of them when Start is left-aligned,
            // or just right of them when Start is centered (the widgets move beside the centered Start).
            return centered
                ? Math.Max(safeFallback, wr + safeGap)
                : Math.Max(safeFallback, wl - safeGap - safeWidth);
        }

        // Widgets toggle is off (no WidgetsButton). The island must occupy the widgets slot rather
        // than dropping to the far-left taskbar edge (which would overlap the Start button).
        if (startLeft is { } sl && startRight is { } sr && sr > sl)
        {
            return centered
                ? Math.Max(safeFallback, sl - safeGap - safeWidth)
                : Math.Max(safeFallback, sr + safeGap);
        }

        return safeFallback;
    }
}
