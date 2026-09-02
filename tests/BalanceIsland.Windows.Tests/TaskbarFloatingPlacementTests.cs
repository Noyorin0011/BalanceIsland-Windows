using Xunit;

namespace BalanceIsland.Windows.Tests;

public sealed class TaskbarFloatingPlacementTests
{
    [Fact]
    public void Left_aligned_aligns_island_right_edge_to_widgets_slot()
    {
        // Island width 160, widgets slot begins at x=180. Right edge is flush to the slot.
        var actual = TaskbarFloatingPlacement.PreferredLeft(
            fallbackLeft: 6,
            centered: false,
            startLeft: 4,
            startRight: 44,
            widgetsLeft: 180,
            widgetsRight: 228,
            islandWidth: 160,
            gap: 6);

        Assert.Equal(20, actual); // 180 - 160
    }

    [Fact]
    public void Left_aligned_widgets_toggle_off_still_aligns_to_persistent_slot()
    {
        // Widgets button remains in the UIA tree (offscreen) with its slot, so the island
        // occupies that slot rather than the far-left Start edge.
        var actual = TaskbarFloatingPlacement.PreferredLeft(
            fallbackLeft: 6,
            centered: false,
            startLeft: 4,
            startRight: 44,
            widgetsLeft: 180,
            widgetsRight: 228,
            islandWidth: 160,
            gap: 6);

        Assert.Equal(20, actual);
    }

    [Fact]
    public void Centered_places_island_right_of_widgets()
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

        Assert.Equal(394, actual); // 388 + 6
    }

    [Fact]
    public void Without_widgets_slot_falls_back_to_start_anchor()
    {
        var actual = TaskbarFloatingPlacement.PreferredLeft(
            fallbackLeft: 6,
            centered: false,
            startLeft: 4,
            startRight: 44,
            widgetsLeft: null,
            widgetsRight: null,
            islandWidth: 160,
            gap: 6);

        Assert.Equal(50, actual); // 44 + 6
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
    public void Centered_without_widgets_occupies_slot_left_of_start()
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

        Assert.Equal(240, actual); // 400 - 160
    }
}
