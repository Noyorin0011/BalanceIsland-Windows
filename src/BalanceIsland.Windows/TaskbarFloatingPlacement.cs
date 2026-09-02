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

        // Widgets hold a persistent taskbar slot (their button stays in the UIA tree even when
        // the toggle is off). The island's RIGHT edge aligns to that slot.
        if (widgetsLeft is { } wl && widgetsRight is { } wr && wr > wl)
        {
            // Left-aligned Start: the island occupies the widgets slot (right edge flush to it),
            // which is just right of the Start button / task buttons.
            // Centered Start: widgets sit right of Start; place the island to their right.
            return centered
                ? Math.Max(safeFallback, wr + safeGap)
                : Math.Max(safeFallback, wl - safeWidth);
        }

        // No widgets slot readable: fall back to the Start button anchor so the island never
        // overlaps the far-left taskbar edge / Start button.
        if (startLeft is { } sl && startRight is { } sr && sr > sl)
        {
            return centered
                ? Math.Max(safeFallback, sl - safeWidth)
                : Math.Max(safeFallback, sr + safeGap);
        }

        return safeFallback;
    }
}
