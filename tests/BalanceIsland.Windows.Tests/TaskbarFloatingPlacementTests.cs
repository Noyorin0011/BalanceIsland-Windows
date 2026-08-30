using Xunit;

namespace BalanceIsland.Windows.Tests;

public sealed class TaskbarFloatingPlacementTests
{
    [Fact]
    public void PreferredLeft_places_island_immediately_left_of_widgets()
    {
        var actual = TaskbarFloatingPlacement.PreferredLeft(
            fallbackLeft: 6,
            widgetsLeft: 180,
            widgetsRight: 228,
            islandWidth: 160,
            gap: 6);

        Assert.Equal(14, actual);
    }

    [Fact]
    public void PreferredLeft_without_widgets_uses_widgets_slot_fallback()
    {
        var actual = TaskbarFloatingPlacement.PreferredLeft(
            fallbackLeft: 6,
            widgetsLeft: null,
            widgetsRight: null,
            islandWidth: 160,
            gap: 6);

        Assert.Equal(6, actual);
    }

    [Fact]
    public void PreferredLeft_never_moves_before_taskbar_fallback()
    {
        var actual = TaskbarFloatingPlacement.PreferredLeft(
            fallbackLeft: 10,
            widgetsLeft: 80,
            widgetsRight: 128,
            islandWidth: 160,
            gap: 6);

        Assert.Equal(10, actual);
    }
}
