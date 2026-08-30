namespace BalanceIsland.Windows;

/// <summary>
/// Pure taskbar-client placement policy for Win10 vertical embedded mode.
/// Coordinates returned here are relative to the Shell_TrayWnd parent, never screen absolute.
/// </summary>
public static class TaskbarEmbeddedPlacement
{
    public const double VerticalHeightDip = 82d;

    public static double ResolveDisplayHeightDip(double configuredHeight, bool vertical) =>
        vertical ? Math.Max(configuredHeight, VerticalHeightDip) : configuredHeight;

    public static TaskbarClientPlacement ResolveVertical(
        int taskbarWidth,
        int taskbarHeight,
        int desiredWidth,
        int desiredHeight,
        int margin,
        int? notificationTopInTaskbar,
        int fallbackTrayClearance)
    {
        margin = Math.Max(0, margin);
        taskbarWidth = Math.Max(1, taskbarWidth);
        taskbarHeight = Math.Max(1, taskbarHeight);
        desiredWidth = Math.Max(1, desiredWidth);
        desiredHeight = Math.Max(1, desiredHeight);
        fallbackTrayClearance = Math.Max(0, fallbackTrayClearance);

        var availableWidth = Math.Max(1, taskbarWidth - margin * 2);
        var width = Math.Min(desiredWidth, availableWidth);
        var availableHeight = Math.Max(1, taskbarHeight - margin * 2);
        var height = Math.Min(desiredHeight, availableHeight);
        var x = Math.Max(0, (taskbarWidth - width) / 2);

        var bottom = notificationTopInTaskbar is { } notificationTop
            ? notificationTop - margin
            : taskbarHeight - fallbackTrayClearance - margin;
        bottom = Math.Clamp(bottom, margin + height, taskbarHeight - margin);
        var y = Math.Max(margin, bottom - height);

        return new TaskbarClientPlacement(x, y, width, height);
    }
}

public readonly record struct TaskbarClientPlacement(int X, int Y, int Width, int Height);
