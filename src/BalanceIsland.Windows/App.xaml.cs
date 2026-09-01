using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace BalanceIsland.Windows;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _trayIcon;
    private Icon? _appIcon;
    private MainWindow? _mainWindow;
    private TaskbarIslandWindow? _island;
    private BalanceCoordinator? _coordinator;
    private INotificationService? _notificationService;
    private SystemThemeManager? _themeManager;
    private DispatcherTimer? _islandHealthTimer;
    private bool _resourcesDisposed;

    private CodexPlanUsageService? _codexPlanService;
    private CodexPlanWindow? _codexPlanWindow;
    private bool _codexPlanDisposed;

    private CodexPlanUsageService EnsureCodexPlanService()
    {
        if (_codexPlanService is null)
        {
            if (_codexPlanWindow is null)
            {
                _codexPlanWindow = new CodexPlanWindow();
                TrackWindow(_codexPlanWindow);
            }
            var browser = new WebView2CodexPlanBrowser(_codexPlanWindow.Browser);
            var timer = new DispatcherCodexPlanTimer(Dispatcher);
            _codexPlanService = new CodexPlanUsageService(_coordinator!, browser, TimeProvider.System, timer);
        }
        return _codexPlanService;
    }

    public void OpenCodexPlanLoginWindow(Window owner)
    {
        if (_coordinator is null || _coordinator.State.CodexPlanConsentVersion != 1) return;
        if (_codexPlanWindow is null)
        {
            _codexPlanWindow = new CodexPlanWindow();
            TrackWindow(_codexPlanWindow);
        }
        _codexPlanWindow.Owner = owner;
        _codexPlanWindow.Show();
        _codexPlanWindow.Activate();
        _codexPlanWindow.NavigateToLogin();
    }

    public void StartCodexPlanService()
    {
        if (_coordinator is null || _coordinator.State.CodexPlanConsentVersion != 1 ||
            !_coordinator.State.CodexPlanEnabled || !_coordinator.State.CodexPlanAutoRefreshEnabled)
            return;
        var service = EnsureCodexPlanService();
        service.Start();
    }

    public async Task<CodexPlanRefreshResult> RefreshCodexPlanAsync(bool manual)
    {
        if (_coordinator is null || _coordinator.State.CodexPlanConsentVersion != 1)
            return new CodexPlanRefreshResult(CodexPlanRefreshOutcome.NotReady, null);
        var service = EnsureCodexPlanService();
        await EnsureCodexPlanBrowserReadyAsync();
        return await service.RefreshAsync(manual, CancellationToken.None);
    }

    public async Task<CodexPlanRefreshResult> ResumeCodexPlanAsync()
    {
        if (_coordinator is null || _coordinator.State.CodexPlanConsentVersion != 1)
            return new CodexPlanRefreshResult(CodexPlanRefreshOutcome.NotReady, null);
        var service = EnsureCodexPlanService();
        await EnsureCodexPlanBrowserReadyAsync();
        return await service.ResumeAndRefreshAsync(CancellationToken.None);
    }

    public async Task DisconnectCodexPlanAsync()
    {
        if (_codexPlanService is null) return;
        await _codexPlanService.DisconnectAsync(CancellationToken.None);
        _codexPlanWindow?.Close();
        _codexPlanWindow = null;
        _codexPlanService = null;
    }

    private async Task EnsureCodexPlanBrowserReadyAsync()
    {
        if (_codexPlanWindow is null)
        {
            _codexPlanWindow = new CodexPlanWindow();
            TrackWindow(_codexPlanWindow);
        }
        // The window must be realized so the WebView2 core is created before a read.
        if (_codexPlanWindow.Browser.CoreWebView2 is null)
            await _codexPlanWindow.Browser.EnsureCoreWebView2Async();
    }

    private void DisposeCodexPlanResources()
    {
        if (_codexPlanDisposed) return;
        _codexPlanDisposed = true;
        _codexPlanWindow?.Close();
        _codexPlanWindow = null;
        _codexPlanService?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _codexPlanService = null;
    }

    private void DisposeCodexPlanSynchronously()
    {
        _codexPlanWindow?.Close();
        _codexPlanWindow = null;
        _codexPlanService?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _codexPlanService = null;
    }

    public NotificationChannelStatus NotificationStatus { get; private set; } = NotificationChannelStatus.Unavailable;
    public event EventHandler? NotificationStatusChanged;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var store = new AppDataStore();
        var credentials = new WindowsCredentialStore();
        var client = new ProviderClient();
        _coordinator = new BalanceCoordinator(store, credentials, client);
        _notificationService = new WindowsNotificationService();
        NotificationStatus = _notificationService.ChannelStatus;
        _themeManager = new SystemThemeManager(Resources, Dispatcher, _coordinator.State.ThemeMode);
        _coordinator.StateChanged += Coordinator_StateChanged;
        _appIcon = AppIconFactory.CreateIcon(64);
        var supportedMode = ResolveDisplayMode(_coordinator.State.IslandDisplayMode);
        if (supportedMode != _coordinator.State.IslandDisplayMode)
            _coordinator.SetIslandDisplayMode(supportedMode);

        _mainWindow = new MainWindow(_coordinator)
        {
            Icon = AppIconFactory.CreateImageSource(64)
        };
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
        _islandHealthTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _islandHealthTimer.Tick += (_, _) => EnsureIslandHealthy();
        _islandHealthTimer.Start();
        _coordinator.AlertRaised += Coordinator_AlertRaised;
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
        menu.Items.Add("Codex 套餐", null, (_, _) =>
        {
            if (_mainWindow is not null) OpenCodexPlanLoginWindow(_mainWindow);
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _appIcon ?? SystemIcons.Information,
            Text = "Balance Island",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    public NotificationDeliveryResult SendTestNotification()
    {
        if (_notificationService is null) return NotificationDeliveryResult.Failed;
        if (Dispatcher.CheckAccess()) return DeliverTestNotification();
        return Dispatcher.Invoke(DeliverTestNotification);
    }

    private NotificationDeliveryResult DeliverTestNotification()
    {
        var result = _notificationService?.SendTest() ?? NotificationDeliveryResult.Failed;
        UpdateNotificationStatus(result);
        if (result == NotificationDeliveryResult.Failed)
            ShowTrayBalloon("Balance Island · 测试", "Windows 原生通知不可用，已使用托盘通知。\n系统通知设置仍然优先。");
        return result;
    }

    private void Coordinator_AlertRaised(object? sender, BalanceAlertEventArgs alert)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Coordinator_AlertRaised(sender, alert));
            return;
        }

        var notification = new BalanceNotification(
            alert.Kind, alert.Title, alert.Message, alert.AccountNote, alert.MaskedKeySuffix);
        var result = _notificationService?.Send(notification) ?? NotificationDeliveryResult.Failed;
        UpdateNotificationStatus(result);
        if (result == NotificationDeliveryResult.Failed)
            ShowTrayBalloon(
                alert.Title,
                $"{AccountContextFormatter.Format(notification.AccountNote, notification.MaskedKeySuffix)}\n{alert.Message}");
    }

    private void UpdateNotificationStatus(NotificationDeliveryResult result)
    {
        var next = result == NotificationDeliveryResult.Failed
            ? NotificationChannelStatus.TrayFallback
            : _notificationService?.ChannelStatus ?? NotificationChannelStatus.Unavailable;
        if (NotificationStatus == next) return;
        NotificationStatus = next;
        NotificationStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowTrayBalloon(string title, string message)
    {
        if (_trayIcon is null) return;
        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = message;
        _trayIcon.BalloonTipIcon = Forms.ToolTipIcon.Warning;
        _trayIcon.ShowBalloonTip(5000);
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void TrackWindow(Window window) => _themeManager?.Track(window);

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
        mode = ResolveDisplayMode(mode);
        _coordinator.SetIslandDisplayMode(mode);
        _mainWindow?.UpdateIslandControls(_coordinator.State.IslandEnabled, mode);
        CreateIslandWindow();
        _island?.SetDisplayMode(mode);
    }

    private static IslandDisplayMode ResolveDisplayMode(IslandDisplayMode mode) =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            ? IslandDisplayMode.Floating
            : mode;

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

    private void Coordinator_StateChanged(object? sender, EventArgs e)
    {
        if (_coordinator is null || _themeManager is null ||
            _themeManager.Mode == _coordinator.State.ThemeMode) return;

        if (Dispatcher.CheckAccess())
            _themeManager.SetMode(_coordinator.State.ThemeMode);
        else
            Dispatcher.BeginInvoke(() =>
            {
                if (_coordinator is not null && _themeManager is not null)
                    _themeManager.SetMode(_coordinator.State.ThemeMode);
            });
    }

    private void ExitApplication()
    {
        DisposeApplicationResources();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DisposeApplicationResources();
        base.OnExit(e);
    }

    private void DisposeApplicationResources()
    {
        if (_resourcesDisposed) return;
        _resourcesDisposed = true;

        if (_coordinator is not null) _coordinator.IsExiting = true;
        _islandHealthTimer?.Stop();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _appIcon?.Dispose();
        _island?.Close();
        _mainWindow?.Close();
        DisposeCodexPlanSynchronously();
        if (_coordinator is not null)
        {
            _coordinator.StateChanged -= Coordinator_StateChanged;
            _coordinator.AlertRaised -= Coordinator_AlertRaised;
        }
        _coordinator?.Dispose();
        _notificationService?.Dispose();
        _themeManager?.Dispose();
    }
}
