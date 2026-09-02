namespace BalanceIsland.Windows;

public static class TaskbarFloatingPlacement
{
    public readonly record struct Result(int Left, int Top, bool FitsInTaskbarBand);

    public static Result PlaceHorizontal(
        int taskbarLeft,
        int taskbarTop,
        int taskbarRight,
        bool centered,
        int? startLeft,
        int? startRight,
        int? widgetsLeft,
        int? widgetsRight,
        int? taskButtonsRight,
        int? notificationLeft,
        int islandWidth,
        int gap)
    {
        var safeWidth = Math.Max(1, islandWidth);
        var safeGap = Math.Max(0, gap);
        var safeLeft = taskbarLeft + safeGap;
        var safeRight = Math.Max(safeLeft, taskbarRight - safeGap - safeWidth);
        var hasStart = startLeft is { } sl && startRight is { } sr && sr > sl;
        var hasWidgets = widgetsLeft is { } wl && widgetsRight is { } wr && wr > wl;

        if (centered)
        {
            // With Widgets enabled, the user-facing anchor is its right edge. Without Widgets,
            // the island occupies the far-left taskbar slot. Start remains a hard right bound.
            var desiredLeft = hasWidgets ? widgetsRight!.Value + safeGap : safeLeft;
            var rightBound = hasStart
                ? Math.Min(safeRight, startLeft!.Value - safeGap - safeWidth)
                : safeRight;
            var fits = desiredLeft >= safeLeft && desiredLeft <= rightBound;
            return new Result(Math.Clamp(desiredLeft, safeLeft, safeRight), taskbarTop, fits);
        }

        // Left-aligned Start: move beyond the entire contiguous Start/task-button group, then
        // require the island to end before both visible Widgets and the notification area.
        var occupiedRight = taskbarLeft;
        if (hasStart) occupiedRight = Math.Max(occupiedRight, startRight!.Value);
        if (taskButtonsRight is { } tbr && tbr > taskbarLeft)
            occupiedRight = Math.Max(occupiedRight, tbr);
        if (hasWidgets && widgetsLeft!.Value <= occupiedRight && widgetsRight!.Value > occupiedRight)
            occupiedRight = widgetsRight.Value;
        var desired = Math.Max(safeLeft, occupiedRight + safeGap);

        var firstRightElement = taskbarRight;
        if (hasWidgets && widgetsLeft!.Value > occupiedRight)
            firstRightElement = Math.Min(firstRightElement, widgetsLeft.Value);
        if (notificationLeft is { } nl && nl > occupiedRight)
            firstRightElement = Math.Min(firstRightElement, nl);
        var maximumLeft = Math.Min(safeRight, firstRightElement - safeGap - safeWidth);
        var fitsLeftAligned = desired <= maximumLeft;
        return new Result(Math.Clamp(desired, safeLeft, safeRight), taskbarTop, fitsLeftAligned);
    }

    public static Result PlaceLegacyHorizontal(
        int taskbarLeft,
        int taskbarTop,
        int taskbarHeight,
        int? widgetsLeft,
        int? widgetsRight,
        int islandWidth,
        int islandHeight,
        int gap)
    {
        var safeGap = Math.Max(0, gap);
        var safeHeight = Math.Max(1, islandHeight);
        var left = PreferredLeft(
            taskbarLeft + safeGap,
            widgetsLeft,
            widgetsRight,
            islandWidth,
            safeGap);
        var top = taskbarTop + Math.Max(0, (Math.Max(1, taskbarHeight) - safeHeight) / 2);
        return new Result(left, top, true);
    }

    public static bool IsCenteredFromSnapshot(
        int taskbarLeft,
        int taskbarRight,
        int? startLeft,
        int? startRight,
        bool registryCentered)
    {
        if (startLeft is not { } left ||
            startRight is not { } right ||
            right <= left ||
            left < taskbarLeft ||
            right > taskbarRight)
        {
            return registryCentered;
        }

        // A left-aligned Start button occupies the first button slot. Compare its offset with
        // the observed button width instead of a percentage of the taskbar: a crowded centered
        // group can legitimately shift Start well inside the old 25% threshold.
        var startWidth = right - left;
        return left > taskbarLeft + startWidth * 2;
    }

    public static bool ShouldRestackForReorder(
        bool isTopLevelContainer,
        bool islandIsAboveTaskbar) =>
        isTopLevelContainer && !islandIsAboveTaskbar;

    public static bool ShouldApply<T>(
        T? previous,
        T current,
        bool isWindowVisible,
        bool forceRestack)
        where T : struct, IEquatable<T>
    {
        return forceRestack ||
               !isWindowVisible ||
               previous is null ||
               !EqualityComparer<T>.Default.Equals(previous.Value, current);
    }

    // Kept for the Windows 10 embedded-taskbar path.
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
