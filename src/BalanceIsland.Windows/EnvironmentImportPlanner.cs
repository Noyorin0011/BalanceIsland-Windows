namespace BalanceIsland.Windows;

public enum EnvironmentPromptAction
{
    None,
    ShowDialog,
    Notify
}

public static class EnvironmentPromptPolicy
{
    public static EnvironmentPromptAction ForStartup(bool silent, int newCandidateCount)
    {
        if (newCandidateCount <= 0) return EnvironmentPromptAction.None;
        return silent ? EnvironmentPromptAction.Notify : EnvironmentPromptAction.ShowDialog;
    }
}

public static class EnvironmentImportPlanner
{
    public static IReadOnlyList<EnvironmentCredentialCandidate> FindNew(
        IEnumerable<EnvironmentCredentialCandidate> candidates,
        IEnumerable<Account> existingAccounts,
        Func<Account, string?> resolveSecret)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(existingAccounts);
        ArgumentNullException.ThrowIfNull(resolveSecret);

        var existing = existingAccounts
            .OfType<Account>()
            .Select(account => new ExistingCredential(account, ReadCleanSecret(account, resolveSecret)))
            .ToArray();
        var accepted = new List<CandidateIdentity>();
        var result = new List<EnvironmentCredentialCandidate>();

        foreach (var candidate in candidates)
        {
            if (candidate is null) continue;
            var key = ApiKeySanitizer.Clean(candidate.ApiKey);
            if (string.IsNullOrWhiteSpace(key)) continue;

            var sameVariable = candidate.Provider is { } provider
                ? existing.Any(item =>
                    item.Account.CredentialSource == CredentialSource.EnvironmentVariable &&
                    item.Account.Provider == provider &&
                    string.Equals(
                        item.Account.EnvironmentVariableName,
                        candidate.VariableName,
                        StringComparison.OrdinalIgnoreCase))
                  || accepted.Any(item =>
                    item.Provider == provider &&
                    string.Equals(item.VariableName, candidate.VariableName, StringComparison.OrdinalIgnoreCase))
                : existing.Any(item =>
                    item.Account.CredentialSource == CredentialSource.EnvironmentVariable &&
                    string.Equals(
                        item.Account.EnvironmentVariableName,
                        candidate.VariableName,
                        StringComparison.OrdinalIgnoreCase))
                  || accepted.Any(item =>
                    string.Equals(item.VariableName, candidate.VariableName, StringComparison.OrdinalIgnoreCase));

            var sameKey = candidate.Provider is { } knownProvider
                ? existing.Any(item =>
                    item.Account.Provider == knownProvider &&
                    string.Equals(item.ApiKey, key, StringComparison.Ordinal))
                  || accepted.Any(item =>
                    item.Provider == knownProvider &&
                    string.Equals(item.ApiKey, key, StringComparison.Ordinal))
                : existing.Any(item => string.Equals(item.ApiKey, key, StringComparison.Ordinal))
                  || accepted.Any(item => string.Equals(item.ApiKey, key, StringComparison.Ordinal));

            if (sameVariable || sameKey) continue;
            result.Add(candidate);
            accepted.Add(new CandidateIdentity(candidate.Provider, candidate.VariableName, key));
        }

        return result;
    }

    private static string ReadCleanSecret(
        Account account,
        Func<Account, string?> resolveSecret)
    {
        try
        {
            return ApiKeySanitizer.Clean(resolveSecret(account));
        }
        catch
        {
            return "";
        }
    }

    private sealed record ExistingCredential(Account Account, string ApiKey);
    private sealed record CandidateIdentity(Provider? Provider, string VariableName, string ApiKey);
}
