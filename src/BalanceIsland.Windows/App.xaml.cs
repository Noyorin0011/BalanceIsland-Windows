using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace BalanceIsland.Windows;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private TaskbarIslandWindow? _island;
    private BalanceCoordinator? _coordinator;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var store = new AppDataStore();
        var credentials = new WindowsCredentialStore();
        var client = new ProviderClient();
        _coordinator = new BalanceCoordinator(store, credentials, client);

        _mainWindow = new MainWindow(_coordinator);
        _island = new TaskbarIslandWindow(_coordinator);
        _mainWindow.IslandVisibilityRequested += (_, visible) => SetIslandVisible(visible);
        _mainWindow.Closing += (_, args) =>
        {
            if (_coordinator.IsExiting) return;
            args.Cancel = true;
            _mainWindow.Hide();
        };

        CreateTrayIcon();
        _coordinator.AlertRaised += (_, alert) => Dispatcher.Invoke(() =>
        {
            if (_trayIcon is null) return;
            _trayIcon.BalloonTipTitle = alert.Title;
            _trayIcon.BalloonTipText = alert.Message;
            _trayIcon.BalloonTipIcon = Forms.ToolTipIcon.Warning;
            _trayIcon.ShowBalloonTip(5000);
        });
        if (_coordinator.State.IslandEnabled) _island.Show();
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
            if (_island is not null) SetIslandVisible(!_island.IsVisible);
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
        if (_island is null || _coordinator is null) return;
        _coordinator.SetIslandEnabled(visible);
        if (visible) _island.Show(); else _island.Hide();
        _mainWindow?.UpdateIslandButton(visible);
    }

    private void ExitApplication()
    {
        if (_coordinator is not null) _coordinator.IsExiting = true;
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _island?.Close();
        _mainWindow?.Close();
        _coordinator?.Dispose();
        Shutdown();
    }
}
