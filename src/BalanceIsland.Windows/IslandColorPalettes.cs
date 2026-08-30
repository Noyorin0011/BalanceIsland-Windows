namespace BalanceIsland.Windows;

public sealed record IslandColorPalette(
    string Normal,
    string Warning15,
    string Anomaly,
    string Critical);

public static class IslandColorPalettes
{
    public static IslandColorPalette Classic { get; } = new(
        "#FFFFFFFF", "#FFFFB340", "#FFD778FF", "#FFFF5C6C");

    public static IslandColorPalette Mint { get; } = new(
        "#FFB8F3D1", "#FFFFCF78", "#FFD8B7FF", "#FFFF8292");

    public static IslandColorPalette Sky { get; } = new(
        "#FFD2EAFF", "#FFFFCF78", "#FFD8B7FF", "#FFFF8292");

    public static IslandColorPalette Coral { get; } = new(
        "#FFFFD2C7", "#FFFFB77F", "#FFE1B8FF", "#FFFF6D7F");

    public static IslandColorPalette Lime { get; } = new(
        "#FFDFFFAC", "#FFFFCC5C", "#FFDAB8FF", "#FFFF6574");

    public static IslandColorPalette Resolve(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.IslandColorTheme switch
        {
            IslandColorTheme.Mint => Mint,
            IslandColorTheme.Sky => Sky,
            IslandColorTheme.Coral => Coral,
            IslandColorTheme.Lime => Lime,
            IslandColorTheme.Custom => new IslandColorPalette(
                NormalizeOrClassic(state.CustomNormalColor, Classic.Normal),
                NormalizeOrClassic(state.CustomWarning15Color, Classic.Warning15),
                NormalizeOrClassic(state.CustomAnomalyColor, Classic.Anomaly),
                NormalizeOrClassic(state.CustomCriticalColor, Classic.Critical)),
            _ => Classic
        };
    }

    public static bool TryNormalizeColor(string? color, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(color)) return false;

        var trimmed = color.Trim();
        if (trimmed.Length is not 7 and not 9 || trimmed[0] != '#' || !trimmed[1..].All(Uri.IsHexDigit))
            return false;

        normalized = trimmed.Length == 7
            ? "#FF" + trimmed[1..].ToUpperInvariant()
            : trimmed.ToUpperInvariant();
        return true;
    }

    private static string NormalizeOrClassic(string? color, string classic) =>
        TryNormalizeColor(color, out var normalized) ? normalized : classic;
}
