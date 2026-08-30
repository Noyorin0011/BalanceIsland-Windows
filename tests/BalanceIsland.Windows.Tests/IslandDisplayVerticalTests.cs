using Xunit;

namespace BalanceIsland.Windows.Tests;

public sealed class IslandDisplayVerticalTests
{
    [Theory]
    [InlineData("今日 ¥3.00", "¥3.00")]
    [InlineData("今日 3.00", "3.00")]
    [InlineData(" 今日 ¥3.00", "¥3.00")]
    public void StripTodayMarker_removes_only_a_leading_today_marker(string input, string expected)
    {
        Assert.Equal(expected, IslandDisplayGroups.StripTodayMarker(input));
    }

    [Fact]
    public void StripTodayMarker_keeps_non_leading_today_text()
    {
        Assert.Equal("usage 今日 ¥2.00", IslandDisplayGroups.StripTodayMarker("usage 今日 ¥2.00"));
        Assert.Equal("¥2.00 今日", IslandDisplayGroups.StripTodayMarker("¥2.00 今日"));
    }

    [Fact]
    public void StripTodayMarker_handles_empty_or_null()
    {
        Assert.Equal("", IslandDisplayGroups.StripTodayMarker(""));
        Assert.Equal("", IslandDisplayGroups.StripTodayMarker(null));
    }
}
