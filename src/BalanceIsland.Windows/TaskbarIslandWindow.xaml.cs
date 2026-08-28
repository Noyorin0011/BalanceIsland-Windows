using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace BalanceIsland.Windows;

public partial class TaskbarIslandWindow : Window
{
    private const int AbmGetTaskbarPos = 5;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExNoActivate = 0x08000000L;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const uint MonitorDefaultToNearest = 2;
    private const double FloatingTrayClearanceDip = 260;

    private readonly BalanceCoordinator _coordinator;
    private readonly TaskbarEmbedder _embedder = new();
    private readonly DispatcherTimer _carouselTimer;
    private readonly DispatcherTimer _layoutTimer;
    private IslandDisplayMode _displayMode;
    private uint _taskbarCreatedMessage;
    private string _lastModeStatus = "";
    private int _index;
    private bool _dragging;
    private bool _layoutAppliedOnce;

    public event EventHandler<string>? DisplayModeStatusChanged;

    public bool IsNativeHandleAlive
    {
        get
        {
            var handle = new WindowInteropHelper(this).Handle;
            return handle != IntPtr.Zero && IsWindow(handle);
        }
    }

    public TaskbarIslandWindow(BalanceCoordinator coordinator)
    {
        InitializeComponent();
        _coordinator = coordinator;
        _coordinator.StateChanged += Coordinator_StateChanged;
        _displayMode = _coordinator.State.IslandDisplayMode;
        Width = Math.Clamp(_coordinator.State.IslandWidth, MinWidth, MaxWidth);
        Height = Math.Clamp(_coordinator.State.IslandHeight, MinHeight, MaxHeight);

        _carouselTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _carouselTimer.Tick += (_, _) => Next();
        _carouselTimer.Start();
        _layoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _layoutTimer.Tick += (_, _) => ApplyDisplayMode();
        _layoutTimer.Start();

        Loaded += (_, _) =>
        {
            ApplyDisplayMode();
            Render();
        };
        LocationChanged += (_, _) => PersistEditBounds();
        SizeChanged += (_, _) => PersistEditBounds();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) ApplyDisplayMode();
            else _embedder.Detach();
        };
    }

    private void Coordinator_StateChanged(object? sender, EventArgs e) => Dispatcher.Invoke(() =>
    {
        _displayMode = _coordinator.State.IslandDisplayMode;
        Render();
        ApplyDisplayMode();
    });

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        ApplyInteractionStyle(handle, _coordinator.State.IslandEditMode);
    }

    protected override void OnClosed(EventArgs e)
    {
        _coordinator.StateChanged -= Coordinator_StateChanged;
        _carouselTimer.Stop();
        _layoutTimer.Stop();
        _embedder.Detach();
        base.OnClosed(e);
    }

    public void SetDisplayMode(IslandDisplayMode mode)
    {
        _displayMode = mode;
        ApplyDisplayMode();
    }

    public void ReconcileDisplayMode() => ApplyDisplayMode();

    private void Island_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_coordinator.State.IslandEditMode || e.ButtonState != MouseButtonState.Pressed) return;
        _dragging = true;
        try { DragMove(); }
        catch (InvalidOperationException) { }
        finally
        {
            _dragging = false;
            PersistEditBounds();
        }
        e.Handled = true;
    }

    private void Island_LeftClick(object sender, MouseButtonEventArgs e)
    {
        if (_coordinator.State.IslandEditMode || _dragging) return;
        Next();
    }

    private void Island_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (_coordinator.State.IslandEditMode) return;
        if (System.Windows.Application.Current.MainWindow is { } main)
        {
            main.Show();
            if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
            main.Activate();
        }
    }

    private void Next()
    {
        var count = _coordinator.CurrentSnapshots.Count;
        _index = count == 0 ? 0 : (_index + 1) % count;
        Render();
    }

    private void Render()
    {
        var snapshots = _coordinator.CurrentSnapshots;
        IslandBorder.BorderThickness = _coordinator.State.IslandEditMode
            ? new Thickness(2) : new Thickness(1);
        IslandBorder.BorderBrush = _coordinator.State.IslandEditMode
            ? new SolidColorBrush(MediaColor.FromRgb(255, 183, 77))
            : (MediaBrush)FindResource("ControlBorder");

        if (snapshots.Count == 0)
        {
            IslandText.Text = _coordinator.State.IslandEditMode
                ? "Balance Island · 编辑模式（拖动 / 缩放）"
                : "Balance Island · 请添加账户";
            IslandText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "PrimaryText");
            return;
        }
        if (_index >= snapshots.Count) _index = 0;
        var snapshot = snapshots[_index];
        IslandText.Text = snapshot.IslandText;
        IslandText.Foreground = snapshot.Status switch
        {
            SnapshotStatus.Critical => new SolidColorBrush(MediaColor.FromRgb(255, 105, 105)),
            SnapshotStatus.Warning => new SolidColorBrush(MediaColor.FromRgb(255, 190, 92)),
            SnapshotStatus.Error => new SolidColorBrush(MediaColor.FromRgb(255, 125, 125)),
            _ => (MediaBrush)FindResource("PrimaryText")
        };
    }

    private void ApplyDisplayMode()
    {
        if (!IsLoaded || !IsVisible) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !IsWindow(handle)) return;

        var edit = _coordinator.State.IslandEditMode;
        ApplyInteractionStyle(handle, edit);
        if (IsForegroundFullscreen(handle))
        {
            Opacity = 0;
            IsHitTestVisible = false;
            return;
        }
        Opacity = 1;
        IsHitTestVisible = edit;

        var targetWidth = Math.Clamp(_coordinator.State.IslandWidth, MinWidth, MaxWidth);
        var targetHeight = Math.Clamp(_coordinator.State.IslandHeight, MinHeight, MaxHeight);
        if (Math.Abs(Width - targetWidth) > .5 && !edit) Width = targetWidth;
        if (Math.Abs(Height - targetHeight) > .5 && !edit) Height = targetHeight;

        if (edit)
        {
            _embedder.Detach();
            Topmost = true;
            ShowActivated = true;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            if (!_layoutAppliedOnce || !double.IsFinite(_coordinator.State.IslandEditLeft) ||
                !double.IsFinite(_coordinator.State.IslandEditTop))
                PositionEditAboveTaskbar();
            else if (!_dragging && !_layoutAppliedOnce)
            {
                Left = _coordinator.State.IslandEditLeft;
                Top = _coordinator.State.IslandEditTop;
            }
            _layoutAppliedOnce = true;
            ReportModeStatus("编辑模式：浮岛已提升到任务栏上方，可拖动和缩放");
            return;
        }

        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        _layoutAppliedOnce = false;
        if (_displayMode == IslandDisplayMode.TaskbarEmbedded)
        {
            Topmost = false;
            var result = _embedder.AttachOrUpdate(handle, targetWidth, targetHeight);
            if (result.Success)
            {
                if (Math.Abs(Width - result.WidthDip) > 0.5) Width = result.WidthDip;
                ReportModeStatus(result.Message);
                return;
            }

            _embedder.Detach();
            Topmost = true;
            PositionFloatingOverTaskbar();
            ReportModeStatus(result.Message);
            return;
        }

        _embedder.Detach();
        Topmost = true;
        PositionFloatingOverTaskbar();
        ReportModeStatus("悬浮在通知区域左侧；正常模式点击穿透任务栏");
    }

    private void ApplyInteractionStyle(IntPtr handle, bool edit)
    {
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        style |= WsExToolWindow;
        if (edit)
            style &= ~(WsExNoActivate | WsExTransparent);
        else
            style |= WsExNoActivate | WsExTransparent;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
    }

    private void PersistEditBounds()
    {
        if (!_coordinator.State.IslandEditMode || !IsLoaded || !double.IsFinite(Left) || !double.IsFinite(Top)) return;
        _coordinator.SaveIslandEditBounds(Left, Top, ActualWidth > 0 ? ActualWidth : Width,
            ActualHeight > 0 ? ActualHeight : Height);
    }

    private void ReportModeStatus(string status)
    {
        if (_lastModeStatus == status) return;
        _lastModeStatus = status;
        DisplayModeStatusChanged?.Invoke(this, status);
    }

    private void PositionEditAboveTaskbar()
    {
        var data = new AppBarData { cbSize = Marshal.SizeOf<AppBarData>() };
        if (SHAppBarMessage(AbmGetTaskbarPos, ref data) == IntPtr.Zero)
        {
            Left = SystemParameters.WorkArea.Right - Width - 24;
            Top = SystemParameters.WorkArea.Bottom - Height - 8;
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(this);
        var taskbarWidth = data.rc.Right - data.rc.Left;
        var taskbarHeight = data.rc.Bottom - data.rc.Top;
        var widthPx = Width * dpi.DpiScaleX;
        var heightPx = Height * dpi.DpiScaleY;
        if (taskbarWidth >= taskbarHeight)
        {
            Left = (data.rc.Right - widthPx - FloatingTrayClearanceDip * dpi.DpiScaleX) / dpi.DpiScaleX;
            Top = (data.rc.Top - heightPx - 6 * dpi.DpiScaleY) / dpi.DpiScaleY;
        }
        else
        {
            Left = (data.rc.Left - widthPx - 6 * dpi.DpiScaleX) / dpi.DpiScaleX;
            Top = (data.rc.Bottom - heightPx - 180 * dpi.DpiScaleY) / dpi.DpiScaleY;
        }
    }

    private void PositionFloatingOverTaskbar()
    {
        var data = new AppBarData { cbSize = Marshal.SizeOf<AppBarData>() };
        if (SHAppBarMessage(AbmGetTaskbarPos, ref data) == IntPtr.Zero)
        {
            Left = SystemParameters.WorkArea.Right - Width - FloatingTrayClearanceDip;
            Top = SystemParameters.WorkArea.Bottom - Height;
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var taskbarWidth = data.rc.Right - data.rc.Left;
        var taskbarHeight = data.rc.Bottom - data.rc.Top;
        var widthPx = Width * dpi.DpiScaleX;
        var heightPx = Height * dpi.DpiScaleY;
        if (taskbarWidth >= taskbarHeight)
        {
            Left = (data.rc.Right - widthPx - FloatingTrayClearanceDip * dpi.DpiScaleX) / dpi.DpiScaleX;
            Top = (data.rc.Top + (taskbarHeight - heightPx) / 2) / dpi.DpiScaleY;
        }
        else
        {
            Left = (data.rc.Left + (taskbarWidth - widthPx) / 2) / dpi.DpiScaleX;
            Top = (data.rc.Bottom - heightPx - FloatingTrayClearanceDip * dpi.DpiScaleY) / dpi.DpiScaleY;
        }
    }

    private static bool IsForegroundFullscreen(IntPtr islandHandle)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == islandHandle) return false;
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (foreground == taskbar || foreground == GetDesktopWindow()) return false;
        if (!GetWindowRect(foreground, out var windowRect)) return false;
        var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return false;
        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;
        const int tolerance = 2;
        return windowRect.Left <= info.rcMonitor.Left + tolerance &&
               windowRect.Top <= info.rcMonitor.Top + tolerance &&
               windowRect.Right >= info.rcMonitor.Right - tolerance &&
               windowRect.Bottom >= info.rcMonitor.Bottom - tolerance;
    }

    private IntPtr WindowProc(
        IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarCreatedMessage != 0 && message == _taskbarCreatedMessage)
        {
            Dispatcher.BeginInvoke(ApplyDisplayMode);
            return IntPtr.Zero;
        }
        if (message == WmMouseActivate && !_coordinator.State.IslandEditMode)
        {
            handled = true;
            return new IntPtr(MaNoActivate);
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public Rect rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
    }

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(int message, ref AppBarData data);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr newLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
