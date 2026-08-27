using System.Globalization;
using System.Windows;

namespace BalanceIsland.Windows;

public partial class MainWindow : Window
{
    private readonly BalanceCoordinator _coordinator;
    public event EventHandler<bool>? IslandVisibilityRequested;

    public MainWindow(BalanceCoordinator coordinator)
    {
        InitializeComponent();
        _coordinator = coordinator;
        ProviderBox.ItemsSource = Enum.GetValues<Provider>()
            .Select(value => new ProviderChoice(value, value.DisplayName()))
            .ToArray();
        ProviderBox.SelectedIndex = 0;
        _coordinator.StateChanged += (_, _) => Dispatcher.Invoke(RefreshRows);
        RefreshRows();
        UpdateIslandButton(_coordinator.State.IslandEnabled);
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
        if (MessageBox.Show(this, $"删除 {row.Provider} / {row.Label}？\n对应 API Key 也会从凭据管理器删除。",
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

    private void ToggleIsland_Click(object sender, RoutedEventArgs e)
    {
        IslandVisibilityRequested?.Invoke(this, !_coordinator.State.IslandEnabled);
    }

    public void UpdateIslandButton(bool visible)
    {
        IslandButton.Content = visible ? "隐藏任务栏浮岛" : "显示任务栏浮岛";
    }

    private void RefreshRows()
    {
        var accounts = _coordinator.State.Accounts.ToDictionary(account => account.Id);
        AccountsGrid.ItemsSource = _coordinator.CurrentSnapshots.Select(snapshot =>
        {
            var account = accounts[snapshot.CredentialId];
            return new AccountRow(
                account.Id,
                account.Provider.DisplayName(),
                account.DisplayLabel,
                snapshot.PrimaryText,
                snapshot.SecondaryText,
                snapshot.UpdatedAt == default ? "—" : snapshot.UpdatedAt.LocalDateTime.ToString("MM-dd HH:mm"));
        }).ToArray();
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
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var local) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out local))
        {
            value = local;
            return double.IsFinite(local) && local >= 0;
        }
        return false;
    }
}

public sealed record ProviderChoice(Provider Value, string Display);
public sealed record AccountRow(
    string Id, string Provider, string Label, string Primary, string Secondary, string Updated);
