using Xunit;

namespace BalanceIsland.Windows.Tests;

public sealed class TaskbarEmbeddedPlacementTests
{
    [Fact]
    public void ResolveVertical_places_island_above_notification_area()
    {
        var placement = TaskbarEmbeddedPlacement.ResolveVertical(
            taskbarWidth: 48,
            taskbarHeight: 1080,
            desiredWidth: 225,
            desiredHeight: 82,
            margin: 6,
            notificationTopInTaskbar: 930,
            fallbackTrayClearance: 42);

        Assert.Equal(6, placement.X);
        Assert.Equal(842, placement.Y);
        Assert.Equal(36, placement.Width);
        Assert.Equal(82, placement.Height);
    }

    [Fact]
    public void ResolveVertical_uses_parent_client_x_so_right_taskbar_cannot_overflow()
    {
        var placement = TaskbarEmbeddedPlacement.ResolveVertical(
            taskbarWidth: 52,
            taskbarHeight: 1080,
            desiredWidth: 225,
            desiredHeight: 82,
            margin: 6,
            notificationTopInTaskbar: 900,
            fallbackTrayClearance: 42);

        Assert.InRange(placement.X, 0, 52 - placement.Width);
        Assert.Equal(6, placement.X);
    }

    [Fact]
    public void ResolveVertical_without_notification_uses_tray_clearance_fallback()
    {
        var placement = TaskbarEmbeddedPlacement.ResolveVertical(
            taskbarWidth: 48,
            taskbarHeight: 1080,
            desiredWidth: 225,
            desiredHeight: 82,
            margin: 6,
            notificationTopInTaskbar: null,
            fallbackTrayClearance: 42);

        Assert.Equal(950, placement.Y);
    }
}
