using Xunit;
using BalanceIsland.Windows;

namespace BalanceIsland.Windows.Tests;

public sealed class CodexPlanRefreshPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-01T12:00:00Z");

    [Fact]
    public void First_attempt_is_immediate() =>
        Assert.True(CodexPlanRefreshPolicy.CanAttempt(Now, null));

    [Theory]
    [InlineData(299, false)]
    [InlineData(300, true)]
    public void Attempts_have_a_five_minute_floor(int elapsedSeconds, bool expected) =>
        Assert.Equal(expected, CodexPlanRefreshPolicy.CanAttempt(Now, Now.AddSeconds(-elapsedSeconds)));

    [Theory]
    [InlineData(CodexPlanReadError.Auth, true)]
    [InlineData(CodexPlanReadError.RateLimit, true)]
    [InlineData(CodexPlanReadError.Network, false)]
    [InlineData(CodexPlanReadError.Http, false)]
    [InlineData(CodexPlanReadError.Parse, false)]
    [InlineData(CodexPlanReadError.Runtime, false)]
    public void Only_auth_and_rate_limit_pause(CodexPlanReadError error, bool expected) =>
        Assert.Equal(expected, CodexPlanRefreshPolicy.ShouldPause(error));

    [Fact]
    public void Next_delay_is_zero_when_interval_elapsed()
    {
        var elapsed = TimeSpan.FromMinutes(6);
        Assert.Equal(TimeSpan.Zero, CodexPlanRefreshPolicy.NextDelay(Now, Now - elapsed));
    }

    [Fact]
    public void Next_delay_counts_remaining_interval()
    {
        var elapsed = TimeSpan.FromMinutes(2);
        var expected = TimeSpan.FromMinutes(3);
        Assert.Equal(expected, CodexPlanRefreshPolicy.NextDelay(Now, Now - elapsed));
    }

    [Fact]
    public void Next_delay_is_zero_without_last_attempt() =>
        Assert.Equal(TimeSpan.Zero, CodexPlanRefreshPolicy.NextDelay(Now, null));
}
