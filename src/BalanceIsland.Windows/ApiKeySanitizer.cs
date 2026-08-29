using System.Text.RegularExpressions;

namespace BalanceIsland.Windows;

public static partial class ApiKeySanitizer
{
    public const string IrreversiblePlaceholder = "••••";

    [GeneratedRegex("^Bearer\\s+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerPrefix();

    [GeneratedRegex("(?:sk-[A-Za-z0-9_-]{8,}|tp-[A-Za-z0-9_-]{8,}|AQ\\.[A-Za-z0-9._-]{8,}|AIza[A-Za-z0-9_-]{8,}|xai-[A-Za-z0-9_-]{8,})")]
    private static partial Regex KnownKey();

    public static string Clean(string raw)
    {
        var trimmed = raw.Trim().Trim('"', '\'', '`');
        var withoutBearer = BearerPrefix().Replace(trimmed, "", 1).Trim();
        var match = KnownKey().Match(withoutBearer);
        return match.Success ? match.Value : withoutBearer;
    }

    public static string SafeKeySuffix(string? secret) => secret is { Length: > 4 }
        ? secret[^4..]
        : "";

    public static string MaskSecret(string? secret, bool revealSafeSuffix = true)
    {
        var suffix = SafeKeySuffix(secret);
        return revealSafeSuffix && suffix.Length == 4
            ? $"{IrreversiblePlaceholder}{suffix}"
            : IrreversiblePlaceholder;
    }

    public static string MaskSuffix(string? safeSuffix) => safeSuffix?.Length == 4
        ? $"{IrreversiblePlaceholder}{safeSuffix}"
        : IrreversiblePlaceholder;
}
