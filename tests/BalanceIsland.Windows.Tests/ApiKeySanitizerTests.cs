using Xunit;
using BalanceIsland.Windows;

namespace BalanceIsland.Windows.Tests;

public sealed class ApiKeySanitizerTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("abc")]
    [InlineData("abcd")]
    public void SafeKeySuffix_does_not_retain_a_complete_short_secret(string secret)
    {
        Assert.Equal("", ApiKeySanitizer.SafeKeySuffix(secret));
        Assert.Equal("••••", ApiKeySanitizer.MaskSecret(secret));
    }

    [Fact]
    public void SafeKeySuffix_retains_only_the_last_four_characters_of_a_long_secret()
    {
        Assert.Equal("bcde", ApiKeySanitizer.SafeKeySuffix("abcde"));
        Assert.Equal("••••bcde", ApiKeySanitizer.MaskSecret("abcde"));
    }

    [Fact]
    public void Normalizer_defensively_clears_legacy_suffixes_that_may_be_complete_short_secrets()
    {
        var state = new AppState
        {
            Accounts = [new() { Id = "account", KeySuffix = "abcd" }],
            Snapshots = new Dictionary<string, BalanceSnapshot>
            {
                ["account"] = new() { CredentialId = "account", KeySuffix = "abcd" }
            },
            SafeKeySuffixVersion = null
        };

        AppStateNormalizer.Normalize(state);

        Assert.Equal("", state.Accounts[0].KeySuffix);
        Assert.Equal("", state.Snapshots["account"].KeySuffix);
        Assert.Equal(1, state.SafeKeySuffixVersion);
        Assert.Equal("••••", state.Accounts[0].DisplayLabel);
        Assert.Equal("••••", state.Snapshots["account"].AccountDisplayLabel);
    }
}
