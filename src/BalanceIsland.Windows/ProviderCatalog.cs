namespace BalanceIsland.Windows;

public sealed record ProviderDefinition(
    Provider Provider,
    string DisplayName,
    IReadOnlyList<string> Aliases,
    string IconResourceKey,
    string DefaultCurrency,
    BalanceCapability Capability,
    int RecommendedRefreshMinutes,
    IReadOnlyList<string> EnvironmentVariableNames,
    IReadOnlyList<string> EnvironmentNameKeywords,
    IReadOnlyList<string> UniqueKeyPrefixes,
    string Limitations)
{
    public string EnvironmentVariableNamesText => string.Join(" / ", EnvironmentVariableNames);
    public string EnvironmentNameKeywordsText => EnvironmentNameKeywords.Count == 0
        ? "—"
        : string.Join(" / ", EnvironmentNameKeywords);
    public string UniqueKeyPrefixesText => UniqueKeyPrefixes.Count == 0 ? "—" : string.Join(" / ", UniqueKeyPrefixes);
    public string CapabilityText => Capability switch
    {
        BalanceCapability.DirectBalance => "余额查询",
        BalanceCapability.UsageOrLimit => "用量或额度",
        _ => "仅验证 Key"
    };
    public string MatchingMethodText => string.Join("；", new[]
    {
        EnvironmentVariableNames.Count == 0 ? null : $"标准变量名：{EnvironmentVariableNamesText}",
        EnvironmentNameKeywords.Count == 0 ? null : $"变量名关键词：{EnvironmentNameKeywordsText}",
        UniqueKeyPrefixes.Count == 0 ? null : $"唯一 Key 前缀：{UniqueKeyPrefixesText}"
    }.Where(value => value is not null));
}

public static class ProviderCatalog
{
    private static readonly IReadOnlyList<ProviderDefinition> Definitions =
    [
        new(Provider.DeepSeek, "DeepSeek", ["deepseek"], "ProviderIcon.DeepSeek", "CNY", BalanceCapability.DirectBalance, 1, ["DEEPSEEK_API_KEY"], ["DEEPSEEK"], [], "支持余额查询。"),
        new(Provider.OpenAI, "OpenAI", ["chatgpt"], "ProviderIcon.OpenAI", "USD", BalanceCapability.UsageOrLimit, 5, ["OPENAI_ADMIN_KEY", "OPENAI_API_KEY"], ["OPENAI"], ["sk-admin-", "sk-proj-", "sk-svcacct-"], "普通 API Key 仅验证 Key；余额或组织消费查询需要 Admin Key。"),
        new(Provider.OpenRouter, "OpenRouter", ["open router"], "ProviderIcon.OpenRouter", "USD", BalanceCapability.DirectBalance, 2, ["OPENROUTER_API_KEY"], ["OPENROUTER"], ["sk-or-"], "支持余额查询。"),
        new(Provider.SiliconFlow, "SiliconFlow", ["silicon flow"], "ProviderIcon.SiliconFlow", "CNY", BalanceCapability.DirectBalance, 2, ["SILICONFLOW_API_KEY"], ["SILICONFLOW"], [], "支持余额查询。"),
        new(Provider.Moonshot, "Kimi / Moonshot", ["kimi", "moonshot"], "ProviderIcon.Moonshot", "CNY", BalanceCapability.DirectBalance, 2, ["MOONSHOT_API_KEY", "KIMI_API_KEY"], ["MOONSHOT", "KIMI"], [], "支持余额查询。"),
        new(Provider.MiMo, "Xiaomi MiMo", ["mimo", "xiaomi"], "ProviderIcon.MiMo", "CNY", BalanceCapability.KeyCheckOnly, 15, ["MIMO_API_KEY", "XIAOMI_MIMO_API_KEY"], ["MIMO", "XIAOMI"], [], "当前仅验证 Key，没有可用的官方余额接口。"),
        new(Provider.Anthropic, "Anthropic", ["claude"], "ProviderIcon.Anthropic", "USD", BalanceCapability.KeyCheckOnly, 15, ["ANTHROPIC_API_KEY"], ["ANTHROPIC", "CLAUDE"], ["sk-ant-"], "当前仅验证 Key，没有可用的官方余额接口。"),
        new(Provider.Gemini, "Google Gemini", ["gemini", "google"], "ProviderIcon.Gemini", "USD", BalanceCapability.KeyCheckOnly, 15, ["GEMINI_API_KEY", "GOOGLE_API_KEY"], ["GEMINI", "GOOGLE"], ["AIza"], "当前仅验证 Key，没有可用的官方余额接口。"),
        new(Provider.XAI, "xAI / Grok", ["xai", "grok"], "ProviderIcon.XAI", "USD", BalanceCapability.KeyCheckOnly, 15, ["XAI_API_KEY", "GROK_API_KEY"], ["XAI", "GROK"], ["xai-"], "当前仅验证 Key，没有可用的官方余额接口。")
    ];

    public static IReadOnlyList<ProviderDefinition> All => Definitions;

    public static ProviderDefinition Get(Provider provider) => Definitions.FirstOrDefault(x => x.Provider == provider)
        ?? throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider.");

    public static IReadOnlyList<ProviderDefinition> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return All;

        var search = query.Trim();
        return Definitions.Where(definition =>
            definition.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            definition.Provider.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ||
            definition.Aliases.Any(alias => alias.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
            definition.EnvironmentVariableNames.Any(name => name.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
            definition.EnvironmentNameKeywords.Any(keyword => keyword.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
            definition.UniqueKeyPrefixes.Any(prefix => prefix.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
            definition.Limitations.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToArray();
}
}
