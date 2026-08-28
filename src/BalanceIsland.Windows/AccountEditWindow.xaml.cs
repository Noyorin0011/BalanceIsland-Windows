using System.Globalization;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace BalanceIsland.Windows;

public partial class AccountEditWindow : Window
{
    public string AccountLabel => LabelTextBox.Text;
    public string ApiKey => ApiKeyBox.Password;
    public double? ManualBalance { get; private set; }
    public int RefreshMinutes { get; private set; }

    public AccountEditWindow(Account account)
    {
        InitializeComponent();
        ProviderTextBox.Text = account.Provider.DisplayName();
        LabelTextBox.Text = account.Label;
        ManualBalanceTextBox.Text = account.ManualBalance?.ToString("0.##", CultureInfo.CurrentCulture) ?? "";
        RefreshMinutesTextBox.Text = account.RefreshIntervalMinutes == 0
            ? "0"
            : account.RefreshIntervalMinutes.ToString(CultureInfo.CurrentCulture);
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e) =>
        ApiKeyPlaceholder.Visibility = string.IsNullOrEmpty(ApiKeyBox.Password)
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryOptionalDouble(ManualBalanceTextBox.Text, out var balance))
        {
            MessageBox.Show(this, "手动余额必须留空或填写不小于 0 的数字。", "Balance Island",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var refreshText = RefreshMinutesTextBox.Text.Trim();
        if (refreshText.Length == 0) refreshText = "0";
        if (!int.TryParse(refreshText, out var refresh) || refresh is < 0 or > 1440)
        {
            MessageBox.Show(this, "刷新分钟必须留空，或填写 0–1440。", "Balance Island",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ManualBalance = balance;
        RefreshMinutes = refresh;
        DialogResult = true;
    }

    private static bool TryOptionalDouble(string text, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if ((double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) ||
             double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) &&
            double.IsFinite(parsed) && parsed >= 0)
        {
            value = parsed;
            return true;
        }
        return false;
    }
}
