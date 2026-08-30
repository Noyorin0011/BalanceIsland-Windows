using Xunit;

namespace BalanceIsland.Windows.Tests;

public sealed class TaskbarVerticalPlacementTests
{
    [Fact]
    public void Place_uses_parent_local_x_so_right_docked_taskbar_stays_visible()
    {
        var actual = TaskbarVerticalPlacement.Place(
            taskbarWidth: 48,
            taskbarHeight: 1080,
            desiredWidth: 225,
            desiredHeight: 38,
            margin: 4,
            notificationTopLocal: 900,
            fallbackTrayClearance: 42,
            minimumContentHeight: 78);

        Assert.Equal(4, actual.X);
        Assert.Equal(40, actual.Width);
        Assert.Equal(78, actual.Height);
    }

    [Fact]
    public void Place_positions_island_immediately_above_real_notification_area()
    {
        var actual = TaskbarVerticalPlacement.Place(
            taskbarWidth: 48,
            taskbarHeight: 1080,
            desiredWidth: 225,
            desiredHeight: 38,
            margin: 4,
            notificationTopLocal: 900,
            fallbackTrayClearance: 42,
            minimumContentHeight: 78);

        Assert.Equal(818, actual.Y);
        Assert.Equal(896, actual.Y + actual.Height);
    }

    [Fact]
    public void Place_falls_back_to_tray_clearance_when_notification_geometry_is_missing()
    {
        var actual = TaskbarVerticalPlacement.Place(
            taskbarWidth: 48,
            taskbarHeight: 1080,
            desiredWidth: 225,
            desiredHeight: 38,
            margin: 4,
            notificationTopLocal: null,
            fallbackTrayClearance: 42,
            minimumContentHeight: 78);

        Assert.Equal(958, actual.Y);
        Assert.Equal(1036, actual.Y + actual.Height);
    }
}
