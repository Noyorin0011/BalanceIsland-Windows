using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace BalanceIsland.Windows;

public partial class MainWindow : Window
{
    private readonly BalanceCoordinator _coordinator;
    private readonly bool _isWindows11;
    private bool _updatingIslandMode;
    private bool _loadingControls;
    private string? _preferredDisplayGroupId;
    private readonly App? _app;

    public event EventHandler<bool>? IslandVisibilityRequested;
    public event EventHandler<IslandDisplayMode>? IslandDisplayModeRequested;

    public MainWindow(BalanceCoordinator coordinator)
    {
        InitializeComponent();
        _coordinator = coordinator;
        _app = System.Windows.Application.Current as App;
        _isWindows11 = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

        SetProviderChoices(ProviderCatalog.All, ProviderCatalog.All[0].Provider);

        IslandModeBox.ItemsSource = _isWindows11
            ? new[] { new IslandModeChoice(IslandDisplayMode.Floating, "透明悬浮窗") }
            : new[]
            {
                new IslandModeChoice(IslandDisplayMode.Floating, "透明悬浮窗"),
                new IslandModeChoice(IslandDisplayMode.TaskbarEmbedded, "任务栏组件（兼容）")
            };
        IslandModeBox.SelectionChanged += IslandModeBox_SelectionChanged;

        PositionPresetBox.ItemsSource = new[]
        {
            new IslandPositionChoice(IslandPositionPreset.Left, "左侧（Widgets 后）"),
            new IslandPositionChoice(IslandPositionPreset.Center, "任务栏居中"),
            new IslandPositionChoice(IslandPositionPreset.Right, "右侧（托盘前）"),
            new IslandPositionChoice(IslandPositionPreset.Custom, "自定义")
        };

        AnomalyModeBox.ItemsSource = new[]
        {
            new AnomalyModeChoice(AnomalyMode.Absolute, "绝对值"),
            new AnomalyModeChoice(AnomalyMode.Percent, "百分比"),
            new AnomalyModeChoice(AnomalyMode.Both, "任一满足")
        };

        SizePresetBox.ItemsSource = new[]
        {
            new IslandSizeChoice(IslandSizePreset.Compact, "紧凑 · 190 × 32"),
            new IslandSizeChoice(IslandSizePreset.Standard, "标准 · 225 × 38"),
            new IslandSizeChoice(IslandSizePreset.Large, "大号 · 285 × 48"),
            new IslandSizeChoice(IslandSizePreset.Custom, "自定义")
        };

        ThemeModeBox.ItemsSource = new[]
        {
            new AppThemeChoice(AppThemeMode.System, "跟随系统"),
            new AppThemeChoice(AppThemeMode.Light, "浅色"),
            new AppThemeChoice(AppThemeMode.Dark, "深色")
        };

        IslandPaletteBox.ItemsSource = new[]
        {
            new IslandPaletteChoice(IslandColorTheme.Classic, "经典"),
            new IslandPaletteChoice(IslandColorTheme.Mint, "薄荷"),
            new IslandPaletteChoice(IslandColorTheme.Sky, "天空"),
            new IslandPaletteChoice(IslandColorTheme.Coral, "珊瑚"),
            new IslandPaletteChoice(IslandColorTheme.Lime, "青柠"),
            new IslandPaletteChoice(IslandColorTheme.Custom, "自定义")
        };

        DisplayGroupModeBox.ItemsSource = new[]
        {
            new IslandGroupModeChoice(IslandGroupMode.Rotation, "轮播"),
            new IslandGroupModeChoice(IslandGroupMode.Aggregate, "聚合")
        };

        EnvironmentProviderGrid.ItemsSource = ProviderCatalog.All;

        _coordinator.StateChanged += Coordinator_StateChanged;
        if (_app is not null) _app.NotificationStatusChanged += App_NotificationStatusChanged;
        Closed += MainWindow_Closed;
        Loaded += MainWindow_Loaded;
        RefreshRows();
        UpdateIslandControls(_coordinator.State.IslandEnabled, _coordinator.State.IslandDisplayMode);
    }

    private void Coordinator_StateChanged(object? sender, EventArgs e) => Dispatcher.Invoke(RefreshRows);

    private void App_NotificationStatusChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
            UpdateNotificationChannelStatus();
        else
            Dispatcher.BeginInvoke(UpdateNotificationChannelStatus);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (_app is not null) _app.NotificationStatusChanged -= App_NotificationStatusChanged;
        _coordinator.StateChanged -= Coordinator_StateChanged;
    }

    private void ProviderSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var selected = ProviderBox.SelectedValue is Provider provider ? provider : (Provider?)null;
        SetProviderChoices(ProviderCatalog.Search(ProviderSearchBox.Text), selected);
    }

    private void SetProviderChoices(IReadOnlyList<ProviderDefinition> providers, Provider? preferred)
    {
        ProviderBox.ItemsSource = providers;
        ProviderBox.SelectedValue = providers.Any(definition => definition.Provider == preferred)
            ? preferred
            : providers.FirstOrDefault()?.Provider;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_coordinator.State.EnvironmentAutoImportEnabled)
            Dispatcher.BeginInvoke(OpenEnvironmentImportDialog);
    }

    private async void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        if (ProviderBox.SelectedValue is not Provider provider)
        {
            MessageBox.Show(this, "请选择 Provider。", "Balance Island");
            return;
        }
        if (!TryOptionalDouble(ManualBalanceBox.Text, out var manualBalance))
        {
            MessageBox.Show(this, "手动余额格式不正确。", "Balance Island");
            return;
        }
        if (!int.TryParse(RefreshMinutesBox.Text, out var interval) || interval is < 0 or > 1440)
        {
            MessageBox.Show(this, "刷新间隔应为 0–1440 分钟，0 表示使用自动建议。", "Balance Island");
            return;
        }

        SetBusy("正在保存并测试账户……", true);
        try
        {
            await _coordinator.AddAccountAsync(provider, LabelBox.Text, ApiKeyBox.Password,
                manualBalance, interval);
            LabelBox.Clear();
            ApiKeyBox.Clear();
            ManualBalanceBox.Clear();
            StatusText.Text = "账户已保存；API Key 仅存放于 Windows 凭据管理器。";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "添加账户失败", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            StatusText.Text = "添加账户失败";
        }
        finally
        {
            SetBusy(StatusText.Text, false);
        }
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (AccountsGrid.SelectedItem is not AccountRow row) return;
        if (MessageBox.Show(this, $"删除 {row.Provider} / {row.Label}？\n手动保存的 API Key 也会从凭据管理器删除。",
            "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _coordinator.RemoveAccount(row.Id);
    }

    private void AccountsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateAccountActionButtons();

    private void UpdateAccountActionButtons()
    {
        var row = AccountsGrid.SelectedItem as AccountRow;
        EnableAccountButton.IsEnabled = row is not null;
        EditAccountButton.IsEnabled = row is not null;
        DeleteAccountButton.IsEnabled = row is not null;
        EnableAccountButton.Content = row is null
            ? "启用/停用"
            : row.IsEnabled ? "停用" : "启用";
    }

    private void ToggleSelectedAccount_Click(object sender, RoutedEventArgs e)
    {
        if (AccountsGrid.SelectedItem is not AccountRow row) return;
        _coordinator.SetAccountEnabled(row.Id, !row.IsEnabled);
        StatusText.Text = row.IsEnabled ? "账户已停用；后台刷新、告警和浮岛显示均已暂停。" : "账户已启用。";
        if (!row.IsEnabled) _ = _coordinator.RefreshDueAsync(force: true, targetCredentialId: row.Id);
    }

    private async void EditSelectedAccount_Click(object sender, RoutedEventArgs e)
    {
        if (AccountsGrid.SelectedItem is not AccountRow row) return;
        var account = _coordinator.State.Accounts.FirstOrDefault(item => item.Id == row.Id);
        if (account is null) return;

        var editor = new AccountEditWindow(
            account,
            hasActiveDisplayGroup: _coordinator.State.ActiveDisplayGroupId is not null) { Owner = this };
        (System.Windows.Application.Current as App)?.TrackWindow(editor);
        if (editor.ShowDialog() != true) return;

        SetBusy("正在保存账户……", true);
        try
        {
            await _coordinator.UpdateAccountAsync(account.Id, editor.AccountLabel, editor.ApiKey,
                editor.ManualBalance, editor.RefreshMinutes);
            _coordinator.SetAccountShowInIsland(account.Id, editor.ShowInIsland);
            StatusText.Text = string.IsNullOrWhiteSpace(editor.ApiKey)
                ? "账户设置已保存；继续使用原 API 凭据。"
                : "账户设置及新 API Key 已保存。";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "编辑账户失败", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            StatusText.Text = "编辑账户失败";
        }
        finally
        {
            SetBusy(StatusText.Text, false);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        SetBusy("正在刷新……", true);
        try
        {
            await _coordinator.RefreshDueAsync(force: true);
            StatusText.Text = "刷新完成；同一账户的手动刷新最短间隔为 30 秒。";
        }
        finally
        {
            SetBusy(StatusText.Text, false);
        }
    }

    private void ToggleIsland_Click(object sender, RoutedEventArgs e) =>
        IslandVisibilityRequested?.Invoke(this, !_coordinator.State.IslandEnabled);

    private void IslandModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingIslandMode || IslandModeBox.SelectedValue is not IslandDisplayMode mode) return;
        IslandDisplayModeRequested?.Invoke(this, mode);
    }

    private void EditIslandBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingControls) return;
        _coordinator.SetIslandEditMode(EditIslandBox.IsChecked == true);
        StatusText.Text = EditIslandBox.IsChecked == true
            ? "编辑模式已开启：浮岛会显示在任务栏上方，可拖动并缩放。"
            : "编辑模式已关闭：浮岛恢复固定并穿透鼠标点击。";
    }

    private void ThemeModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingControls || ThemeModeBox.SelectedValue is not AppThemeMode mode) return;
        _coordinator.SetThemeMode(mode);
        StatusText.Text = mode switch
        {
            AppThemeMode.System => "已切换为跟随 Windows 系统主题。",
            AppThemeMode.Light => "已切换为浅色主题。",
            _ => "已切换为深色主题。"
        };
    }

    private void IslandPaletteBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingControls || IslandPaletteBox.SelectedValue is not IslandColorTheme theme) return;
        _coordinator.SetIslandColorTheme(theme);
        UpdateCustomColorControls();
        StatusText.Text = theme == IslandColorTheme.Custom
            ? "已启用自定义浮岛配色；请填写四种状态颜色后保存。"
            : "浮岛调色板已应用。";
    }

    private void CustomColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingControls) return;
        UpdateCustomColorPreviews();
    }

    private void SaveCustomColors_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingControls || _coordinator.State.IslandColorTheme != IslandColorTheme.Custom) return;
        try
        {
            _coordinator.SetCustomIslandColors(
                CustomNormalColorBox.Text,
                CustomAnomalyColorBox.Text,
                CustomWarning15ColorBox.Text,
                CustomCriticalColorBox.Text);
            StatusText.Text = "自定义浮岛配色已保存。";
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(this, exception.Message, "颜色格式不正确", MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusText.Text = "自定义颜色未保存。";
        }
    }

    private void UpdateCustomColorControls()
    {
        var isCustom = _coordinator.State.IslandColorTheme == IslandColorTheme.Custom;
        CustomColorFieldsPanel.IsEnabled = isCustom;
        UpdateCustomColorPreviews();
    }

    private void UpdateCustomColorPreviews()
    {
        SetColorPreview(CustomNormalColorPreview, CustomNormalColorBox.Text);
        SetColorPreview(CustomAnomalyColorPreview, CustomAnomalyColorBox.Text);
        SetColorPreview(CustomWarning15ColorPreview, CustomWarning15ColorBox.Text);
        SetColorPreview(CustomCriticalColorPreview, CustomCriticalColorBox.Text);
    }

    private static void SetColorPreview(Border preview, string color)
    {
        if (IslandColorPalettes.TryNormalizeColor(color, out var normalized) &&
            System.Windows.Media.ColorConverter.ConvertFromString(normalized) is System.Windows.Media.Color parsed)
        {
            preview.Background = new SolidColorBrush(parsed);
            return;
        }

        preview.Background = System.Windows.Media.Brushes.Transparent;
    }

    private void DisplayGroupBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingControls) return;
        if (DisplayGroupBox.SelectedValue is string groupId)
            LoadDisplayGroup(groupId);
        else
            PrepareNewDisplayGroup();
    }

    private void DisplayGroupSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingControls) return;
        UpdateDisplayGroupValidation();
    }

    private void NewDisplayGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingControls) return;
        _loadingControls = true;
        try
        {
            DisplayGroupBox.SelectedItem = null;
            DisplayGroupNameBox.Text = "";
            DisplayGroupModeBox.SelectedValue = IslandGroupMode.Rotation;
            DisplayGroupIncludePlanBox.IsChecked = false;
            DisplayGroupAccountsList.UnselectAll();
        }
        finally
        {
            _loadingControls = false;
        }
        UpdateDisplayGroupValidation();
        StatusText.Text = "请填写分组名称并选择至少一个账户。";
    }

    private void SaveDisplayGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingControls || DisplayGroupModeBox.SelectedValue is not IslandGroupMode mode) return;
        try
        {
            var accountIds = SelectedDisplayGroupAccountIds();
            var includePlan = DisplayGroupIncludePlanBox.IsChecked == true;
            if (DisplayGroupBox.SelectedValue is string groupId)
            {
                _preferredDisplayGroupId = groupId;
                _coordinator.UpdateDisplayGroup(groupId, DisplayGroupNameBox.Text, mode, accountIds, includePlan);
                StatusText.Text = "浮岛显示分组已保存。";
            }
            else
            {
                var group = _coordinator.CreateDisplayGroup(DisplayGroupNameBox.Text, mode, accountIds, includePlan);
                _preferredDisplayGroupId = group.Id;
                RefreshRows();
                StatusText.Text = $"已创建分组“{group.Name}”。";
            }
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(this, exception.Message, "分组无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusText.Text = "分组未保存，请检查名称和成员。";
        }
    }

    private void DeleteDisplayGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingControls || DisplayGroupBox.SelectedValue is not string groupId) return;
        var name = _coordinator.State.DisplayGroups.FirstOrDefault(group => group.Id == groupId)?.Name ?? "该分组";
        if (MessageBox.Show(this, $"删除“{name}”？这不会删除任何账户。", "确认删除分组",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            _coordinator.DeleteDisplayGroup(groupId);
            StatusText.Text = "分组已删除。";
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(this, exception.Message, "删除分组失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetActiveDisplayGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingControls || DisplayGroupBox.SelectedValue is not string groupId) return;
        try
        {
            _coordinator.SetActiveDisplayGroup(groupId);
            StatusText.Text = "已设为浮岛活动分组。";
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(this, exception.Message, "无法设为活动分组", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearActiveDisplayGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingControls || _coordinator.State.ActiveDisplayGroupId is null) return;
        _coordinator.SetActiveDisplayGroup(null);
        StatusText.Text = "已停用分组，浮岛将使用默认账户显示设置。";
    }

    private void LoadDisplayGroup(string groupId)
    {
        var group = _coordinator.State.DisplayGroups.FirstOrDefault(item => item.Id == groupId);
        if (group is null) return;

        _loadingControls = true;
        try
        {
            DisplayGroupNameBox.Text = group.Name;
            DisplayGroupModeBox.SelectedValue = group.Mode;
            DisplayGroupIncludePlanBox.IsChecked = group.IncludeCodexPlanUsage;
            DisplayGroupAccountsList.UnselectAll();
            foreach (var choice in DisplayGroupAccountsList.Items.OfType<AccountChoice>()
                         .Where(choice => group.AccountIds.Contains(choice.Id, StringComparer.Ordinal)))
                DisplayGroupAccountsList.SelectedItems.Add(choice);
        }
        finally
        {
            _loadingControls = false;
        }
        UpdateDisplayGroupValidation();
    }

    private void PrepareNewDisplayGroup()
    {
        _loadingControls = true;
        try
        {
            DisplayGroupNameBox.Text = "";
            DisplayGroupModeBox.SelectedValue = IslandGroupMode.Rotation;
            DisplayGroupAccountsList.UnselectAll();
        }
        finally
        {
            _loadingControls = false;
        }
        UpdateDisplayGroupValidation();
    }

    private string[] SelectedDisplayGroupAccountIds() => DisplayGroupAccountsList.SelectedItems
        .OfType<AccountChoice>()
        .Select(choice => choice.Id)
        .ToArray();

    private void UpdateDisplayGroupValidation()
    {
        var providers = SelectedDisplayGroupAccountIds()
            .Select(id => _coordinator.State.Accounts.FirstOrDefault(account => account.Id == id)?.Provider)
            .Where(provider => provider is not null)
            .Distinct()
            .ToArray();
        var isAggregate = DisplayGroupModeBox.SelectedValue is IslandGroupMode.Aggregate;
        var invalidAggregate = DisplayGroupEditorValidation.HasMixedProviders(
            DisplayGroupModeBox.SelectedValue,
            providers);

        DisplayGroupIncludePlanBox.IsEnabled = !isAggregate;
        if (isAggregate && DisplayGroupIncludePlanBox.IsChecked == true)
        {
            _loadingControls = true;
            DisplayGroupIncludePlanBox.IsChecked = false;
            _loadingControls = false;
        }
        SaveDisplayGroupButton.IsEnabled = !invalidAggregate;
        DisplayGroupValidationText.Text = invalidAggregate
            ? "聚合分组只能包含同一 Provider 的账户。"
            : isAggregate
                ? "聚合会合计同一 Provider 账户的余额；币种不一致时浮岛会说明无法汇总。"
                : "轮播会按所选账户依次显示，可混合不同 Provider。";
        DeleteDisplayGroupButton.IsEnabled = DisplayGroupBox.SelectedValue is string;
        SetActiveDisplayGroupButton.IsEnabled = DisplayGroupBox.SelectedValue is string;
        ClearActiveDisplayGroupButton.IsEnabled = _coordinator.State.ActiveDisplayGroupId is not null;
        var activeGroup = _coordinator.State.ActiveDisplayGroupId is { } activeId
            ? _coordinator.State.DisplayGroups.FirstOrDefault(group => group.Id == activeId)
            : null;
        ActiveDisplayGroupStatusText.Text = activeGroup is null
            ? "当前显示：默认账户显示（按账户的“在浮岛显示”设置）。"
            : $"当前活动分组：{activeGroup.Name}。可停用分组并恢复默认账户显示。";
    }

    private void PositionPresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingControls || PositionPresetBox.SelectedValue is not IslandPositionPreset preset) return;
        _coordinator.SetIslandPositionPreset(preset);
        StatusText.Text = preset == IslandPositionPreset.Custom
            ? "已切换到保存的自定义位置。"
            : "浮岛位置预设已应用。";
    }

    private void SizePresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingControls || SizePresetBox.SelectedValue is not IslandSizePreset preset ||
            preset == IslandSizePreset.Custom) return;
        _coordinator.SetIslandSizePreset(preset);
        StatusText.Text = $"浮岛尺寸已设为 {_coordinator.State.IslandWidth:0} × {_coordinator.State.IslandHeight:0}。";
    }

    private void ApplyIslandSize_Click(object sender, RoutedEventArgs e)
    {
        if (!TryPositiveDouble(IslandWidthBox.Text, out var width) ||
            !TryPositiveDouble(IslandHeightBox.Text, out var height))
        {
            MessageBox.Show(this, "浮岛宽度和高度必须是正数。", "Balance Island");
            return;
        }

        _coordinator.SetIslandSize(width, height);
        StatusText.Text = $"浮岛尺寸已设为 {_coordinator.State.IslandWidth:0} × {_coordinator.State.IslandHeight:0}。";
    }

    private void AlertAccountBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingControls || AlertAccountBox.SelectedValue is not string id) return;
        LoadAlertAccount(id);
    }

    private void LoadAlertAccount(string id)
    {
        var account = _coordinator.State.Accounts.FirstOrDefault(item => item.Id == id);
        if (account is null) return;
        _loadingControls = true;
        try
        {
            AlertEnabledBox.IsChecked = account.AlertEnabled;
            WarningLineBox.Text = account.WarningLine.ToString("0.##", CultureInfo.InvariantCulture);
            AnomalyEnabledBox.IsChecked = account.AnomalyEnabled;
            AnomalyThresholdBox.Text = account.AnomalyThreshold.ToString("0.##", CultureInfo.InvariantCulture);
            AnomalyPercentBox.Text = account.AnomalyPercentThreshold.ToString("0.##", CultureInfo.InvariantCulture);
            AnomalyModeBox.SelectedValue = account.AnomalyMode;
            AnomalyCooldownBox.Text = account.AnomalyCooldownMinutes.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _loadingControls = false;
        }
    }

    private void SaveAlert_Click(object sender, RoutedEventArgs e)
    {
        if (AlertAccountBox.SelectedValue is not string id) return;
        if (!TryNonNegativeDouble(WarningLineBox.Text, out var warningLine) ||
            !TryPositiveDouble(AnomalyThresholdBox.Text, out var anomalyThreshold) ||
            !TryPositiveDouble(AnomalyPercentBox.Text, out var anomalyPercent) ||
            !int.TryParse(AnomalyCooldownBox.Text, out var cooldown) || cooldown is < 1 or > 10080 ||
            AnomalyModeBox.SelectedValue is not AnomalyMode mode)
        {
            MessageBox.Show(this,
                "请检查告警参数：警告线应 ≥ 0，其余阈值应 > 0，冷却时间为 1–10080 分钟。",
                "Balance Island", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _coordinator.UpdateAlertSettings(id, AlertEnabledBox.IsChecked == true, warningLine,
            AnomalyEnabledBox.IsChecked == true, anomalyThreshold, anomalyPercent, mode, cooldown);
        StatusText.Text = "余额告警设置已保存，并已重置该账户的告警参考状态。";
    }

    private void EnvironmentAutoImportBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingControls) return;
        _coordinator.SetEnvironmentAutoImport(EnvironmentAutoImportBox.IsChecked == true);
        StatusText.Text = EnvironmentAutoImportBox.IsChecked == true
            ? "启动后将提示扫描环境 API；仍需确认勾选后才会导入。"
            : "已停用启动扫描提示；已添加的环境账户仍保留。";
    }

    private void ScanEnvironment_Click(object sender, RoutedEventArgs e) => OpenEnvironmentImportDialog();

    private void NotificationSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingControls) return;
        _coordinator.SetNotificationSettings(
            NotifyWarning15Box.IsChecked == true,
            NotifyCriticalBox.IsChecked == true,
            NotifyAnomalyBox.IsChecked == true);
        StatusText.Text = "通知类型设置已保存。";
    }

    private void SendTestNotification_Click(object sender, RoutedEventArgs e)
    {
        var result = (System.Windows.Application.Current as App)?.SendTestNotification();
        UpdateNotificationChannelStatus();
        StatusText.Text = result == NotificationDeliveryResult.NativeToast
            ? "已请求发送 Windows 测试通知；是否显示仍由系统通知设置决定。"
            : "Windows 原生通知不可用，已尝试使用托盘通知。";
    }

    private void OpenNotificationSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:notifications",
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
            MessageBox.Show(this, "无法打开 Windows 通知设置。请在系统设置中搜索“通知”。",
                "Balance Island", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateNotificationChannelStatus()
    {
        var status = (System.Windows.Application.Current as App)?.NotificationStatus
            ?? NotificationChannelStatus.Unavailable;
        NotificationChannelStatusText.Text = status switch
        {
            NotificationChannelStatus.WindowsImportantNotification => "通知通道：Windows 重要通知",
            NotificationChannelStatus.WindowsNotification => "通知通道：普通 Windows 通知",
            NotificationChannelStatus.TrayFallback => "通知通道：托盘回退",
            _ => "通知通道：不可用"
        };
    }

    private void OpenEnvironmentImportDialog()
    {
        var candidates = EnvironmentCredentialDiscovery.Scan();
        var dialog = new EnvironmentImportWindow(candidates, _coordinator.State.Accounts) { Owner = this };
        (System.Windows.Application.Current as App)?.TrackWindow(dialog);
        if (dialog.ShowDialog() != true) return;

        var result = _coordinator.ImportEnvironmentAccounts(dialog.SelectedCandidates);
        var added = result.AddedProviders.Count == 0 ? "无新增" : string.Join("、", result.AddedProviders);
        StatusText.Text = $"扫描到 {candidates.Count} 个受支持环境凭据；新增：{added}。";
    }

    public void UpdateIslandButton(bool visible) =>
        IslandButton.Content = visible ? "隐藏任务栏浮岛" : "显示任务栏浮岛";

    public void UpdateIslandControls(bool visible, IslandDisplayMode mode, string? status = null)
    {
        if (_isWindows11) mode = IslandDisplayMode.Floating;
        UpdateIslandButton(visible);
        _updatingIslandMode = true;
        IslandModeBox.SelectedValue = mode;
        _updatingIslandMode = false;

        _loadingControls = true;
        try
        {
            EditIslandBox.IsChecked = _coordinator.State.IslandEditMode;
            PositionPresetBox.SelectedValue = _coordinator.State.IslandPositionPreset;
            SizePresetBox.SelectedValue = _coordinator.State.IslandSizePreset;
            IslandWidthBox.Text = _coordinator.State.IslandWidth.ToString("0", CultureInfo.InvariantCulture);
            IslandHeightBox.Text = _coordinator.State.IslandHeight.ToString("0", CultureInfo.InvariantCulture);
            EnvironmentAutoImportBox.IsChecked = _coordinator.State.EnvironmentAutoImportEnabled;
        }
        finally
        {
            _loadingControls = false;
        }

        IslandModeStatus.Text = status ?? mode switch
        {
            IslandDisplayMode.TaskbarEmbedded => "挂载到 Explorer 任务栏；不可用时自动回退悬浮",
            _ => "透明悬浮；全屏时自动隐藏，锁定后鼠标穿透"
        };
    }

    private void CodexPlanConsent_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingControls) return;
        var accepted = CodexPlanConsentBox.IsChecked == true;
        _coordinator.SetCodexPlanConsent(accepted);
        RefreshCodexPlanControls();
        StatusText.Text = accepted
            ? "已确认风险；可以打开登录窗口并读取套餐余量。"
            : "已撤销套餐读取授权，功能与自动刷新均已关闭。";
    }

    private void CodexPlanOpenLogin_Click(object sender, RoutedEventArgs e)
    {
        (_app as App)?.OpenCodexPlanLoginWindow(this);
    }

    private async void CodexPlanRead_Click(object sender, RoutedEventArgs e)
    {
        if (_coordinator.State.CodexPlanReadState.AutoRefreshPaused)
        {
            StatusText.Text = "套餐读取因 401/403/429 暂停；请点击“重试并恢复”。";
            return;
        }
        SetBusy("正在读取套餐余量……", true);
        try
        {
            var result = await (_app as App)?.RefreshCodexPlanAsync(true) ?? new CodexPlanRefreshResult(CodexPlanRefreshOutcome.NotReady, null);
            StatusText.Text = result.Outcome switch
            {
                CodexPlanRefreshOutcome.Success => "套餐余量已更新。",
                CodexPlanRefreshOutcome.TooSoon => "距上次读取不足 5 分钟，请稍后再试。",
                CodexPlanRefreshOutcome.Paused => "套餐读取已暂停（401/403/429）；请点击“重试并恢复”。",
                CodexPlanRefreshOutcome.NotReady => "尚未就绪：请确认风险并启用功能。",
                _ => "读取失败，请查看下方错误信息。"
            };
        }
        finally
        {
            SetBusy(StatusText.Text, false);
            RefreshCodexPlanControls();
        }
    }

    private async void CodexPlanResume_Click(object sender, RoutedEventArgs e)
    {
        SetBusy("正在重试并恢复套餐读取……", true);
        try
        {
            var result = await (_app as App)?.ResumeCodexPlanAsync() ?? new CodexPlanRefreshResult(CodexPlanRefreshOutcome.NotReady, null);
            StatusText.Text = result.Outcome switch
            {
                CodexPlanRefreshOutcome.Success => "已恢复并读取成功。",
                CodexPlanRefreshOutcome.TooSoon => "距上次网络尝试不足 5 分钟，请稍后再试。",
                CodexPlanRefreshOutcome.NotReady => "尚未就绪：请确认风险并启用功能。",
                _ => "重试失败，请查看下方错误信息。"
            };
        }
        finally
        {
            SetBusy(StatusText.Text, false);
            RefreshCodexPlanControls();
        }
    }

    private void CodexPlanAutoRefresh_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingControls) return;
        if (CodexPlanAutoRefreshBox.IsChecked == true && !ConfirmBackgroundAutoRefresh())
        {
            _loadingControls = true;
            CodexPlanAutoRefreshBox.IsChecked = false;
            _loadingControls = false;
            return;
        }
        _coordinator.SetCodexPlanSettings(
            _coordinator.State.CodexPlanEnabled,
            CodexPlanAutoRefreshBox.IsChecked == true,
            CodexPlanShowInIslandBox.IsChecked == true);
        if (CodexPlanAutoRefreshBox.IsChecked == true)
            (_app as App)?.StartCodexPlanService();
        RefreshCodexPlanControls();
    }

    private bool ConfirmBackgroundAutoRefresh()
    {
        var result = MessageBox.Show(this,
            "启用自动刷新后，即使登录窗口已隐藏，应用仍会在后台每 5 分钟联网读取一次套餐余量。\n\n是否继续？",
            "确认后台自动刷新", MessageBoxButton.YesNo, MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    private void CodexPlanShowInIsland_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingControls) return;
        _coordinator.SetCodexPlanSettings(
            _coordinator.State.CodexPlanEnabled,
            CodexPlanAutoRefreshBox.IsChecked == true,
            CodexPlanShowInIslandBox.IsChecked == true);
        RefreshCodexPlanControls();
    }

    private async void CodexPlanDisconnect_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "将清除专用登录 Profile、Cookie 与所有套餐数据，并停止自动刷新。是否继续？",
                "断开并清除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        SetBusy("正在断开并清除……", true);
        try
        {
            var app = _app as App;
            if (app is not null) await app.DisconnectCodexPlanAsync();
            StatusText.Text = "套餐读取已断开，登录 Profile 与数据已清除。";
        }
        finally
        {
            SetBusy(StatusText.Text, false);
            RefreshCodexPlanControls();
        }
    }

    private void DisplayGroupIncludePlan_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingControls) return;
        if (DisplayGroupModeBox.SelectedValue is IslandGroupMode.Aggregate &&
            DisplayGroupIncludePlanBox.IsChecked == true)
        {
            _loadingControls = true;
            DisplayGroupIncludePlanBox.IsChecked = false;
            _loadingControls = false;
            StatusText.Text = "聚合分组不能包含套餐余量。";
            return;
        }
        UpdateDisplayGroupValidation();
    }

    private void RefreshCodexPlanControls()
    {
        var state = _coordinator.State;
        _loadingControls = true;
        try
        {
            CodexPlanConsentBox.IsChecked = state.CodexPlanConsentVersion == 1;
            CodexPlanOpenLoginButton.IsEnabled = state.CodexPlanConsentVersion == 1;
            CodexPlanReadButton.IsEnabled = state.CodexPlanEnabled &&
                !state.CodexPlanReadState.AutoRefreshPaused;
            CodexPlanResumeButton.Visibility = state.CodexPlanEnabled &&
                state.CodexPlanReadState.AutoRefreshPaused
                ? Visibility.Visible : Visibility.Collapsed;
            CodexPlanAutoRefreshBox.IsChecked = state.CodexPlanAutoRefreshEnabled;
            CodexPlanShowInIslandBox.IsChecked = state.CodexPlanShowInIsland;
            CodexPlanDisconnectButton.IsEnabled = state.CodexPlanConsentVersion == 1 ||
                state.CodexPlanEnabled || state.CodexPlanUsage is not null;
            var lastSuccess = state.CodexPlanReadState.LastSuccessfulAt;
            CodexPlanStatusText.Text = state.CodexPlanUsage is null
                ? "尚未读取套餐余量。"
                : $"上次成功：{lastSuccess?.LocalDateTime.ToString("MM-dd HH:mm") ?? "—"} · 套餐 {state.CodexPlanUsage.PlanType}";
            CodexPlanErrorText.Text = state.CodexPlanReadState.LastError is { } error
                ? $"最近错误：{CodexPlanErrorDescription(error)}"
                : "";
        }
        finally
        {
            _loadingControls = false;
        }
    }

    private static string CodexPlanErrorDescription(CodexPlanReadError error) => error switch
    {
        CodexPlanReadError.Auth => "登录失效或权限不足（401/403）",
        CodexPlanReadError.RateLimit => "请求受限（429），已暂停自动刷新",
        CodexPlanReadError.Network => "网络连接失败",
        CodexPlanReadError.Http => "服务器返回异常状态",
        CodexPlanReadError.Parse => "响应解析失败",
        _ => "运行时错误"
    };

    public void UpdateIslandModeStatus(string status) => IslandModeStatus.Text = status;

    private void RefreshRows()
    {
        var previousSelected = (AccountsGrid.SelectedItem as AccountRow)?.Id;
        var previousAlert = AlertAccountBox.SelectedValue as string;
        var previousGroup = DisplayGroupBox.SelectedValue as string;
        var accounts = _coordinator.State.Accounts.ToDictionary(account => account.Id);

        var rows = _coordinator.CurrentSnapshots.Select(snapshot =>
        {
            var account = accounts[snapshot.CredentialId];
            return new AccountRow(
                account.Id,
                account.IsEnabled,
                account.Provider.DisplayName(),
                account.DisplayLabel,
                account.CredentialSourceLabel,
                account.IsEnabled ? snapshot.PrimaryText : "已停用",
                account.IsEnabled ? snapshot.SecondaryText : "后台刷新、告警和浮岛显示已暂停",
                snapshot.UpdatedAt == default ? "—" : snapshot.UpdatedAt.LocalDateTime.ToString("MM-dd HH:mm"));
        }).ToArray();
        var choices = _coordinator.State.Accounts
            .Select(account => new AccountChoice(account.Id,
                $"{account.Provider.DisplayName()} · {account.DisplayLabel}"))
            .ToArray();
        var groups = _coordinator.State.DisplayGroups
            .Select(group => new DisplayGroupChoice(group.Id,
                $"{group.Name} · {(group.Mode == IslandGroupMode.Aggregate ? "聚合" : "轮播")}"))
            .ToArray();
        var selectedGroup = DisplayGroupSelection.Resolve(
            _coordinator.State.DisplayGroups,
            previousGroup,
            _preferredDisplayGroupId,
            _coordinator.State.ActiveDisplayGroupId);
        _preferredDisplayGroupId = null;

        _loadingControls = true;
        try
        {
            AccountsGrid.ItemsSource = rows;
            AccountsGrid.SelectedItem = rows.FirstOrDefault(row => row.Id == previousSelected);
            UpdateAccountActionButtons();
            AlertAccountBox.ItemsSource = choices;
            DisplayGroupAccountsList.ItemsSource = choices;
            DisplayGroupBox.ItemsSource = groups;
            AlertAccountBox.SelectedValue = choices.Any(item => item.Id == previousAlert)
                ? previousAlert : choices.FirstOrDefault()?.Id;
            DisplayGroupBox.SelectedValue = selectedGroup;
            ThemeModeBox.SelectedValue = _coordinator.State.ThemeMode;
            IslandPaletteBox.SelectedValue = _coordinator.State.IslandColorTheme;
            CustomNormalColorBox.Text = _coordinator.State.CustomNormalColor;
            CustomAnomalyColorBox.Text = _coordinator.State.CustomAnomalyColor;
            CustomWarning15ColorBox.Text = _coordinator.State.CustomWarning15Color;
            CustomCriticalColorBox.Text = _coordinator.State.CustomCriticalColor;
            EditIslandBox.IsChecked = _coordinator.State.IslandEditMode;
            PositionPresetBox.SelectedValue = _coordinator.State.IslandPositionPreset;
            SizePresetBox.SelectedValue = _coordinator.State.IslandSizePreset;
            EnvironmentAutoImportBox.IsChecked = _coordinator.State.EnvironmentAutoImportEnabled;
            IslandWidthBox.Text = _coordinator.State.IslandWidth.ToString("0", CultureInfo.InvariantCulture);
            IslandHeightBox.Text = _coordinator.State.IslandHeight.ToString("0", CultureInfo.InvariantCulture);
            NotifyWarning15Box.IsChecked = _coordinator.State.NotifyWarning15;
            NotifyCriticalBox.IsChecked = _coordinator.State.NotifyCritical;
            NotifyAnomalyBox.IsChecked = _coordinator.State.NotifyAnomaly;
        }
        finally
        {
            _loadingControls = false;
        }

        if (AlertAccountBox.SelectedValue is string alertId) LoadAlertAccount(alertId);
        if (DisplayGroupBox.SelectedValue is string groupId) LoadDisplayGroup(groupId);
        else PrepareNewDisplayGroup();
        UpdateCustomColorControls();
        UpdateNotificationChannelStatus();
        RefreshCodexPlanControls();
    }

    private void SetBusy(string message, bool busy)
    {
        StatusText.Text = message;
        IsEnabled = !busy;
    }

    private static bool TryOptionalDouble(string text, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (TryDouble(text, out var parsed) && parsed >= 0)
        {
            value = parsed;
            return true;
        }
        return false;
    }

    private static bool TryPositiveDouble(string text, out double value) =>
        TryDouble(text, out value) && value > 0;

    private static bool TryNonNegativeDouble(string text, out double value) =>
        TryDouble(text, out value) && value >= 0;

    private static bool TryDouble(string text, out double value)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return double.IsFinite(value);
        return false;
    }
}

public sealed record ProviderChoice(Provider Value, string Display);
public sealed record IslandModeChoice(IslandDisplayMode Value, string Display);
public sealed record AnomalyModeChoice(AnomalyMode Value, string Display);
public sealed record IslandPositionChoice(IslandPositionPreset Value, string Display);
public sealed record IslandSizeChoice(IslandSizePreset Value, string Display);
public sealed record AccountChoice(string Id, string Display);
public sealed record AppThemeChoice(AppThemeMode Value, string Display);
public sealed record IslandPaletteChoice(IslandColorTheme Value, string Display);
public sealed record IslandGroupModeChoice(IslandGroupMode Value, string Display);
public sealed record DisplayGroupChoice(string Id, string Display);
public sealed record AccountRow(
    string Id, bool IsEnabled, string Provider, string Label, string Source, string Primary, string Secondary, string Updated);
