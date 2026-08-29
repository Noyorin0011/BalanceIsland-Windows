namespace BalanceIsland.Windows;

public static class EnvironmentCredentialDiscovery
{
    public static IReadOnlyList<EnvironmentCredentialCandidate> Scan(IEnumerable<EnvironmentVariableEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var candidates = new List<EnvironmentCredentialCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Value)) continue;
            if (!seen.Add($"{entry.Scope}\0{entry.Name}")) continue;

            var key = ApiKeySanitizer.Clean(entry.Value);
            if (string.IsNullOrWhiteSpace(key)) continue;

            var definition = Match(entry.Name, key);
            if (definition is null && !key.StartsWith("sk-", StringComparison.OrdinalIgnoreCase)) continue;

            candidates.Add(new EnvironmentCredentialCandidate(
                definition?.Provider,
                entry.Name,
                key,
                entry.Scope,
                definition is null ? "待选择 Provider" : MatchReason(definition, entry.Name)));
        }
        return candidates;
    }

    public static IReadOnlyList<EnvironmentCredentialCandidate> Scan()
    {
        var entries = new List<EnvironmentVariableEntry>();
        var namesSeenAtHigherPriority = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in new[]
                 {
                     EnvironmentVariableTarget.Process,
                     EnvironmentVariableTarget.User,
                     EnvironmentVariableTarget.Machine
                 })
        {
            try
            {
                var variables = Environment.GetEnvironmentVariables(target);
                foreach (System.Collections.DictionaryEntry variable in variables)
                {
                    if (variable.Key is not string name || variable.Value is not string value ||
                        string.IsNullOrWhiteSpace(value) || !namesSeenAtHigherPriority.Add(name)) continue;
                    entries.Add(new EnvironmentVariableEntry(name, value, target.ToString()));
                }
            }
            catch
            {
                // User and machine stores can be unavailable or access-restricted.
            }
        }
        return Scan(entries);
    }

    public static string? Read(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        foreach (var target in new[]
                 {
                     EnvironmentVariableTarget.Process,
                     EnvironmentVariableTarget.User,
                     EnvironmentVariableTarget.Machine
                 })
        {
            try
            {
                var value = Environment.GetEnvironmentVariable(name, target);
                if (string.IsNullOrWhiteSpace(value)) continue;

                var key = ApiKeySanitizer.Clean(value);
                if (!string.IsNullOrWhiteSpace(key)) return key;
            }
            catch
            {
                // Continue to the next scope when this one cannot be read.
            }
        }
        return null;
    }

    public static string SupportedVariablesText => string.Join(" · ", ProviderCatalog.All.Select(definition =>
        $"{definition.DisplayName}: {string.Join(" / ", definition.EnvironmentVariableNames)}"));

    public static string LimitationsText => string.Join(" ", ProviderCatalog.All
        .Where(definition => !string.IsNullOrWhiteSpace(definition.Limitations))
        .Select(definition => $"{definition.DisplayName}：{definition.Limitations}"));

    private static ProviderDefinition? Match(string name, string key) =>
        ProviderCatalog.All.FirstOrDefault(definition => definition.EnvironmentVariableNames
            .Contains(name, StringComparer.OrdinalIgnoreCase))
        ?? ProviderCatalog.All.FirstOrDefault(definition => definition.EnvironmentNameKeywords
            .Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        ?? ProviderCatalog.All.FirstOrDefault(definition => definition.UniqueKeyPrefixes
            .Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));

    private static string MatchReason(ProviderDefinition definition, string name)
    {
        if (definition.EnvironmentVariableNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return "标准变量名";
        if (definition.EnvironmentNameKeywords.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase))) return "变量名关键词";
        return "Key 前缀";
    }
}

public sealed record EnvironmentVariableEntry(string Name, string Value, string Scope);

public sealed class EnvironmentCredentialCandidate
{
    private readonly string _apiKey;

    public EnvironmentCredentialCandidate(Provider? provider, string variableName, string apiKey)
        : this(provider, variableName, apiKey, "Process", "标准变量名")
    {
    }

    public EnvironmentCredentialCandidate(Provider? provider, string variableName, string apiKey, string scope, string matchReason)
    {
        Provider = provider;
        VariableName = variableName;
        _apiKey = apiKey;
        Scope = scope;
        MatchReason = matchReason;
    }

    public Provider? Provider { get; }
    public string VariableName { get; }
    public string Scope { get; }
    public string MatchReason { get; }
    public string MaskedKey => ApiKeySanitizer.MaskSecret(_apiKey, revealSafeSuffix: Provider is not null);
    internal string ApiKey => _apiKey;

    public EnvironmentCredentialCandidate WithProvider(Provider provider) => new(
        provider,
        VariableName,
        _apiKey,
        Scope,
        Provider is null ? "用户明确选择" : MatchReason);

    public override string ToString() => $"{(Provider is { } provider ? provider.DisplayName() : "未分类")} · {VariableName} · {Scope} · {MaskedKey} · {MatchReason}";
}
