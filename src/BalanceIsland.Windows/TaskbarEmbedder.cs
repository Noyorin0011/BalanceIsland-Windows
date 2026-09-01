using System.Runtime.InteropServices;
using System.Windows.Automation;
using Microsoft.Win32;

namespace BalanceIsland.Windows;

/// <summary>
/// Hosts the island HWND under Explorer's taskbar HWND. This is intentionally isolated from
/// the WPF window so the supported floating mode remains available if Explorer internals move.
/// </summary>
public sealed class TaskbarEmbedder
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsChild = 0x40000000L;
    private const long WsPopup = 0x80000000L;
    private const long WsClipSiblings = 0x04000000L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const int SwShowNoActivate = 4;
    private const int Gap = 6;
    private const int TrayClearance = 42;
    private const int VerticalContentHeightDip = 78;
    private const int MinimumWidthDip = 120;
    private const string TaskbarAlignmentKey =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    private IntPtr _window;
    private IntPtr _taskbar;
    private long _originalStyle;
    private long _originalExStyle;
    private bool _stylesCaptured;

    public bool IsAttached => _window != IntPtr.Zero && IsWindow(_window) &&
                              _taskbar != IntPtr.Zero && GetParent(_window) == _taskbar;

    public int GetPreferredFloatingLeft(IntPtr taskbar, int fallbackLeft, int gap)
    {
        if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out var taskbarRect))
            return fallbackLeft;

        // Win11 can rebuild/reposition Start/Widgets immediately after TaskbarAl changes.
        // Read fresh UIA geometry so alignment changes are not delayed by stale coordinates.
        var geometry = ReadGeometry(taskbar, taskbarRect);
        var dpi = GetDpiForWindow(taskbar);
        if (dpi == 0) dpi = 96;
        var islandWidthDip = System.Windows.Application.Current?.Windows
            .OfType<TaskbarIslandWindow>()
            .FirstOrDefault()?.ActualWidth ?? 160d;
        var islandWidth = Math.Max(1, (int)Math.Round(islandWidthDip * dpi / 96d));

        return TaskbarFloatingPlacement.PreferredLeft(
            fallbackLeft,
            geometry.WidgetsButton.IsValid ? geometry.WidgetsButton.Left : null,
            geometry.WidgetsButton.IsValid ? geometry.WidgetsButton.Right : null,
            islandWidth,
            gap);
    }

    public TaskbarAttachResult AttachOrUpdate(IntPtr window, double desiredWidthDip, double desiredHeightDip)
    {
        if (window == IntPtr.Zero || !IsWindow(window))
            return TaskbarAttachResult.Failed("浮岛窗口尚未就绪");

        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out var taskbarRect))
            return TaskbarAttachResult.Failed("Explorer 任务栏尚未就绪");

        var taskbarWidth = taskbarRect.Right - taskbarRect.Left;
        var taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
        var isVertical = taskbarHeight > taskbarWidth;

        var dpi = GetDpiForWindow(taskbar);
        if (dpi == 0) dpi = 96;
        var scale = dpi / 96d;
        var desiredWidth = Math.Max(1, (int)Math.Round(desiredWidthDip * scale));
        var desiredHeight = Math.Max(1, (int)Math.Round(desiredHeightDip * scale));
        var minimumWidth = Math.Max(1, (int)Math.Round(MinimumWidthDip * scale));

        // Always read current geometry here. Win10 taskbar docking/orientation changes can move
        // Start, task buttons and notification controls synchronously; a time cache made the
        // island visibly lag behind those changes.
        var geometry = ReadGeometry(taskbar, taskbarRect);
        var centered = IsCenteredTaskbar(geometry.StartButton, taskbarRect);
        var notificationLeft = geometry.NotificationArea.IsValid
            ? geometry.NotificationArea.Left
            : FindNotificationAreaLeft(taskbar, taskbarRect);
        if (notificationLeft <= taskbarRect.Left) notificationLeft = taskbarRect.Right;

        int x;
        int y;
        int width;
        int height;
        if (isVertical)
        {
            var margin = Math.Max(1, (int)Math.Round(Gap * scale));
            var clearance = Math.Max(1, (int)Math.Round(TrayClearance * scale));
            var minimumContentHeight = Math.Max(1, (int)Math.Round(VerticalContentHeightDip * scale));
            int? notificationTopLocal = geometry.NotificationArea.IsValid
                ? Math.Max(0, geometry.NotificationArea.Top - taskbarRect.Top)
                : null;

            var placement = TaskbarVerticalPlacement.Place(
                taskbarWidth,
                taskbarHeight,
                desiredWidth,
                desiredHeight,
                margin,
                notificationTopLocal,
                clearance,
                minimumContentHeight);
            x = placement.X;
            y = placement.Y;
            width = placement.Width;
            height = placement.Height;
        }
        else
        {
            height = Math.Min(desiredHeight, Math.Max(1, taskbarHeight - 4));
            int left;
            int right;
            if (centered)
            {
                left = Math.Max(taskbarRect.Left + 8,
                    geometry.WidgetsButton.IsValid ? geometry.WidgetsButton.Right + Gap : taskbarRect.Left + 8);
                right = Math.Min(notificationLeft - Gap,
                    geometry.TaskButtons.IsValid ? geometry.TaskButtons.Left - Gap : taskbarRect.Left + taskbarWidth / 2 - Gap);
            }
            else
            {
                left = geometry.TaskButtons.IsValid
                    ? geometry.TaskButtons.Right + Gap
                    : geometry.StartButton.IsValid ? geometry.StartButton.Right + Gap : taskbarRect.Left + taskbarHeight + Gap;
                right = notificationLeft - Gap - Math.Max(1, (int)Math.Round(TrayClearance * scale));
            }

            var available = right - left;
            if (available < minimumWidth)
                return TaskbarAttachResult.Failed("任务栏没有足够的安全空位，已回退悬浮模式");

            width = Math.Min(desiredWidth, available);
            var xScreen = centered ? left : right - width;
            x = xScreen - taskbarRect.Left;
            y = Math.Max(0, (taskbarHeight - height) / 2);
        }

        CaptureStyles(window);
        var style = GetWindowLongPtr(window, GwlStyle).ToInt64();
        SetWindowLongPtr(window, GwlStyle, new IntPtr((style & ~WsPopup) | WsChild | WsClipSiblings));
        var exStyle = GetWindowLongPtr(window, GwlExStyle).ToInt64();
        SetWindowLongPtr(window, GwlExStyle, new IntPtr(exStyle | WsExToolWindow | WsExNoActivate));

        if (GetParent(window) != taskbar)
        {
            SetLastError(0);
            SetParent(window, taskbar);
            if (GetParent(window) != taskbar)
            {
                RestoreWindow(window);
                return TaskbarAttachResult.Failed($"Explorer 拒绝嵌入（Win32 {Marshal.GetLastWin32Error()}）");
            }
        }

        if (!SetWindowPos(window, IntPtr.Zero, x, y, width, height,
                SwpNoActivate | SwpFrameChanged | SwpShowWindow))
        {
            RestoreWindow(window);
            return TaskbarAttachResult.Failed($"任务栏定位失败（Win32 {Marshal.GetLastWin32Error()}）");
        }

        ShowWindow(window, SwShowNoActivate);
        _window = window;
        _taskbar = taskbar;
        var label = isVertical
            ? "已嵌入任务栏（垂直），位于隐藏图标/通知区域上方"
            : centered ? "已嵌入任务栏左侧" : "已嵌入通知区域左侧（预留系统托盘间距）";
        return TaskbarAttachResult.Succeeded(label, width * 96d / dpi);
    }

    public void Detach()
    {
        if (_window != IntPtr.Zero && IsWindow(_window)) RestoreWindow(_window);
        _window = IntPtr.Zero;
        _taskbar = IntPtr.Zero;
    }

    private void CaptureStyles(IntPtr window)
    {
        if (_stylesCaptured && _window == window) return;
        _window = window;
        _originalStyle = GetWindowLongPtr(window, GwlStyle).ToInt64();
        _originalExStyle = GetWindowLongPtr(window, GwlExStyle).ToInt64();
        _stylesCaptured = true;
    }

    private void RestoreWindow(IntPtr window)
    {
        SetParent(window, IntPtr.Zero);
        if (_stylesCaptured)
        {
            SetWindowLongPtr(window, GwlStyle, new IntPtr(_originalStyle));
            SetWindowLongPtr(window, GwlExStyle, new IntPtr(_originalExStyle));
        }
        SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0,
            0x0001 | 0x0002 | SwpNoActivate | SwpFrameChanged);
    }

    private static bool IsCenteredTaskbar(Span startButton, NativeRect taskbarRect)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(TaskbarAlignmentKey);
            if (key?.GetValue("TaskbarAl") is int value) return value != 0;
        }
        catch { }

        return startButton.IsValid && startButton.Left > taskbarRect.Left +
            (taskbarRect.Right - taskbarRect.Left) / 4;
    }

    private static TaskbarGeometry ReadGeometry(IntPtr taskbar, NativeRect taskbarRect)
    {
        try
        {
            GetWindowThreadProcessId(taskbar, out var taskbarProcessId);
            var root = AutomationElement.FromHandle(taskbar);
            var elements = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            var start = Span.Invalid;
            var widgets = Span.Invalid;
            var notification = Span.Invalid;
            var buttons = new List<Span>();

            foreach (AutomationElement element in elements)
            {
                try
                {
                    var current = element.Current;
                    if (current.ProcessId != taskbarProcessId || current.IsOffscreen) continue;
                    var rectangle = current.BoundingRectangle;
                    if (rectangle.IsEmpty || rectangle.Right <= taskbarRect.Left ||
                        rectangle.Left >= taskbarRect.Right || rectangle.Bottom <= taskbarRect.Top ||
                        rectangle.Top >= taskbarRect.Bottom) continue;

                    var span = new Span(
                        (int)Math.Floor(rectangle.Left),
                        (int)Math.Floor(rectangle.Top),
                        (int)Math.Ceiling(rectangle.Right),
                        (int)Math.Ceiling(rectangle.Bottom));
                    var automationId = current.AutomationId;
                    if (automationId == "StartButton") start = span;
                    else if (automationId == "WidgetsButton") widgets = span;
                    else if (automationId is "SystemTrayIcon" or "NotifyItemIcon")
                        notification = notification.Union(span);
                    else if (current.ControlType == ControlType.Button) buttons.Add(span);
                }
                catch (ElementNotAvailableException) { }
                catch (InvalidOperationException) { }
            }

            var taskButtons = ResolveContiguousButtons(start, buttons);
            return new TaskbarGeometry(start, widgets, taskButtons, notification);
        }
        catch
        {
            return TaskbarGeometry.Empty;
        }
    }

    private static Span ResolveContiguousButtons(Span start, List<Span> buttons)
    {
        if (buttons.Count == 0) return Span.Invalid;
        var anchor = start.IsValid ? start.Right : buttons.Min(value => value.Left);
        var left = start.IsValid ? start.Left : anchor;
        var right = anchor;
        var top = start.IsValid ? start.Top : buttons.Min(value => value.Top);
        var bottom = start.IsValid ? start.Bottom : buttons.Max(value => value.Bottom);
        foreach (var button in buttons.OrderBy(value => value.Left))
        {
            if (button.Right <= anchor) continue;
            if (button.Left > right + 18) break;
            left = Math.Min(left, button.Left);
            right = Math.Max(right, button.Right);
            top = Math.Min(top, button.Top);
            bottom = Math.Max(bottom, button.Bottom);
        }
        return right > anchor ? new Span(left, top, right, bottom) : start;
    }

    private static int FindNotificationAreaLeft(IntPtr taskbar, NativeRect taskbarRect)
    {
        var notification = FindDescendantWindow(taskbar, "TrayNotifyWnd");
        return notification != IntPtr.Zero && GetWindowRect(notification, out var rectangle)
            ? rectangle.Left : taskbarRect.Right;
    }

    private static IntPtr FindDescendantWindow(IntPtr parent, string className)
    {
        var direct = FindWindowEx(parent, IntPtr.Zero, className, null);
        if (direct != IntPtr.Zero) return direct;
        var child = FindWindowEx(parent, IntPtr.Zero, null, null);
        while (child != IntPtr.Zero)
        {
            var nested = FindDescendantWindow(child, className);
            if (nested != IntPtr.Zero) return nested;
            child = FindWindowEx(parent, child, null, null);
        }
        return IntPtr.Zero;
    }

    private readonly record struct TaskbarGeometry(
        Span StartButton, Span WidgetsButton, Span TaskButtons, Span NotificationArea)
    {
        public static TaskbarGeometry Empty => new(Span.Invalid, Span.Invalid, Span.Invalid, Span.Invalid);
    }

    private readonly record struct Span(int Left, int Top, int Right, int Bottom)
    {
        public bool IsValid => Right > Left && Bottom > Top;
        public static Span Invalid => new(0, 0, 0, 0);
        public Span Union(Span other) => !IsValid ? other : !other.IsValid ? this
            : new Span(
                Math.Min(Left, other.Left),
                Math.Min(Top, other.Top),
                Math.Max(Right, other.Right),
                Math.Max(Bottom, other.Bottom));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(
        IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr newLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("kernel32.dll")]
    private static extern void SetLastError(uint errorCode);
}

public sealed record TaskbarAttachResult(bool Success, string Message, double WidthDip)
{
    public static TaskbarAttachResult Succeeded(string message, double widthDip) =>
        new(true, message, widthDip);
    public static TaskbarAttachResult Failed(string message) => new(false, message, 0);
}