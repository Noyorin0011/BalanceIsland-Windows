using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;
using MediaVisualTreeHelper = System.Windows.Media.VisualTreeHelper;

namespace BalanceIsland.Windows;

public partial class TaskbarIslandWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExNoActivate = 0x08000000L;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMinimizeStart = 0x0016;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private const uint EventObjectLocationChange = 0x800B;
    private const int ObjidWindow = 0;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const double MinimumWidth = 160;
    private const double MinimumHeight = 28;
    private const double MaximumWidth = 480;
    private const double MaximumHeight = 100;
    private const double FloatingTrayClearanceDip = 260;
    private static readonly IntPtr HwndTopMost = new(-1);
    private static readonly IntPtr HwndBottom = new(1);

    private readonly BalanceCoordinator _coordinator;
    private readonly TaskbarEmbedder _embedder = new();
    private readonly DispatcherTimer _carouselTimer;
    private readonly DispatcherTimer _layoutTimer;
    private readonly DispatcherTimer _eventSettleTimer;
    private readonly WinEventDelegate _winEventDelegate;
    private IslandDisplayMode _displayMode;
    private uint _taskbarCreatedMessage;
    private string _lastModeStatus = "";
    private FloatingPlacement? _lastFloatingPlacement;
    private IntPtr _foregroundHook;
    private IntPtr _minimizeHook;
    private IntPtr _locationHook;
    private bool _editingGesture;
    private int _index;

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
        _displayMode = ResolveDisplayMode(_coordinator.State.IslandDisplayMode);
        Width = Math.Clamp(_coordinator.State.IslandWidth, MinimumWidth, MaximumWidth);
        Height = Math.Clamp(_coordinator.State.IslandHeight, MinimumHeight, MaximumHeight);

        _carouselTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _carouselTimer.Tick += (_, _) => Next();
        _carouselTimer.Start();

        _layoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _layoutTimer.Tick += (_, _) => ApplyDisplayMode();
        _layoutTimer.Start();

        _eventSettleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _eventSettleTimer.Tick += (_, _) =>
        {
            _eventSettleTimer.Stop();
            ApplyDisplayMode();
        };

        _winEventDelegate = OnWinEvent;
        Loaded += (_, _) =>
        {
            Render();
            ApplyDisplayMode();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) ApplyDisplayMode();
            else _embedder.Detach();
        };
    }

    private void Coordinator_StateChanged(object? sender, EventArgs e) => Dispatcher.Invoke(() =>
    {
        _displayMode = ResolveDisplayMode(_coordinator.State.IslandDisplayMode);
        _lastFloatingPlacement = null;
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

        _foregroundHook = SetWinEventHook(
            EventSystemForeground, EventSystemForeground, IntPtr.Zero,
            _winEventDelegate, 0, 0, WineventOutOfContext);
        _minimizeHook = SetWinEventHook(
            EventSystemMinimizeStart, EventSystemMinimizeEnd, IntPtr.Zero,
            _winEventDelegate, 0, 0, WineventOutOfContext | WineventSkipOwnProcess);
        _locationHook = SetWinEventHook(
            EventObjectLocationChange, EventObjectLocationChange, IntPtr.Zero,
            _winEventDelegate, 0, 0, WineventOutOfContext | WineventSkipOwnProcess);
    }

    protected override void OnClosed(EventArgs e)
    {
        _coordinator.StateChanged -= Coordinator_StateChanged;
        _carouselTimer.Stop();
        _layoutTimer.Stop();
        _eventSettleTimer.Stop();
        if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
        if (_minimizeHook != IntPtr.Zero) UnhookWinEvent(_minimizeHook);
        if (_locationHook != IntPtr.Zero) UnhookWinEvent(_locationHook);
        _embedder.Detach();
        base.OnClosed(e);
    }

    public void SetDisplayMode(IslandDisplayMode mode)
    {
        mode = ResolveDisplayMode(mode);
        if (_displayMode != mode) _embedder.Detach();
        _displayMode = mode;
        _lastFloatingPlacement = null;
        ApplyDisplayMode();
    }

    public void ReconcileDisplayMode() => ApplyDisplayMode();

    private void IslandSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_coordinator.State.IslandEditMode ||
            e.LeftButton != MouseButtonState.Pressed ||
            IsInsideThumb(e.OriginalSource)) return;

        _editingGesture = true;
        e.Handled = true;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The mouse can be released before WPF enters its modal move loop.
        }
        finally
        {
            _editingGesture = false;
            CaptureCustomLayout();
        }
    }

    private static bool IsInsideThumb(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is Thumb) return true;
            try
            {
                current = MediaVisualTreeHelper.GetParent(current);
            }
            catch (InvalidOperationException)
            {
                current = LogicalTreeHelper.GetParent(current);
            }
        }
        return false;
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_coordinator.State.IslandEditMode || sender is not Thumb { Tag: string edge }) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rectangle)) return;

        var taskbar = FindWindow("Shell_TrayWnd", null);
        var dpi = taskbar == IntPtr.Zero ? GetDpiForWindow(handle) : GetDpiForWindow(taskbar);
        if (dpi == 0) dpi = 96;
        var scale = dpi / 96d;
        var horizontal = (int)Math.Round(e.HorizontalChange * scale);
        var vertical = (int)Math.Round(e.VerticalChange * scale);

        var left = rectangle.Left;
        var top = rectangle.Top;
        var right = rectangle.Right;
        var bottom = rectangle.Bottom;
        if (edge.Contains("Left", StringComparison.Ordinal)) left += horizontal;
        if (edge.Contains("Right", StringComparison.Ordinal)) right += horizontal;
        if (edge.Contains("Top", StringComparison.Ordinal)) top += vertical;
        if (edge.Contains("Bottom", StringComparison.Ordinal)) bottom += vertical;

        var minimumWidthPx = (int)Math.Round(MinimumWidth * scale);
        var minimumHeightPx = (int)Math.Round(MinimumHeight * scale);
        var maximumWidthPx = (int)Math.Round(MaximumWidth * scale);
        var maximumHeightPx = (int)Math.Round(MaximumHeight * scale);
        var width = Math.Clamp(right - left, minimumWidthPx, maximumWidthPx);
        var height = Math.Clamp(bottom - top, minimumHeightPx, maximumHeightPx);
        if (edge.Contains("Left", StringComparison.Ordinal)) left = right - width;
        else right = left + width;
        if (edge.Contains("Top", StringComparison.Ordinal)) top = bottom - height;
        else bottom = top + height;

        _editingGesture = true;
        _lastFloatingPlacement = null;
        SetWindowPos(handle, IntPtr.Zero, left, top, right - left, bottom - top,
            SwpNoActivate | SwpNoZOrder);
    }

    private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!_coordinator.State.IslandEditMode) return;
        _editingGesture = false;
        CaptureCustomLayout();
    }

    private void CaptureCustomLayout()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (handle == IntPtr.Zero || taskbar == IntPtr.Zero ||
            !GetWindowRect(handle, out var rectangle) ||
            !GetWindowRect(taskbar, out var taskbarRect)) return;

        var dpi = GetDpiForWindow(taskbar);
        if (dpi == 0) dpi = 96;
        var scale = dpi / 96d;
        _coordinator.SaveIslandCustomLayout(
            (rectangle.Left - taskbarRect.Left) / scale,
            (rectangle.Top - taskbarRect.Top) / scale,
            (rectangle.Right - rectangle.Left) / scale,
            (rectangle.Bottom - rectangle.Top) / scale);
        _lastFloatingPlacement = null;
    }

    private void Next()
    {
        var count = IslandAccountSelection.VisibleSnapshots(_coordinator).Count;
        _index = count == 0 ? 0 : (_index + 1) % count;
        Render();
    }

    private void Render()
    {
        var snapshots = IslandAccountSelection.VisibleSnapshots(_coordinator);
        if (snapshots.Count == 0)
        {
            ProviderIconImage.Source = null;
            ProviderIconImage.Visibility = Visibility.Collapsed;
            ProviderIconFallbackText.Visibility = Visibility.Visible;
            ProviderIconBackground.Background = new MediaSolidColorBrush(
                MediaColor.FromRgb(255, 190, 70));
            ProviderIconBackground.Padding = new Thickness(0);
            IslandTitleText.Text = "Balance Island";
            IslandUsageText.Text = "没有启用显示的账户";
            return;
        }

        if (_index >= snapshots.Count) _index = 0;
        var snapshot = snapshots[_index];
        ProviderIconImage.Source = (MediaImageSource)FindResource($"ProviderIcon.{snapshot.Provider}");
        ProviderIconImage.Visibility = Visibility.Visible;
        ProviderIconFallbackText.Visibility = Visibility.Collapsed;
        ProviderIconBackground.Background = snapshot.Provider switch
        {
            Provider.OpenAI or Provider.XAI =>
                new MediaSolidColorBrush(MediaColor.FromRgb(20, 22, 27)),
            Provider.Moonshot => MediaBrushes.White,
            _ => MediaBrushes.Transparent
        };
        ProviderIconBackground.Padding = snapshot.Provider is
            Provider.OpenAI or Provider.XAI or Provider.Moonshot
                ? new Thickness(2)
                : new Thickness(0);

        IslandTitleText.Text = $"{snapshot.Provider.DisplayName()} · {snapshot.AccountDisplayLabel}";
        var today = snapshot.TodayUsedAmount is null
            ? ""
            : $" · 今日 {BalanceSnapshot.CurrencySymbol(snapshot.CurrencyCode)}{snapshot.TodayUsedAmount:0.00}";
        var detail = today.Length == 0 && !string.IsNullOrWhiteSpace(snapshot.SecondaryText)
            ? $" · {snapshot.SecondaryText}"
            : today;
        IslandUsageText.Text = $"{snapshot.PrimaryText}{detail}";
    }

    private void ApplyDisplayMode()
    {
        if (!IsLoaded || !IsVisible) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !IsWindow(handle)) return;

        var edit = _coordinator.State.IslandEditMode;
        EditChrome.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
        IslandSurface.ToolTip = edit
            ? "拖动浮岛移动；拖拽边缘或四角缩放"
            : "已锁定并允许鼠标穿透";
        ApplyInteractionStyle(handle, edit);

        if (IsForegroundFullscreen(handle))
        {
            Opacity = 0;
            IsHitTestVisible = false;
            SetWindowPos(handle, HwndBottom, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate);
            _lastFloatingPlacement = null;
            return;
        }

        Opacity = 1;
        IsHitTestVisible = edit;
        if (_editingGesture) return;

        var size = CurrentSizeDip();
        Width = size.Width;
        Height = size.Height;

        if (edit)
        {
            _embedder.Detach();
            ApplyInteractionStyle(handle, edit: true);
            ShowActivated = true;
            Topmost = true;
            PositionFloatingOverTaskbar(forceTopmost: true);
            ReportModeStatus("编辑模式：拖动浮岛或拖拽边缘缩放；关闭后固定并穿透点击");
            return;
        }

        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = false;
        if (_displayMode == IslandDisplayMode.TaskbarEmbedded &&
            !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var result = _embedder.AttachOrUpdate(handle, size.Width, size.Height);
            if (result.Success)
            {
                ReportModeStatus(result.Message);
                return;
            }
            _embedder.Detach();
            ApplyInteractionStyle(handle, edit: false);
            PositionFloatingOverTaskbar(forceTopmost: false);
            ReportModeStatus(result.Message);
            return;
        }

        _embedder.Detach();
        ApplyInteractionStyle(handle, edit: false);
        PositionFloatingOverTaskbar(forceTopmost: false);
        ReportModeStatus("透明悬浮；全屏时隐藏，锁定后鼠标穿透");
    }

    private (double Width, double Height) CurrentSizeDip() => (
        Math.Clamp(_coordinator.State.IslandWidth, MinimumWidth, MaximumWidth),
        Math.Clamp(_coordinator.State.IslandHeight, MinimumHeight, MaximumHeight));

    private static IslandDisplayMode ResolveDisplayMode(IslandDisplayMode mode) =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            ? IslandDisplayMode.Floating
            : mode;

    private void ApplyInteractionStyle(IntPtr handle, bool edit)
    {
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        style |= WsExToolWindow;
        if (edit) style &= ~(WsExNoActivate | WsExTransparent);
        else style |= WsExNoActivate | WsExTransparent;
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    private void PositionFloatingOverTaskbar(bool forceTopmost)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var size = CurrentSizeDip();
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (handle == IntPtr.Zero || taskbar == IntPtr.Zero ||
            !GetWindowRect(taskbar, out var taskbarRect))
        {
            var fallbackX = (int)Math.Round(SystemParameters.WorkArea.Right - size.Width - 16);
            var fallbackY = (int)Math.Round(SystemParameters.WorkArea.Bottom - size.Height);
            SetWindowPos(handle, HwndTopMost, fallbackX, fallbackY,
                (int)Math.Round(size.Width), (int)Math.Round(size.Height),
                SwpNoActivate | SwpShowWindow);
            return;
        }

        var dpi = GetDpiForWindow(taskbar);
        if (dpi == 0) dpi = 96;
        var scale = dpi / 96d;
        var widthPx = Math.Max(1, (int)Math.Round(size.Width * scale));
        var heightPx = Math.Max(1, (int)Math.Round(size.Height * scale));
        var taskbarWidth = taskbarRect.Right - taskbarRect.Left;
        var taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
        int x;
        int y;

        if (_coordinator.State.IslandPositionPreset == IslandPositionPreset.Custom)
        {
            x = taskbarRect.Left + (int)Math.Round(_coordinator.State.IslandCustomLeftDip * scale);
            y = taskbarRect.Top + (int)Math.Round(_coordinator.State.IslandCustomTopDip * scale);
            ClampToVirtualScreen(ref x, ref y, widthPx, heightPx);
        }
        else if (taskbarWidth >= taskbarHeight)
        {
            var margin = Math.Max(1, (int)Math.Round(6 * scale));
            x = _coordinator.State.IslandPositionPreset switch
            {
                IslandPositionPreset.Center =>
                    taskbarRect.Left + Math.Max(0, (taskbarWidth - widthPx) / 2),
                IslandPositionPreset.Right =>
                    taskbarRect.Right - widthPx - (int)Math.Round(FloatingTrayClearanceDip * scale),
                _ => _embedder.GetPreferredFloatingLeft(
                    taskbar, taskbarRect.Left + margin, margin)
            };
            x = Math.Clamp(x, taskbarRect.Left + margin, taskbarRect.Right - widthPx - margin);
            y = taskbarRect.Top + Math.Max(0, (taskbarHeight - heightPx) / 2);
        }
        else
        {
            x = taskbarRect.Left + Math.Max(0, (taskbarWidth - widthPx) / 2);
            var margin = Math.Max(1, (int)Math.Round(6 * scale));
            y = _coordinator.State.IslandPositionPreset switch
            {
                IslandPositionPreset.Left => taskbarRect.Top + (int)Math.Round(80 * scale),
                IslandPositionPreset.Center =>
                    taskbarRect.Top + Math.Max(0, (taskbarHeight - heightPx) / 2),
                _ => taskbarRect.Bottom - heightPx -
                     (int)Math.Round(FloatingTrayClearanceDip * scale)
            };
            y = Math.Clamp(y, taskbarRect.Top + margin, taskbarRect.Bottom - heightPx - margin);
        }

        var target = new NativeRect
        {
            Left = x,
            Top = y,
            Right = x + widthPx,
            Bottom = y + heightPx
        };
        var taskbarPresented = IsTaskbarPresented(taskbar, target, handle);
        var useTopmost = forceTopmost || taskbarPresented;
        var placement = new FloatingPlacement(x, y, widthPx, heightPx, useTopmost);
        if (_lastFloatingPlacement == placement && IsWindowVisible(handle)) return;

        SetWindowPos(handle, useTopmost ? HwndTopMost : HwndBottom,
            x, y, widthPx, heightPx, SwpNoActivate | SwpShowWindow);
        _lastFloatingPlacement = placement;
    }

    private static void ClampToVirtualScreen(ref int x, ref int y, int width, int height)
    {
        const int smXVirtualScreen = 76;
        const int smYVirtualScreen = 77;
        const int smCxVirtualScreen = 78;
        const int smCyVirtualScreen = 79;
        var virtualLeft = GetSystemMetrics(smXVirtualScreen);
        var virtualTop = GetSystemMetrics(smYVirtualScreen);
        var virtualRight = virtualLeft + GetSystemMetrics(smCxVirtualScreen);
        var virtualBottom = virtualTop + GetSystemMetrics(smCyVirtualScreen);
        var visible = Math.Min(24, Math.Min(width, height));
        x = Math.Clamp(x, virtualLeft - width + visible, virtualRight - visible);
        y = Math.Clamp(y, virtualTop - height + visible, virtualBottom - visible);
    }

    private static bool IsTaskbarPresented(
        IntPtr taskbar, NativeRect overlayTarget, IntPtr overlayWindow)
    {
        if (!IsWindowVisible(taskbar) || !GetWindowRect(taskbar, out var rectangle)) return false;
        var width = rectangle.Right - rectangle.Left;
        var height = rectangle.Bottom - rectangle.Top;
        if (width < 3 || height < 1) return false;

        var y = rectangle.Top + height / 2;
        foreach (var fraction in new[] { 0.08, 0.28, 0.50, 0.72, 0.94 })
        {
            var x = rectangle.Left + Math.Clamp((int)Math.Round(width * fraction), 1, width - 1);
            if (overlayTarget.Contains(x, y)) continue;
            var hit = WindowFromPoint(new NativePoint { X = x, Y = y });
            if (hit == IntPtr.Zero || hit == overlayWindow || IsChild(overlayWindow, hit)) continue;
            if (hit == taskbar || IsChild(taskbar, hit)) return true;
        }
        return false;
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

    private void OnWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (eventType == EventObjectLocationChange)
        {
            if (objectId != ObjidWindow) return;
            var foreground = GetForegroundWindow();
            var taskbar = FindWindow("Shell_TrayWnd", null);
            if (window != foreground && window != taskbar) return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            ApplyDisplayMode();
            _eventSettleTimer.Stop();
            _eventSettleTimer.Start();
        }), DispatcherPriority.Send);
    }

    private void ReportModeStatus(string status)
    {
        if (_lastModeStatus == status) return;
        _lastModeStatus = status;
        DisplayModeStatusChanged?.Invoke(this, status);
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
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public bool Contains(int x, int y) =>
            x >= Left && x < Right && y >= Top && y < Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }

    private readonly record struct FloatingPlacement(
        int X, int Y, int Width, int Height, bool Topmost);

    private delegate void WinEventDelegate(
        IntPtr hook,
        uint eventType,
        IntPtr window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr eventHookModule,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr parent, IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr newLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
}
