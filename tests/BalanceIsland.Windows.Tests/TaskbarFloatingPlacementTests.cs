using Xunit;

namespace BalanceIsland.Windows.Tests;

public sealed class TaskbarFloatingPlacementTests
{
    [Fact]
    public void ResolveLeft_places_island_immediately_left_of_widgets()
    {
        var x = TaskbarFloatingPlacement.ResolveLeft(
            taskbarLeft: 0,
            taskbarRight: 1920,
            islandWidth: 225,
            margin: 6,
            widgetsLeft: 300,
            widgetsRight: 348);

        Assert.Equal(69, x);
    }

    [Fact]
    public void ResolveLeft_without_widgets_uses_left_widgets_slot()
    {
        var x = TaskbarFloatingPlacement.ResolveLeft(
            taskbarLeft: 0,
            taskbarRight: 1920,
            islandWidth: 225,
            margin: 6,
            widgetsLeft: null,
            widgetsRight: null);

        Assert.Equal(6, x);
    }

    [Fact]
    public void ResolveLeft_clamps_to_taskbar_when_widgets_are_too_close_to_left_edge()
    {
        var x = TaskbarFloatingPlacement.ResolveLeft(
            taskbarLeft: 100,
            taskbarRight: 2020,
            islandWidth: 225,
            margin: 6,
            widgetsLeft: 180,
            widgetsRight: 228);

        Assert.Equal(106, x);
    }
}
