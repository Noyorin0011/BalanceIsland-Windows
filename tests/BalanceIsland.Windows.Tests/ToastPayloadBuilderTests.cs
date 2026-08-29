using Xunit;
namespace BalanceIsland.Windows.Tests;

public sealed class ToastPayloadBuilderTests
{
    [Fact]
    public void Windows11_payload_is_urgent_and_contains_no_full_key()
    {
        var xml = ToastPayloadBuilder.Build(
            new BalanceNotification(BalanceAlertKind.Critical, "DeepSeek · 余额临界", "¥19.00，已到达警戒线", "Prod", "••••1234"),
            urgentSupported: true);

        Assert.Contains("scenario=\"urgent\"", xml);
        Assert.Contains("Prod · ••••1234", xml);
        Assert.Contains("••••1234", xml);
        Assert.DoesNotContain("sk-secret", xml);
    }

    [Fact]
    public void Windows10_payload_omits_unsupported_urgent_scenario()
    {
        var xml = ToastPayloadBuilder.Build(
            new BalanceNotification(BalanceAlertKind.Warning15, "OpenAI · 余额预警", "$22.00，接近警戒线", "Dev", "••••5678"),
            urgentSupported: false);

        Assert.DoesNotContain("scenario=", xml);
    }

    [Fact]
    public void Payload_escapes_provider_and_balance_text()
    {
        var xml = ToastPayloadBuilder.Build(
            new BalanceNotification(BalanceAlertKind.Anomaly, "OpenAI · 异常变动", "$10.00 & changed", "<Prod>", "••••9876"),
            urgentSupported: true);

        Assert.Contains("&lt;Prod&gt; · ••••9876", xml);
        Assert.Contains("$10.00 &amp; changed", xml);
    }

    [Theory]
    [InlineData("Prod", "••••1234", "Prod · ••••1234")]
    [InlineData("", "••••1234", "••••1234")]
    [InlineData("Prod", "abcd", "Prod · ••••")]
    [InlineData("", "abcd", "••••")]
    public void Account_context_uses_a_note_or_safe_irreversible_suffix(
        string note,
        string suffix,
        string expected)
    {
        var context = AccountContextFormatter.Format(note, suffix);

        Assert.Equal(expected, context);
        if (suffix == "abcd") Assert.DoesNotContain(suffix, context, StringComparison.Ordinal);
    }
}
