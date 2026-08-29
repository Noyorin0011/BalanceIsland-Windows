using BalanceIsland.Windows;

namespace BalanceIsland.Windows.Tests;

public sealed class ProviderCatalogTests
{
    [Fact]
    public void Every_provider_has_exactly_one_definition()
    {
        Assert.Equal(
            Enum.GetValues<Provider>().Order(),
            ProviderCatalog.All.Select(x => x.Provider).Order());
        Assert.Equal(
            ProviderCatalog.All.Count,
            ProviderCatalog.All.Select(x => x.Provider).Distinct().Count());
    }

    [Theory]
    [InlineData("kimi", Provider.Moonshot)]
    [InlineData("grok", Provider.XAI)]
    [InlineData("google", Provider.Gemini)]
    public void Search_matches_names_and_aliases(string query, Provider expected)
    {
        Assert.Contains(ProviderCatalog.Search(query), x => x.Provider == expected);
    }

    [Theory]
    [InlineData("OPENROUTER_API_KEY", Provider.OpenRouter)]
    [InlineData("sk-ant-", Provider.Anthropic)]
    [InlineData("余额接口", Provider.MiMo)]
    public void Search_matches_environment_metadata_and_limitations(string query, Provider expected)
    {
        Assert.Contains(ProviderCatalog.Search(query), x => x.Provider == expected);
    }

    [Fact]
    public void Support_rows_expose_localized_capability_matching_method_and_name_keywords()
    {
        var openAi = ProviderCatalog.Get(Provider.OpenAI);

        Assert.Equal("用量或额度", openAi.CapabilityText);
        Assert.Contains("标准变量名", openAi.MatchingMethodText);
        Assert.Contains("唯一 Key 前缀", openAi.MatchingMethodText);
        Assert.Equal("OPENAI", openAi.EnvironmentNameKeywordsText);
        Assert.All(ProviderCatalog.All, definition =>
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.CapabilityText));
            Assert.False(string.IsNullOrWhiteSpace(definition.MatchingMethodText));
        });
    }
}
