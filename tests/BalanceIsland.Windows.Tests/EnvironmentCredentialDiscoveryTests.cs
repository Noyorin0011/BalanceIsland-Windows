using BalanceIsland.Windows;

namespace BalanceIsland.Windows.Tests;

public sealed class EnvironmentCredentialDiscoveryTests
{
    [Theory]
    [InlineData("WHATEVER", "sk-or-v1-abc1234", Provider.OpenRouter)]
    [InlineData("WHATEVER", "sk-ant-api03-abc1234", Provider.Anthropic)]
    [InlineData("WHATEVER", "AIzaSyExample1234", Provider.Gemini)]
    [InlineData("WHATEVER", "xai-example1234", Provider.XAI)]
    [InlineData("MY_KIMI_SECRET", "sk-example1234", Provider.Moonshot)]
    public void Scan_matches_unique_prefix_or_name_keyword(string name, string value, Provider provider)
    {
        var candidate = Assert.Single(EnvironmentCredentialDiscovery.Scan(
            [new EnvironmentVariableEntry(name, value, "User")]));

        Assert.Equal(provider, candidate.Provider);
        Assert.Equal("••••1234", candidate.MaskedKey);
    }

    [Fact]
    public void Generic_sk_key_without_provider_name_is_not_guessed()
    {
        const string key = "sk-example1234";
        var candidate = Assert.Single(EnvironmentCredentialDiscovery.Scan(
            [new EnvironmentVariableEntry("MY_API_KEY", key, "User")]));

        Assert.Null(candidate.Provider);
        Assert.Equal("••••", candidate.MaskedKey);
        Assert.Equal("待选择 Provider", candidate.MatchReason);
        Assert.DoesNotContain(key, candidate.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("1234", candidate.MaskedKey, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_deduplicates_identical_variable_name_and_scope()
    {
        var candidates = EnvironmentCredentialDiscovery.Scan(
        [
            new EnvironmentVariableEntry("OPENROUTER_API_KEY", "sk-or-v1-abc1234", "Process"),
            new EnvironmentVariableEntry("openrouter_api_key", "sk-or-v1-other5678", "Process")
        ]);

        var candidate = Assert.Single(candidates);
        Assert.Equal("••••1234", candidate.MaskedKey);
    }

    [Fact]
    public void Candidate_never_includes_the_complete_key_in_display_metadata()
    {
        const string key = "sk-or-v1-secret1234";
        var candidate = Assert.Single(EnvironmentCredentialDiscovery.Scan(
            [new EnvironmentVariableEntry("OPENROUTER_API_KEY", key, "User")]));

        Assert.DoesNotContain(key, candidate.MaskedKey, StringComparison.Ordinal);
        Assert.DoesNotContain(key, candidate.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("abc")]
    [InlineData("abcd")]
    public void Candidate_masks_a_short_key_without_disclosing_it(string key)
    {
        var candidate = Assert.Single(EnvironmentCredentialDiscovery.Scan(
            [new EnvironmentVariableEntry("OPENAI_API_KEY", key, "User")]));

        Assert.Equal("••••", candidate.MaskedKey);
        Assert.DoesNotContain(key, candidate.MaskedKey, StringComparison.Ordinal);
        Assert.DoesNotContain(key, candidate.ToString(), StringComparison.Ordinal);
    }
}
