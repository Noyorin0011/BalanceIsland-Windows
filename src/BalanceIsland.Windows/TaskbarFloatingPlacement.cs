namespace BalanceIsland.Windows;

/// <summary>
/// Pure positioning policy for the Win11 floating island on a horizontal taskbar.
/// </summary>
public static class TaskbarFloatingPlacement
{
    public static int ResolveLeft(
        int taskbarLeft,
        int taskbarRight,
        int islandWidth,
        int margin,
        int? widgetsLeft,
        int? widgetsRight)
    {
        margin = Math.Max(0, margin);
        islandWidth = Math.Max(1, islandWidth);

        var minimumLeft = taskbarLeft + margin;
        var maximumLeft = taskbarRight - margin - islandWidth;
        if (maximumLeft < minimumLeft) return minimumLeft;

        if (widgetsLeft is not { } left || widgetsRight is not { } right || right <= left)
            return minimumLeft;

        var preferredLeft = left - margin - islandWidth;
        return Math.Clamp(preferredLeft, minimumLeft, maximumLeft);
    }
}
