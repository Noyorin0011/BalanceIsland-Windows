using Xunit;
using System.Text.Json;
using System.Text.Json.Serialization;
using BalanceIsland.Windows;

namespace BalanceIsland.Windows.Tests;

public sealed class CodexPlanUsageLogicTests
{
    [Fact]
    public void Parse_filters_known_fields_and_converts_used_to_remaining()
    {
        const string json = "{\"plan_type\":\"plus\",\"rate_limit\":{\"primary_window\":{\"used_percent\":18.4,\"reset_at\":1788242400,\"limit_window_seconds\":18000},\"secondary_window\":{\"used_percent\":6,\"reset_after_seconds\":600,\"limit_window_seconds\":604800}},\"access_token\":\"must-not-survive\"}";
        var now = DateTimeOffset.FromUnixTimeSeconds(1788240000);
        var usage = CodexPlanUsageParser.Parse(json, now);
        Assert.Equal("plus", usage.PlanType);
        Assert.Equal(82, usage.Primary!.RemainingPercent);
        Assert.Equal(94, usage.Secondary!.RemainingPercent);
        Assert.Equal(1788240600, usage.Secondary.ResetAtUnixSeconds);
    }

    [Theory]
    [InlineData(14_400, CodexPlanWindowKind.FiveHour)]
    [InlineData(21_600, CodexPlanWindowKind.FiveHour)]
    [InlineData(518_400, CodexPlanWindowKind.Weekly)]
    [InlineData(691_200, CodexPlanWindowKind.Weekly)]
    [InlineData(3_600, CodexPlanWindowKind.Unknown)]
    public void Classify_uses_android_compatible_ranges(long seconds, CodexPlanWindowKind expected) =>
        Assert.Equal(expected, CodexPlanWindowClassifier.Classify(seconds));

    [Fact]
    public void Format_marks_snapshot_stale_after_fifteen_minutes()
    {
        var usage = Usage(updatedAt: DateTimeOffset.Parse("2026-09-01T12:00:00Z"));
        var text = CodexPlanOverlayFormatter.Format(usage, DateTimeOffset.Parse("2026-09-01T12:16:00Z"));
        Assert.Contains("数据较旧", text);
    }

    [Theory]
    [InlineData("https://chatgpt.com/", true)]
    [InlineData("http://chatgpt.com/", false)]
    [InlineData("https://chatgpt.com.evil.test/", false)]
    [InlineData("https://sub.chatgpt.com/", false)]
    [InlineData("https://chatgpt.com:8443/", false)]
    public void Read_origin_is_exact(string value, bool expected) =>
        Assert.Equal(expected, CodexPlanOriginPolicy.CanRead(new Uri(value)));

    [Fact]
    public void Script_envelope_never_projects_sensitive_extra_fields()
    {
        var result = JsonSerializer.Serialize(new { status = 200, body = new { plan_type = "plus", rate_limit = new { } }, accessToken = "secret", email = "user@example.test" });
        var envelope = CodexPlanScriptEnvelopeParser.Parse(result);
        Assert.Equal(200, envelope.StatusCode);
        Assert.DoesNotContain("secret", envelope.BodyJson);
        Assert.DoesNotContain("example.test", envelope.BodyJson);
    }

    private static CodexPlanUsage Usage(DateTimeOffset updatedAt) => new()
    {
        PlanType = "plus",
        Primary = new CodexPlanQuotaWindow { RemainingPercent = 82, WindowSeconds = 18_000 },
        UpdatedAt = updatedAt
    };
}
