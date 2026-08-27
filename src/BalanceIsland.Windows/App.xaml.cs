using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace BalanceIsland.Windows;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private TaskbarIslandWindow? _island;
    private BalanceCoordinator? _coordinator;
    private SystemThemeManager? _themeManager;
    private DispatcherTimer? _islandHealthTimer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _themeManager = new SystemThemeManager(Resources, Dispatcher);

        var store = new AppDataStore();
        var credentials = new WindowsCredentialStore();
        var client = new ProviderClient();
        _coordinator = new BalanceCoordinator(store, credentials, client);

        _mainWindow = new MainWindow(_coordinator);
        _themeManager.Track(_mainWindow);
        CreateIslandWindow();
        _mainWindow.IslandVisibilityRequested += (_, visible) => SetIslandVisible(visible);
        _mainWindow.IslandDisplayModeRequested += (_, mode) => SetIslandDisplayMode(mode);
        _mainWindow.Closing += (_, args) =>
        {
            if (_coordinator.IsExiting) return;
            args.Cancel = true;
            _mainWindow.Hide();
        };

        CreateTrayIcon();
        _islandHealthTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _islandHealthTimer.Tick += (_, _) => EnsureIslandHealthy();
        _islandHealthTimer.Start();
        _coordinator.AlertRaised += (_, alert) => Dispatcher.Invoke(() =>
        {
            if (_trayIcon is null) return;
            _trayIcon.BalloonTipTitle = alert.Title;
            _trayIcon.BalloonTipText = alert.Message;
            _trayIcon.BalloonTipIcon = Forms.ToolTipIcon.Warning;
            _trayIcon.ShowBalloonTip(5000);
        });
        if (_coordinator.State.IslandEnabled) _island?.Show();
        _mainWindow.Show();
        _ = _coordinator.RefreshDueAsync(force: false);
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 Balance Island", null, (_, _) => ShowMainWindow());
        menu.Items.Add("立即刷新", null, async (_, _) =>
        {
            if (_coordinator is not null) await _coordinator.RefreshDueAsync(force: true);
        });
        menu.Items.Add("显示/隐藏任务栏浮岛", null, (_, _) =>
        {
            SetIslandVisible(!(_coordinator?.State.IslandEnabled ?? false));
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "Balance Island",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void SetIslandVisible(bool visible)
    {
        if (_coordinator is null) return;
        _coordinator.SetIslandEnabled(visible);
        _mainWindow?.UpdateIslandControls(visible, _coordinator.State.IslandDisplayMode);
        if (visible)
        {
            CreateIslandWindow();
            _island?.Show();
            _island?.SetDisplayMode(_coordinator.State.IslandDisplayMode);
        }
        else
        {
            _island?.Hide();
        }
    }

    private void SetIslandDisplayMode(IslandDisplayMode mode)
    {
        if (_coordinator is null) return;
        _coordinator.SetIslandDisplayMode(mode);
        _mainWindow?.UpdateIslandControls(_coordinator.State.IslandEnabled, mode);
        CreateIslandWindow();
        _island?.SetDisplayMode(mode);
    }

    private void CreateIslandWindow()
    {
        if (_coordinator is null || _themeManager is null) return;
        if (_island is not null && (!_island.IsLoaded || _island.IsNativeHandleAlive)) return;

        var island = new TaskbarIslandWindow(_coordinator);
        island.DisplayModeStatusChanged += (_, status) =>
            _mainWindow?.UpdateIslandModeStatus(status);
        island.Closed += (_, _) =>
        {
            if (ReferenceEquals(_island, island)) _island = null;
            if (!_coordinator.IsExiting && _coordinator.State.IslandEnabled)
                Dispatcher.BeginInvoke(CreateIslandWindow);
        };
        _themeManager.Track(island);
        _island = island;
    }

    private void EnsureIslandHealthy()
    {
        if (_coordinator is null || !_coordinator.State.IslandEnabled) return;
        if (_island is null || !_island.IsNativeHandleAlive)
        {
            CreateIslandWindow();
            _island?.Show();
            return;
        }
        _island.ReconcileDisplayMode();
    }

    private void ExitApplication()
    {
        if (_coordinator is not null) _coordinator.IsExiting = true;
        _islandHealthTimer?.Stop();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _island?.Close();
        _mainWindow?.Close();
        _coordinator?.Dispose();
        _themeManager?.Dispose();
        Shutdown();
    }
}
