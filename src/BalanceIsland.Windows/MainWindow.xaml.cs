using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace BalanceIsland.Windows;

public partial class MainWindow : Window
{
    private readonly BalanceCoordinator _coordinator;
    private bool _updatingIslandMode;
    private bool _loadingControls;

    public event EventHandler<bool>? IslandVisibilityRequested;
    public event EventHandler<IslandDisplayMode>? IslandDisplayModeRequested;

    public MainWindow(BalanceCoordinator coordinator)
    {
        InitializeComponent();
        _coordinator = coordinator;

        ProviderBox.ItemsSource = Enum.GetValues<Provider>()
            .Select(value => new ProviderChoice(value, value.DisplayName()))
            .ToArray();
        ProviderBox.SelectedIndex = 0;

        IslandModeBox.ItemsSource = new[]
        {
            new IslandModeChoice(IslandDisplayMode.Floating, "悬浮窗"),
            new IslandModeChoice(IslandDisplayMode.TaskbarEmbedded, "任务栏嵌入（实验）")
        };
        IslandModeBox.SelectionChanged += IslandModeBox_SelectionChanged;

        AnomalyModeBox.ItemsSource = new[]
        {
            new AnomalyModeChoice(AnomalyMode.Absolute, "绝对值"),
            new AnomalyModeChoice(AnomalyMode.Percent, "百分比"),
            new AnomalyModeChoice(AnomalyMode.Both, "任一满足")
        };

        SizePresetBox.ItemsSource = new[]
        {
            new IslandSizePreset("紧凑 · 260 × 26", 260, 26),
            new IslandSizePreset("默认 · 310 × 28", 310, 28),
            new IslandSizePreset("宽 · 380 × 30", 380, 30),
            new IslandSizePreset("大 · 460 × 34", 460, 34)
        };
        SizePresetBox.SelectedIndex = 1;

        EnvironmentVariablesText.Text = EnvironmentCredentialDiscovery.SupportedVariablesText;
        EnvironmentLimitationsText.Text = EnvironmentCredentialDiscovery.LimitationsText;

        _coordinator.StateChanged += Coordinator_StateChanged;
        RefreshRows();
        UpdateIslandControls(_coordinator.State.IslandEnabled, _coordinator.State.IslandDisplayMode);
    }

    private void Coordinator_StateChanged(object? sender, EventArgs e) => Dispatcher.Invoke(RefreshRows);

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

    private void ApplyIslandSize_Click(object sender, RoutedEventArgs e)
    {
        double width;
        double height;
        if (SizePresetBox.SelectedItem is IslandSizePreset preset)
        {
            width = preset.Width;
            height = preset.Height;
            IslandWidthBox.Text = width.ToString("0", CultureInfo.InvariantCulture);
            IslandHeightBox.Text = height.ToString("0", CultureInfo.InvariantCulture);
        }
        else if (!TryPositiveDouble(IslandWidthBox.Text, out width) ||
                 !TryPositiveDouble(IslandHeightBox.Text, out height))
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
            DropStepBox.Text = account.DropStep.ToString("0.##", CultureInfo.InvariantCulture);
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
            !TryPositiveDouble(DropStepBox.Text, out var dropStep) ||
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

        _coordinator.UpdateAlertSettings(id, AlertEnabledBox.IsChecked == true, warningLine, dropStep,
            AnomalyEnabledBox.IsChecked == true, anomalyThreshold, anomalyPercent, mode, cooldown);
        StatusText.Text = "余额告警设置已保存，并已重置该账户的告警参考状态。";
    }

    private void DisplayAccountBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingControls || DisplayAccountBox.SelectedValue is not string id) return;
        var account = _coordinator.State.Accounts.FirstOrDefault(item => item.Id == id);
        if (account is null) return;
        _loadingControls = true;
        DisplayAccountVisibleBox.IsChecked = account.ShowInIsland;
        _loadingControls = false;
    }

    private void SaveDisplayAccount_Click(object sender, RoutedEventArgs e)
    {
        if (DisplayAccountBox.SelectedValue is not string id) return;
        IslandAccountSelection.SetVisible(_coordinator, id, DisplayAccountVisibleBox.IsChecked == true);
        StatusText.Text = "浮岛显示账户设置已保存。";
    }

    private void EnvironmentAutoImportBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingControls) return;
        _coordinator.SetEnvironmentAutoImport(EnvironmentAutoImportBox.IsChecked == true);
        StatusText.Text = EnvironmentAutoImportBox.IsChecked == true
            ? "已启用环境 API 自动发现。" : "已停用环境 API 自动发现；已添加的环境账户仍保留。";
    }

    private async void ScanEnvironment_Click(object sender, RoutedEventArgs e)
    {
        var result = _coordinator.ImportEnvironmentAccounts();
        var added = result.AddedProviders.Count == 0 ? "无新增" : string.Join("、", result.AddedProviders);
        StatusText.Text = $"扫描到 {result.FoundCount} 个受支持环境凭据；新增：{added}。";
        if (result.AddedProviders.Count > 0)
            await _coordinator.RefreshDueAsync(force: true);
    }

    public void UpdateIslandButton(bool visible) =>
        IslandButton.Content = visible ? "隐藏任务栏浮岛" : "显示任务栏浮岛";

    public void UpdateIslandControls(bool visible, IslandDisplayMode mode, string? status = null)
    {
        UpdateIslandButton(visible);
        _updatingIslandMode = true;
        IslandModeBox.SelectedValue = mode;
        _updatingIslandMode = false;

        _loadingControls = true;
        try
        {
            EditIslandBox.IsChecked = _coordinator.State.IslandEditMode;
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
            _ => "悬浮在通知区域左侧"
        };
    }

    public void UpdateIslandModeStatus(string status) => IslandModeStatus.Text = status;

    private void RefreshRows()
    {
        var previousAlert = AlertAccountBox.SelectedValue as string;
        var previousDisplay = DisplayAccountBox.SelectedValue as string;
        var accounts = _coordinator.State.Accounts.ToDictionary(account => account.Id);

        AccountsGrid.ItemsSource = _coordinator.CurrentSnapshots.Select(snapshot =>
        {
            var account = accounts[snapshot.CredentialId];
            return new AccountRow(
                account.Id,
                account.Provider.DisplayName(),
                account.DisplayLabel,
                account.CredentialSourceLabel,
                snapshot.PrimaryText,
                snapshot.SecondaryText,
                snapshot.UpdatedAt == default ? "—" : snapshot.UpdatedAt.LocalDateTime.ToString("MM-dd HH:mm"));
        }).ToArray();

        var choices = _coordinator.State.Accounts
            .Select(account => new AccountChoice(account.Id,
                $"{account.Provider.DisplayName()} · {account.DisplayLabel}"))
            .ToArray();

        _loadingControls = true;
        try
        {
            AlertAccountBox.ItemsSource = choices;
            DisplayAccountBox.ItemsSource = choices;
            AlertAccountBox.SelectedValue = choices.Any(item => item.Id == previousAlert)
                ? previousAlert : choices.FirstOrDefault()?.Id;
            DisplayAccountBox.SelectedValue = choices.Any(item => item.Id == previousDisplay)
                ? previousDisplay : choices.FirstOrDefault()?.Id;
            EditIslandBox.IsChecked = _coordinator.State.IslandEditMode;
            EnvironmentAutoImportBox.IsChecked = _coordinator.State.EnvironmentAutoImportEnabled;
            IslandWidthBox.Text = _coordinator.State.IslandWidth.ToString("0", CultureInfo.InvariantCulture);
            IslandHeightBox.Text = _coordinator.State.IslandHeight.ToString("0", CultureInfo.InvariantCulture);
        }
        finally
        {
            _loadingControls = false;
        }

        if (AlertAccountBox.SelectedValue is string alertId) LoadAlertAccount(alertId);
        if (DisplayAccountBox.SelectedValue is string displayId)
        {
            var account = _coordinator.State.Accounts.FirstOrDefault(item => item.Id == displayId);
            if (account is not null)
            {
                _loadingControls = true;
                DisplayAccountVisibleBox.IsChecked = account.ShowInIsland;
                _loadingControls = false;
            }
        }
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
public sealed record IslandSizePreset(string Display, double Width, double Height);
public sealed record AccountChoice(string Id, string Display);
public sealed record AccountRow(
    string Id, string Provider, string Label, string Source, string Primary, string Secondary, string Updated);
