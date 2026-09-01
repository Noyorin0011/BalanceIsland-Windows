using Xunit;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BalanceIsland.Windows;

namespace BalanceIsland.Windows.Tests;

public sealed class CodexPlanStateTests
{
    [Fact]
    public void Normalize_old_state_adds_safe_codex_defaults()
    {
        var state = AppStateNormalizer.Normalize(new AppState());
        Assert.Equal(0, state.CodexPlanConsentVersion);
        Assert.False(state.CodexPlanEnabled);
        Assert.False(state.CodexPlanAutoRefreshEnabled);
        Assert.True(state.CodexPlanShowInIsland);
        Assert.Null(state.CodexPlanUsage);
        Assert.NotNull(state.CodexPlanReadState);
    }

    [Fact]
    public void Store_round_trips_only_filtered_plan_fields()
    {
        var state = new AppState
        {
            CodexPlanConsentVersion = 1,
            CodexPlanEnabled = true,
            CodexPlanUsage = new CodexPlanUsage
            {
                PlanType = "plus",
                Primary = new CodexPlanQuotaWindow { RemainingPercent = 82, WindowSeconds = 18_000 },
                UpdatedAt = DateTimeOffset.Parse("2026-09-01T12:00:00+08:00")
            }
        };
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new JsonStringEnumConverter() }
        };
        var json = JsonSerializer.Serialize(state, options);
        Assert.Contains("plus", json);
        Assert.DoesNotContain("accessToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Store_rejects_invalid_remaining_percent(int remaining)
    {
        var directory = CreateStateDirectory(PlanUsageJson(remainingPercent: remaining));
        var result = new AppDataStore(directory).LoadResult();
        Assert.False(result.LoadedFromDisk);
        Assert.Contains("RemainingPercent", result.Error);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Store_rejects_invalid_consent_version(int version)
    {
        var root = new JsonObject { ["codexPlanConsentVersion"] = version };
        var directory = CreateStateDirectory(root.ToJsonString());
        var result = new AppDataStore(directory).LoadResult();
        Assert.True(result.LoadedFromDisk);
        Assert.Equal(0, result.State.CodexPlanConsentVersion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Store_rejects_non_positive_window_seconds(long windowSeconds)
    {
        var directory = CreateStateDirectory(PlanUsageJson(windowSeconds: windowSeconds));
        var result = new AppDataStore(directory).LoadResult();
        Assert.False(result.LoadedFromDisk);
        Assert.Contains("WindowSeconds", result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Store_rejects_non_positive_reset_time(long resetAtUnixSeconds)
    {
        var directory = CreateStateDirectory(PlanUsageJson(resetAtUnixSeconds: resetAtUnixSeconds));
        var result = new AppDataStore(directory).LoadResult();
        Assert.False(result.LoadedFromDisk);
        Assert.Contains("ResetAtUnixSeconds", result.Error);
    }

    [Fact]
    public void Store_rejects_usage_without_updated_at()
    {
        var root = new JsonObject
        {
            ["codexPlanUsage"] = new JsonObject
            {
                ["primary"] = new JsonObject { ["remainingPercent"] = 50 }
            }
        };
        var directory = CreateStateDirectory(root.ToJsonString());
        var result = new AppDataStore(directory).LoadResult();
        Assert.False(result.LoadedFromDisk);
        Assert.Contains("UpdatedAt", result.Error);
    }

    private static string PlanUsageJson(
        int? remainingPercent = null,
        long? windowSeconds = null,
        long? resetAtUnixSeconds = null,
        bool includeUpdatedAt = true)
    {
        var primary = new JsonObject();
        if (remainingPercent is { } remaining) primary["remainingPercent"] = remaining;
        if (windowSeconds is { } window) primary["windowSeconds"] = window;
        if (resetAtUnixSeconds is { } reset) primary["resetAtUnixSeconds"] = reset;
        var usage = new JsonObject { ["primary"] = primary };
        if (includeUpdatedAt) usage["updatedAt"] = "2026-09-01T12:00:00Z";
        return new JsonObject { ["codexPlanUsage"] = usage }.ToJsonString();
    }

    private static string CreateStateDirectory(string json)
    {
        var directory = Path.Combine(Path.GetTempPath(), "BalanceIsland.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "state.json"), json);
        return directory;
    }
}
