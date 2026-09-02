using Xunit;

namespace BalanceIsland.Windows.Tests;

public sealed class TaskbarFloatingPlacementTests
{
    [Fact]
    public void Left_aligned_places_island_immediately_left_of_widgets()
    {
        var actual = TaskbarFloatingPlacement.PreferredLeft(
            fallbackLeft: 6,
            centered: false,
            startLeft: 4,
            startRight: 44,
            widgetsLeft: 180,
            widgetsRight: 228,
            islandWidth: 160,
            gap: 6);

        Assert.Equal(14, actual);
    }

    [Fact]
    public void Left_aligned_without_widgets_occupies_widgets_slot_after_start()
    {
        // Closing the Widgets toggle removes the WidgetsButton; the island must take the
        // widgets slot (immediately right of Start), not the far-left taskbar edge.
        var actual = TaskbarFloatingPlacement.PreferredLeft(
            fallbackLeft: 6,
            centered: false,
            startLeft: 4,
            startRight: 44,
            widgetsLeft: null,
            widgetsRight: null,
            islandWidth: 160,
            gap: 6);

        Assert.Equal(50, actual);
    }

    [Fact]
    public void Centered_places_island_immediately_right_of_widgets()
    {
        var actual = TaskbarFloatingPlacement.PreferredLeft(
            fallbackLeft: 6,
            centered: true,
            startLeft: 400,
            startRight: 440,
            widgetsLeft: 340,
            widgetsRight: 388,
            islandWidth: 160,
            gap: 6);

        Assert.Equal(394, actual);
    }

    [Fact]
    public void Centered_without_widgets_occupies_widgets_slot_left_of_start()
    {
        var actual = TaskbarFloatingPlacement.PreferredLeft(
            fallbackLeft: 6,
            centered: true,
            startLeft: 400,
            startRight: 440,
            widgetsLeft: null,
            widgetsRight: null,
            islandWidth: 160,
            gap: 6);

        Assert.Equal(234, actual);
    }

    [Fact]
    public void Left_aligned_never_moves_before_taskbar_fallback()
    {
        var actual = TaskbarFloatingPlacement.PreferredLeft(
            fallbackLeft: 10,
            centered: false,
            startLeft: 4,
            startRight: 44,
            widgetsLeft: 80,
            widgetsRight: 128,
            islandWidth: 160,
            gap: 6);

        Assert.Equal(10, actual);
    }

    [Fact]
    public void Without_start_anchor_falls_back_to_safe_left()
    {
        var actual = TaskbarFloatingPlacement.PreferredLeft(
            fallbackLeft: 6,
            centered: false,
            startLeft: null,
            startRight: null,
            widgetsLeft: 180,
            widgetsRight: 228,
            islandWidth: 160,
            gap: 6);

        Assert.Equal(14, actual);
    }
}
