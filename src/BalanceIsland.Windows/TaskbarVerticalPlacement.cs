namespace BalanceIsland.Windows;

public static class TaskbarVerticalPlacement
{
    public readonly record struct Result(int X, int Y, int Width, int Height);

    public static Result Place(
        int taskbarWidth,
        int taskbarHeight,
        int desiredWidth,
        int desiredHeight,
        int margin,
        int? notificationTopLocal,
        int fallbackTrayClearance,
        int minimumContentHeight)
    {
        var safeMargin = Math.Max(1, margin);
        var availableWidth = Math.Max(1, taskbarWidth - 2 * safeMargin);
        var width = Math.Min(Math.Max(1, desiredWidth), availableWidth);

        var maxHeight = Math.Max(1, taskbarHeight - 2 * safeMargin);
        var requestedHeight = Math.Max(Math.Max(1, desiredHeight), Math.Max(1, minimumContentHeight));
        var height = Math.Min(requestedHeight, maxHeight);

        var fallbackBottom = Math.Max(safeMargin, taskbarHeight - Math.Max(0, fallbackTrayClearance) - safeMargin);
        var bottom = notificationTopLocal is > 0 and < int.MaxValue
            ? Math.Min(taskbarHeight - safeMargin, notificationTopLocal.Value - safeMargin)
            : fallbackBottom;
        bottom = Math.Max(safeMargin + height, bottom);

        var y = Math.Max(safeMargin, bottom - height);
        return new Result(safeMargin, y, width, height);
    }
}
