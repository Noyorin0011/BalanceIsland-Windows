using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace BalanceIsland.Windows;

public partial class TaskbarIslandWindow : Window
{
    private const int AbmGetTaskbarPos = 5;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;

    private readonly BalanceCoordinator _coordinator;
    private readonly DispatcherTimer _carouselTimer;
    private int _index;

    public TaskbarIslandWindow(BalanceCoordinator coordinator)
    {
        InitializeComponent();
        _coordinator = coordinator;
        _coordinator.StateChanged += (_, _) => Dispatcher.Invoke(Render);
        _carouselTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _carouselTimer.Tick += (_, _) => Next();
        _carouselTimer.Start();
        Loaded += (_, _) =>
        {
            PositionOverTaskbar();
            Render();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(handle);
        source?.AddHook(WindowProc);
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle,
            new IntPtr(style | WsExToolWindow | WsExNoActivate));
    }

    protected override void OnClosed(EventArgs e)
    {
        _carouselTimer.Stop();
        base.OnClosed(e);
    }

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
            IslandText.Text = "Balance Island · 请添加账户";
            IslandText.Foreground = Brushes.White;
            return;
        }
        if (_index >= snapshots.Count) _index = 0;
        var snapshot = snapshots[_index];
        IslandText.Text = snapshot.IslandText;
        IslandText.Foreground = snapshot.Status switch
        {
            SnapshotStatus.Critical => new SolidColorBrush(Color.FromRgb(255, 105, 105)),
            SnapshotStatus.Warning => new SolidColorBrush(Color.FromRgb(255, 190, 92)),
            SnapshotStatus.Error => new SolidColorBrush(Color.FromRgb(255, 125, 125)),
            _ => Brushes.White
        };
    }

    private void PositionOverTaskbar()
    {
        var data = new AppBarData { cbSize = Marshal.SizeOf<AppBarData>() };
        if (SHAppBarMessage(AbmGetTaskbarPos, ref data) == IntPtr.Zero)
        {
            Left = SystemParameters.WorkArea.Right - Width - 16;
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
            // Leave the right-most area to the notification icons and clock.
            Left = (data.rc.Right - widthPx - 190) / dpi.DpiScaleX;
            Top = (data.rc.Top + (taskbarHeight - heightPx) / 2) / dpi.DpiScaleY;
        }
        else
        {
            Left = (data.rc.Left + (taskbarWidth - widthPx) / 2) / dpi.DpiScaleX;
            Top = (data.rc.Bottom - heightPx - 190) / dpi.DpiScaleY;
        }
    }

    private static IntPtr WindowProc(
        IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
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

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(int message, ref AppBarData data);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr newLong);
}
