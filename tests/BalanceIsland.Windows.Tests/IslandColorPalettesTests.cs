using Xunit;
using BalanceIsland.Windows;

namespace BalanceIsland.Windows.Tests;

public sealed class IslandColorPalettesTests
{
    [Theory]
    [InlineData("#12ABef", "#FF12ABEF")]
    [InlineData("#8012ABEF", "#8012ABEF")]
    public void Normalize_accepts_rgb_and_argb(string input, string expected)
    {
        Assert.True(IslandColorPalettes.TryNormalizeColor(input, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#GG0000")]
    [InlineData("")]
    public void Normalize_rejects_non_hex_colors(string input) =>
        Assert.False(IslandColorPalettes.TryNormalizeColor(input, out _));

    [Theory]
    [InlineData(IslandColorTheme.Classic)]
    [InlineData(IslandColorTheme.Mint)]
    [InlineData(IslandColorTheme.Sky)]
    [InlineData(IslandColorTheme.Coral)]
    [InlineData(IslandColorTheme.Lime)]
    public void Every_builtin_theme_resolves_a_complete_immutable_palette(IslandColorTheme theme)
    {
        var palette = IslandColorPalettes.Resolve(new AppState { IslandColorTheme = theme });

        Assert.False(string.IsNullOrWhiteSpace(palette.Normal));
        Assert.False(string.IsNullOrWhiteSpace(palette.Warning15));
        Assert.False(string.IsNullOrWhiteSpace(palette.Anomaly));
        Assert.False(string.IsNullOrWhiteSpace(palette.Critical));
        Assert.Equal("#FFFFFFFF", IslandColorPalettes.Classic.Normal);
    }

    [Fact]
    public void Custom_theme_uses_normalized_state_colors()
    {
        var palette = IslandColorPalettes.Resolve(new AppState
        {
            IslandColorTheme = IslandColorTheme.Custom,
            CustomNormalColor = "#123456",
            CustomWarning15Color = "#80123456",
            CustomAnomalyColor = "#abcdef",
            CustomCriticalColor = "#7F010203"
        });

        Assert.Equal("#FF123456", palette.Normal);
        Assert.Equal("#80123456", palette.Warning15);
        Assert.Equal("#FFABCDEF", palette.Anomaly);
        Assert.Equal("#7F010203", palette.Critical);
    }

    [Fact]
    public void Normalizer_replaces_invalid_custom_colors_with_classic_defaults()
    {
        var state = new AppState
        {
            CustomNormalColor = "red",
            CustomWarning15Color = "#12345",
            CustomAnomalyColor = "#GG0000",
            CustomCriticalColor = "#010203"
        };

        AppStateNormalizer.Normalize(state);

        Assert.Equal(IslandColorPalettes.Classic.Normal, state.CustomNormalColor);
        Assert.Equal(IslandColorPalettes.Classic.Warning15, state.CustomWarning15Color);
        Assert.Equal(IslandColorPalettes.Classic.Anomaly, state.CustomAnomalyColor);
        Assert.Equal("#FF010203", state.CustomCriticalColor);
    }
}
