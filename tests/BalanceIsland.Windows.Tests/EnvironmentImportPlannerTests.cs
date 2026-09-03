using Xunit;

namespace BalanceIsland.Windows.Tests;

public sealed class EnvironmentImportPlannerTests
{
    [Fact]
    public void Existing_provider_and_variable_is_not_new_after_key_rotation()
    {
        var account = EnvironmentAccount(Provider.OpenRouter, "OPENROUTER_API_KEY");
        var candidate = new EnvironmentCredentialCandidate(
            Provider.OpenRouter, "openrouter_api_key", "rotated-secret");

        var actual = EnvironmentImportPlanner.FindNew(
            [candidate],
            [account],
            _ => "rotated-secret");

        Assert.Empty(actual);
    }

    [Fact]
    public void Same_provider_and_key_under_another_variable_is_not_new()
    {
        var account = EnvironmentAccount(Provider.OpenRouter, "FIRST_KEY");
        var candidate = new EnvironmentCredentialCandidate(
            Provider.OpenRouter, "SECOND_KEY", "Bearer shared-secret");

        var actual = EnvironmentImportPlanner.FindNew(
            [candidate],
            [account],
            _ => "shared-secret");

        Assert.Empty(actual);
    }

    [Fact]
    public void Same_provider_and_explicit_credential_is_not_new()
    {
        var account = new Account
        {
            Provider = Provider.OpenRouter,
            CredentialSource = CredentialSource.WindowsCredentialManager
        };
        var candidate = new EnvironmentCredentialCandidate(
            Provider.OpenRouter, "OPENROUTER_API_KEY", "shared-secret");

        var actual = EnvironmentImportPlanner.FindNew(
            [candidate],
            [account],
            _ => "shared-secret");

        Assert.Empty(actual);
    }

    [Fact]
    public void Different_key_for_the_same_provider_is_new()
    {
        var account = EnvironmentAccount(Provider.OpenRouter, "FIRST_KEY");
        var candidate = new EnvironmentCredentialCandidate(
            Provider.OpenRouter, "SECOND_KEY", "new-secret");

        var actual = EnvironmentImportPlanner.FindNew(
            [candidate],
            [account],
            _ => "old-secret");

        Assert.Same(candidate, Assert.Single(actual));
    }

    [Fact]
    public void Unclassified_candidate_with_existing_variable_is_not_new()
    {
        var account = EnvironmentAccount(Provider.DeepSeek, "MY_API_KEY");
        var candidate = new EnvironmentCredentialCandidate(
            null, "my_api_key", "different-secret");

        var actual = EnvironmentImportPlanner.FindNew(
            [candidate],
            [account],
            _ => "old-secret");

        Assert.Empty(actual);
    }

    [Fact]
    public void Unclassified_candidate_with_existing_key_is_not_new()
    {
        var account = EnvironmentAccount(Provider.DeepSeek, "DEEPSEEK_API_KEY");
        var candidate = new EnvironmentCredentialCandidate(
            null, "MY_API_KEY", "shared-secret");

        var actual = EnvironmentImportPlanner.FindNew(
            [candidate],
            [account],
            _ => "shared-secret");

        Assert.Empty(actual);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Classified_and_unclassified_candidates_with_the_same_key_are_deduplicated_regardless_of_order(
        bool unclassifiedFirst)
    {
        var classified = new EnvironmentCredentialCandidate(
            Provider.OpenRouter, "OPENROUTER_API_KEY", "shared-secret");
        var unclassified = new EnvironmentCredentialCandidate(
            null, "CUSTOM_API_KEY", "Bearer shared-secret");
        var candidates = unclassifiedFirst
            ? new[] { unclassified, classified }
            : new[] { classified, unclassified };

        var actual = EnvironmentImportPlanner.FindNew(
            candidates,
            [],
            _ => null);

        Assert.Same(classified, Assert.Single(actual));
    }

    [Theory]
    [InlineData(false, 0, EnvironmentPromptAction.None)]
    [InlineData(true, 0, EnvironmentPromptAction.None)]
    [InlineData(false, 1, EnvironmentPromptAction.ShowDialog)]
    [InlineData(true, 1, EnvironmentPromptAction.Notify)]
    public void Startup_prompt_policy_only_acts_for_new_candidates(
        bool silent,
        int newCandidateCount,
        EnvironmentPromptAction expected)
    {
        Assert.Equal(
            expected,
            EnvironmentPromptPolicy.ForStartup(silent, newCandidateCount));
    }

    private static Account EnvironmentAccount(Provider provider, string variableName) => new()
    {
        Provider = provider,
        CredentialSource = CredentialSource.EnvironmentVariable,
        EnvironmentVariableName = variableName
    };
}
