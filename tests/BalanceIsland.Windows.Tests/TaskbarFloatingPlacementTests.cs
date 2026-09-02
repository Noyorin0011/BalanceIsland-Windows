using Xunit;

namespace BalanceIsland.Windows.Tests;

public sealed class TaskbarFloatingPlacementTests
{
    [Fact]
    public void Centered_with_widgets_places_island_immediately_right_of_widgets()
    {
        var actual = TaskbarFloatingPlacement.PlaceHorizontal(
            taskbarLeft: 0,
            taskbarTop: 1032,
            taskbarRight: 1920,
            taskbarHeight: 48,
            islandHeight: 38,
            centered: true,
            startLeft: 760,
            startRight: 808,
            widgetsLeft: 0,
            widgetsRight: 180,
            taskButtonsRight: 1120,
            notificationLeft: 1600,
            islandWidth: 225,
            gap: 6);

        Assert.Equal(186, actual.Left);
        Assert.Equal(1037, actual.Top);
        Assert.True(actual.FitsInTaskbarBand);
    }

    [Fact]
    public void Centered_without_visible_widgets_uses_taskbar_far_left()
    {
        var actual = TaskbarFloatingPlacement.PlaceHorizontal(
            taskbarLeft: 0,
            taskbarTop: 1032,
            taskbarRight: 1920,
            taskbarHeight: 48,
            islandHeight: 38,
            centered: true,
            startLeft: 760,
            startRight: 808,
            widgetsLeft: null,
            widgetsRight: null,
            taskButtonsRight: 1120,
            notificationLeft: 1600,
            islandWidth: 225,
            gap: 6);

        Assert.Equal(6, actual.Left);
        Assert.Equal(1037, actual.Top);
        Assert.True(actual.FitsInTaskbarBand);
    }

    [Fact]
    public void Left_aligned_with_widgets_aligns_right_edge_to_widgets_left()
    {
        var actual = TaskbarFloatingPlacement.PlaceHorizontal(
            taskbarLeft: 0,
            taskbarTop: 1032,
            taskbarRight: 1920,
            taskbarHeight: 48,
            islandHeight: 38,
            centered: false,
            startLeft: 0,
            startRight: 48,
            widgetsLeft: 1500,
            widgetsRight: 1548,
            taskButtonsRight: 360,
            notificationLeft: 1600,
            islandWidth: 225,
            gap: 6);

        Assert.Equal(1269, actual.Left);
        Assert.Equal(1037, actual.Top);
        Assert.True(actual.FitsInTaskbarBand);
        Assert.Equal(1500 - 6, actual.Left + 225);
    }

    [Fact]
    public void Left_aligned_with_widgets_reports_no_fit_when_widgets_slot_overlaps_task_buttons()
    {
        var actual = TaskbarFloatingPlacement.PlaceHorizontal(
            taskbarLeft: 0,
            taskbarTop: 1032,
            taskbarRight: 1920,
            taskbarHeight: 48,
            islandHeight: 38,
            centered: false,
            startLeft: 0,
            startRight: 48,
            widgetsLeft: 350,
            widgetsRight: 398,
            taskButtonsRight: 360,
            notificationLeft: 1600,
            islandWidth: 225,
            gap: 6);

        Assert.Equal(119, actual.Left);
        Assert.Equal(1037, actual.Top);
        Assert.False(actual.FitsInTaskbarBand);
    }

    [Fact]
    public void Left_aligned_with_widgets_reports_no_fit_when_notification_precedes_widgets_slot()
    {
        var actual = TaskbarFloatingPlacement.PlaceHorizontal(
            taskbarLeft: 0,
            taskbarTop: 1032,
            taskbarRight: 1920,
            taskbarHeight: 48,
            islandHeight: 38,
            centered: false,
            startLeft: 0,
            startRight: 48,
            widgetsLeft: 1500,
            widgetsRight: 1548,
            taskButtonsRight: 360,
            notificationLeft: 1000,
            islandWidth: 225,
            gap: 6);

        Assert.Equal(1269, actual.Left);
        Assert.Equal(1037, actual.Top);
        Assert.False(actual.FitsInTaskbarBand);
    }

    [Theory]
    [InlineData(1200)]
    [InlineData(1000)]
    public void Left_aligned_with_widgets_reports_no_fit_when_notification_reaches_task_buttons(
        int notificationLeft)
    {
        var actual = TaskbarFloatingPlacement.PlaceHorizontal(
            taskbarLeft: 0,
            taskbarTop: 1032,
            taskbarRight: 1920,
            taskbarHeight: 48,
            islandHeight: 38,
            centered: false,
            startLeft: 0,
            startRight: 48,
            widgetsLeft: 1500,
            widgetsRight: 1548,
            taskButtonsRight: 1200,
            notificationLeft: notificationLeft,
            islandWidth: 225,
            gap: 6);

        Assert.Equal(1269, actual.Left);
        Assert.Equal(1037, actual.Top);
        Assert.False(actual.FitsInTaskbarBand);
    }

    [Fact]
    public void Blocked_widgets_target_is_clamped_on_screen_while_reporting_no_fit()
    {
        var actual = TaskbarFloatingPlacement.PlaceHorizontal(
            taskbarLeft: 0,
            taskbarTop: 1032,
            taskbarRight: 300,
            taskbarHeight: 48,
            islandHeight: 38,
            centered: false,
            startLeft: 0,
            startRight: 48,
            widgetsLeft: 150,
            widgetsRight: 198,
            taskButtonsRight: 60,
            notificationLeft: 280,
            islandWidth: 225,
            gap: 6);

        Assert.Equal(6, actual.Left);
        Assert.Equal(1037, actual.Top);
        Assert.False(actual.FitsInTaskbarBand);
    }

    [Fact]
    public void Left_aligned_without_widgets_aligns_right_edge_to_notification_area()
    {
        var actual = TaskbarFloatingPlacement.PlaceHorizontal(
            taskbarLeft: 0,
            taskbarTop: 1032,
            taskbarRight: 1920,
            taskbarHeight: 48,
            islandHeight: 38,
            centered: false,
            startLeft: 0,
            startRight: 48,
            widgetsLeft: null,
            widgetsRight: null,
            taskButtonsRight: 360,
            notificationLeft: 1600,
            islandWidth: 225,
            gap: 6);

        Assert.Equal(1369, actual.Left);
        Assert.Equal(1037, actual.Top);
        Assert.True(actual.FitsInTaskbarBand);
        Assert.Equal(1600 - 6, actual.Left + 225);
    }

    [Fact]
    public void Left_aligned_without_widgets_uses_taskbar_right_when_notification_is_missing()
    {
        var actual = TaskbarFloatingPlacement.PlaceHorizontal(
            taskbarLeft: 0,
            taskbarTop: 1032,
            taskbarRight: 1920,
            taskbarHeight: 48,
            islandHeight: 38,
            centered: false,
            startLeft: 0,
            startRight: 48,
            widgetsLeft: null,
            widgetsRight: null,
            taskButtonsRight: 360,
            notificationLeft: null,
            islandWidth: 225,
            gap: 6);

        Assert.Equal(1689, actual.Left);
        Assert.Equal(1037, actual.Top);
        Assert.True(actual.FitsInTaskbarBand);
    }

    [Fact]
    public void Left_aligned_without_widgets_reports_no_fit_when_task_buttons_reach_notification_slot()
    {
        var actual = TaskbarFloatingPlacement.PlaceHorizontal(
            taskbarLeft: 0,
            taskbarTop: 1032,
            taskbarRight: 1920,
            taskbarHeight: 48,
            islandHeight: 38,
            centered: false,
            startLeft: 0,
            startRight: 48,
            widgetsLeft: null,
            widgetsRight: null,
            taskButtonsRight: 1400,
            notificationLeft: 1600,
            islandWidth: 225,
            gap: 6);

        Assert.Equal(1369, actual.Left);
        Assert.False(actual.FitsInTaskbarBand);
    }

    [Fact]
    public void Left_aligned_without_widgets_reports_no_fit_when_task_buttons_reach_taskbar_fallback()
    {
        var actual = TaskbarFloatingPlacement.PlaceHorizontal(
            taskbarLeft: 0,
            taskbarTop: 1032,
            taskbarRight: 1920,
            taskbarHeight: 48,
            islandHeight: 38,
            centered: false,
            startLeft: 0,
            startRight: 48,
            widgetsLeft: null,
            widgetsRight: null,
            taskButtonsRight: 1700,
            notificationLeft: null,
            islandWidth: 225,
            gap: 6);

        Assert.Equal(1689, actual.Left);
        Assert.False(actual.FitsInTaskbarBand);
    }

    [Fact]
    public void Placement_reports_when_requested_slot_cannot_avoid_taskbar_elements()
    {
        var actual = TaskbarFloatingPlacement.PlaceHorizontal(
            taskbarLeft: 0,
            taskbarTop: 1032,
            taskbarRight: 1920,
            taskbarHeight: 48,
            islandHeight: 38,
            centered: true,
            startLeft: 400,
            startRight: 448,
            widgetsLeft: 0,
            widgetsRight: 350,
            taskButtonsRight: 900,
            notificationLeft: 1600,
            islandWidth: 225,
            gap: 6);

        Assert.False(actual.FitsInTaskbarBand);
    }

    [Theory]
    [InlineData(38, 38)]
    [InlineData(32, 38)]
    public void Horizontal_taskbar_never_offsets_an_island_that_is_not_shorter(
        int taskbarHeight,
        int islandHeight)
    {
        var actual = TaskbarFloatingPlacement.PlaceHorizontal(
            taskbarLeft: 0,
            taskbarTop: 1032,
            taskbarRight: 1920,
            taskbarHeight: taskbarHeight,
            islandHeight: islandHeight,
            centered: true,
            startLeft: 760,
            startRight: 808,
            widgetsLeft: 0,
            widgetsRight: 180,
            taskButtonsRight: 1120,
            notificationLeft: 1600,
            islandWidth: 225,
            gap: 6);

        Assert.Equal(1032, actual.Top);
    }

    [Fact]
    public void Legacy_horizontal_placement_stays_left_of_widgets_and_vertically_centered()
    {
        var actual = TaskbarFloatingPlacement.PlaceLegacyHorizontal(
            taskbarLeft: 0,
            taskbarTop: 1032,
            taskbarHeight: 48,
            widgetsLeft: 180,
            widgetsRight: 228,
            islandWidth: 160,
            islandHeight: 38,
            gap: 6);

        Assert.Equal(14, actual.Left);
        Assert.Equal(1037, actual.Top);
        Assert.True(actual.FitsInTaskbarBand);
    }

    [Theory]
    [InlineData(760, 808, false, true)]
    [InlineData(100, 148, false, true)]
    [InlineData(6, 54, true, false)]
    [InlineData(null, null, true, true)]
    public void Alignment_uses_snapshot_start_position_before_registry_fallback(
        int? startLeft,
        int? startRight,
        bool registryCentered,
        bool expected)
    {
        var actual = TaskbarFloatingPlacement.IsCenteredFromSnapshot(
            taskbarLeft: 0,
            taskbarRight: 1920,
            startLeft: startLeft,
            startRight: startRight,
            registryCentered: registryCentered);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void Reorder_restack_requires_top_level_container_and_wrong_z_order(
        bool isTopLevelContainer,
        bool islandIsAboveTaskbar,
        bool expected)
    {
        Assert.Equal(expected, TaskbarFloatingPlacement.ShouldRestackForReorder(
            isTopLevelContainer: isTopLevelContainer,
            islandIsAboveTaskbar: islandIsAboveTaskbar));
    }

    [Fact]
    public void Same_visible_placement_is_reapplied_when_z_order_restack_is_requested()
    {
        var placement = new TaskbarFloatingPlacement.Result(
            Left: 186,
            Top: 1032,
            FitsInTaskbarBand: true);

        Assert.True(TaskbarFloatingPlacement.ShouldApply(
            previous: placement,
            current: placement,
            isWindowVisible: true,
            forceRestack: true));
        Assert.False(TaskbarFloatingPlacement.ShouldApply(
            previous: placement,
            current: placement,
            isWindowVisible: true,
            forceRestack: false));
    }
}
