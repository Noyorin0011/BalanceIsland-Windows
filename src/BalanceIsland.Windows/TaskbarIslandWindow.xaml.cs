using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace BalanceIsland.Windows;

public partial class TaskbarIslandWindow : Window
{
    private const double FloatingWidth = 250;
    private const double FloatingHeight = 44;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopMost = new(-1);
    private static readonly IntPtr HwndBottom = new(1);

    private readonly BalanceCoordinator _coordinator;
    private readonly TaskbarEmbedder _embedder = new();
    private readonly DispatcherTimer _carouselTimer;
    private readonly DispatcherTimer _layoutTimer;
    private IslandDisplayMode _displayMode;
    private uint _taskbarCreatedMessage;
    private string _lastModeStatus = "";
    private FloatingPlacement? _lastFloatingPlacement;
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
        _coordinator.StateChanged += (_, _) => Dispatcher.Invoke(Render);
        _displayMode = ResolveDisplayMode(_coordinator.State.IslandDisplayMode);
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
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) ApplyDisplayMode();
            else _embedder.Detach();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(handle);
        source?.AddHook(WindowProc);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle,
            new IntPtr(style | WsExToolWindow | WsExNoActivate));
    }

    protected override void OnClosed(EventArgs e)
    {
        _carouselTimer.Stop();
        _layoutTimer.Stop();
        _embedder.Detach();
        base.OnClosed(e);
    }

    public void SetDisplayMode(IslandDisplayMode mode)
    {
        mode = ResolveDisplayMode(mode);
        if (_displayMode != mode) _embedder.Detach();
        _displayMode = mode;
        ApplyDisplayMode();
    }

    public void ReconcileDisplayMode() => ApplyDisplayMode();

    private void Island_LeftClick(object sender, MouseButtonEventArgs e) => Next();

    private void Island_RightClick(object sender, MouseButtonEventArgs e)
    {
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
        if (snapshots.Count == 0)
        {
            ProviderIconText.Text = "BI";
            IslandTitleText.Text = "Balance Island";
            IslandUsageText.Text = "请添加账户";
            return;
        }
        if (_index >= snapshots.Count) _index = 0;
        var snapshot = snapshots[_index];
        var provider = snapshot.Provider.DisplayName();
        ProviderIconText.Text = ProviderAbbreviation(snapshot.Provider);
        IslandTitleText.Text = $"{provider} · {snapshot.AccountDisplayLabel}";
        var today = snapshot.TodayUsedAmount is null
            ? ""
            : $" · 今日 {BalanceSnapshot.CurrencySymbol(snapshot.CurrencyCode)}{snapshot.TodayUsedAmount:0.00}";
        var detail = today.Length == 0 && !string.IsNullOrWhiteSpace(snapshot.SecondaryText)
            ? $" · {snapshot.SecondaryText}"
            : today;
        IslandUsageText.Text = $"{snapshot.PrimaryText}{detail}";
    }

    private static string ProviderAbbreviation(Provider provider) => provider switch
    {
        Provider.DeepSeek => "DS",
        Provider.OpenAI => "OA",
        Provider.OpenRouter => "OR",
        Provider.SiliconFlow => "SF",
        Provider.Moonshot => "KM",
        Provider.MiMo => "MI",
        Provider.Anthropic => "AN",
        Provider.Gemini => "GE",
        Provider.XAI => "xA",
        _ => "BI"
    };

    private void ApplyDisplayMode()
    {
        if (!IsLoaded || !IsVisible) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !IsWindow(handle)) return;

        if (_displayMode == IslandDisplayMode.TaskbarEmbedded &&
            !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var result = _embedder.AttachOrUpdate(handle, FloatingWidth, FloatingHeight);
            if (result.Success)
            {
                ReportModeStatus(result.Message);
                return;
            }

            _embedder.Detach();
            Width = FloatingWidth;
            Height = FloatingHeight;
            PositionFloatingOverTaskbar();
            ReportModeStatus(result.Message);
            return;
        }

        _embedder.Detach();
        Width = FloatingWidth;
        Height = FloatingHeight;
        PositionFloatingOverTaskbar();
        ReportModeStatus(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            ? "Windows 11 · 透明悬浮模式"
            : "透明悬浮在任务栏区域");
    }

    private static IslandDisplayMode ResolveDisplayMode(IslandDisplayMode mode) =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ||
        mode == IslandDisplayMode.WidgetsButtonOverlay
            ? IslandDisplayMode.Floating
            : mode;

    private void ReportModeStatus(string status)
    {
        if (_lastModeStatus == status) return;
        _lastModeStatus = status;
        DisplayModeStatusChanged?.Invoke(this, status);
    }

    private void PositionFloatingOverTaskbar()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (handle == IntPtr.Zero || taskbar == IntPtr.Zero ||
            !GetWindowRect(taskbar, out var taskbarRect))
        {
            var x = (int)Math.Round(SystemParameters.WorkArea.Right - FloatingWidth - 16);
            var y = (int)Math.Round(SystemParameters.WorkArea.Bottom - FloatingHeight);
            SetWindowPos(handle, HwndTopMost, x, y, (int)FloatingWidth, (int)FloatingHeight,
                SwpNoActivate | SwpShowWindow);
            return;
        }

        var dpi = GetDpiForWindow(taskbar);
        if (dpi == 0) dpi = 96;
        var scale = dpi / 96d;
        var widthPx = Math.Max(1, (int)Math.Round(FloatingWidth * scale));
        var heightPx = Math.Max(1, (int)Math.Round(FloatingHeight * scale));
        var taskbarWidth = taskbarRect.Right - taskbarRect.Left;
        var taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
        int x;
        int y;
        if (taskbarWidth >= taskbarHeight)
        {
            // Leave the right-most area to the notification icons and clock.
            x = taskbarRect.Right - widthPx - (int)Math.Round(190 * scale);
            x = Math.Max(taskbarRect.Left + (int)Math.Round(8 * scale), x);
            y = taskbarRect.Top + Math.Max(0, (taskbarHeight - heightPx) / 2);
        }
        else
        {
            x = taskbarRect.Left + Math.Max(0, (taskbarWidth - widthPx) / 2);
            y = taskbarRect.Bottom - heightPx - (int)Math.Round(190 * scale);
            y = Math.Max(taskbarRect.Top + (int)Math.Round(8 * scale), y);
        }

        var target = new Rect { Left = x, Top = y, Right = x + widthPx, Bottom = y + heightPx };
        var taskbarPresented = IsTaskbarPresented(taskbar, target, handle);
        var placement = new FloatingPlacement(x, y, widthPx, heightPx, taskbarPresented);
        if (_lastFloatingPlacement == placement && IsWindowVisible(handle)) return;

        SetWindowPos(handle, taskbarPresented ? HwndTopMost : HwndBottom,
            x, y, widthPx, heightPx, SwpNoActivate | SwpShowWindow);
        _lastFloatingPlacement = placement;
    }

    private static bool IsTaskbarPresented(IntPtr taskbar, Rect overlayTarget, IntPtr overlayWindow)
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
            var hit = WindowFromPoint(new Point { X = x, Y = y });
            if (hit == IntPtr.Zero || hit == overlayWindow || IsChild(overlayWindow, hit)) continue;
            if (hit == taskbar || IsChild(taskbar, hit)) return true;
        }
        return false;
    }

    private IntPtr WindowProc(
        IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarCreatedMessage != 0 && message == _taskbarCreatedMessage)
        {
            Dispatcher.BeginInvoke(ApplyDisplayMode);
            return IntPtr.Zero;
        }
        if (message == WmMouseActivate)
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

        public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private readonly record struct FloatingPlacement(
        int X, int Y, int Width, int Height, bool TaskbarPresented);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

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
