using Xunit;
using BalanceIsland.Windows;

namespace BalanceIsland.Windows.Tests;

public sealed class EnvironmentImportTests
{
    [Fact]
    public void Import_only_adds_selected_candidates_and_deduplicates_variable()
    {
        using var coordinator = TestFactory.CreateCoordinator();
        var selected = new EnvironmentCredentialCandidate(
            Provider.OpenRouter, "ROUTER_KEY", "sk-or-v1-abc1234", "User", "Key prefix");
        var ignored = new EnvironmentCredentialCandidate(
            Provider.Gemini, "GEMINI_KEY", "AIza-example5678", "User", "Key prefix");

        coordinator.ImportEnvironmentAccounts([selected]);
        coordinator.ImportEnvironmentAccounts([selected]);

        Assert.Single(coordinator.State.Accounts);
        Assert.DoesNotContain(coordinator.State.Accounts, x => x.Provider == ignored.Provider);
        Assert.Equal("ROUTER_KEY", coordinator.State.Accounts[0].EnvironmentVariableName);
    }

    [Fact]
    public void Import_deduplicates_selected_candidates_by_provider_and_variable()
    {
        using var coordinator = TestFactory.CreateCoordinator();
        var first = new EnvironmentCredentialCandidate(
            Provider.OpenRouter, "ROUTER_KEY", "sk-or-v1-abc1234", "User", "Key prefix");
        var duplicate = new EnvironmentCredentialCandidate(
            Provider.OpenRouter, "router_key", "sk-or-v1-different5678", "Machine", "Key prefix");

        coordinator.ImportEnvironmentAccounts([first, duplicate]);

        Assert.Single(coordinator.State.Accounts);
        Assert.Equal("1234", coordinator.State.Accounts[0].KeySuffix);
    }

    [Fact]
    public async Task Import_refreshes_all_new_enabled_accounts_through_one_serial_flow()
    {
        const string firstVariable = "BALANCE_ISLAND_TASK6_FIRST";
        const string secondVariable = "BALANCE_ISLAND_TASK6_SECOND";
        var previousFirst = Environment.GetEnvironmentVariable(firstVariable, EnvironmentVariableTarget.Process);
        var previousSecond = Environment.GetEnvironmentVariable(secondVariable, EnvironmentVariableTarget.Process);
        var client = new BlockingProviderClient();
        try
        {
            Environment.SetEnvironmentVariable(firstVariable, "first-secret", EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(secondVariable, "second-secret", EnvironmentVariableTarget.Process);
            using var coordinator = TestFactory.CreateCoordinator(client: client);

            coordinator.ImportEnvironmentAccounts(
            [
                new EnvironmentCredentialCandidate(Provider.OpenRouter, firstVariable, "first-secret"),
                new EnvironmentCredentialCandidate(Provider.OpenRouter, secondVariable, "second-secret")
            ]);

            await client.FirstRequestStarted.WaitAsync(TimeSpan.FromSeconds(1));
            client.CompleteFirstRequest();
            await client.SecondRequestStarted.WaitAsync(TimeSpan.FromSeconds(1));
            await WaitUntilAsync(() => coordinator.State.Snapshots.Values.Count == 2 &&
                coordinator.State.Snapshots.Values.All(snapshot => snapshot.PrimaryText == "已刷新"));

            Assert.Equal(2, client.RequestedCredentialIds.Count);
        }
        finally
        {
            Environment.SetEnvironmentVariable(firstVariable, previousFirst, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(secondVariable, previousSecond, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void Imported_row_cannot_be_selected()
    {
        var row = new EnvironmentImportRow(
            new EnvironmentCredentialCandidate(Provider.OpenRouter, "ROUTER_KEY", "sk-or-v1-abc1234"),
            isAlreadyImported: true);

        row.IsSelected = true;

        Assert.False(row.IsSelected);
    }

    [Fact]
    public void Unclassified_row_cannot_be_selected_until_a_provider_is_explicitly_chosen()
    {
        var candidate = new EnvironmentCredentialCandidate(null, "MY_API_KEY", "sk-example1234");
        var row = new EnvironmentImportRow(candidate, isAlreadyImported: false);

        row.IsSelected = true;
        Assert.False(row.IsSelected);

        row.SelectedProvider = Provider.DeepSeek;
        row.IsSelected = true;

        Assert.True(row.IsSelected);
        Assert.Equal(Provider.DeepSeek, row.ResolvedCandidate.Provider);
    }

    [Fact]
    public void Coordinator_rejects_an_unclassified_candidate_without_mutating_state()
    {
        using var coordinator = TestFactory.CreateCoordinator();
        var candidate = new EnvironmentCredentialCandidate(null, "MY_API_KEY", "sk-example1234");

        var error = Assert.Throws<ArgumentException>(() => coordinator.ImportEnvironmentAccounts([candidate]));

        Assert.Contains("Provider", error.Message);
        Assert.Empty(coordinator.State.Accounts);
    }

    [Fact]
    public void Explicitly_classified_candidate_imports_with_the_selected_provider()
    {
        using var coordinator = TestFactory.CreateCoordinator();
        var candidate = new EnvironmentCredentialCandidate(null, "MY_API_KEY", "sk-example1234")
            .WithProvider(Provider.DeepSeek);

        coordinator.ImportEnvironmentAccounts([candidate]);

        Assert.Equal(Provider.DeepSeek, Assert.Single(coordinator.State.Accounts).Provider);
    }

    [Fact]
    public async Task Environment_rotation_uses_the_new_secret_and_persists_only_its_safe_suffix()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BalanceIsland.Tests", Guid.NewGuid().ToString("N"));
        var variableName = $"BALANCE_ISLAND_ROTATION_{Guid.NewGuid():N}";
        const string rotatedSecret = "rotated-secret-9876";
        var account = new Account
        {
            Id = "account",
            Provider = Provider.OpenRouter,
            KeySuffix = "1234",
            CredentialSource = CredentialSource.EnvironmentVariable,
            EnvironmentVariableName = variableName
        };
        var store = new AppDataStore(directory);
        store.Save(new AppState
        {
            SafeKeySuffixVersion = 1,
            Accounts = [account],
            Snapshots = new Dictionary<string, BalanceSnapshot>
            {
                [account.Id] = new()
                {
                    Provider = account.Provider,
                    CredentialId = account.Id,
                    KeySuffix = account.KeySuffix
                }
            }
        });
        var client = new CapturingProviderClient();
        Environment.SetEnvironmentVariable(variableName, rotatedSecret, EnvironmentVariableTarget.Process);
        try
        {
            using var coordinator = new BalanceCoordinator(store, new WindowsCredentialStore(), client);

            await coordinator.RefreshDueAsync(force: true, targetCredentialId: account.Id);

            Assert.Equal(rotatedSecret, client.RequestedSecret);
            Assert.Equal("9876", coordinator.State.Accounts.Single().KeySuffix);
            Assert.Equal("9876", coordinator.State.Snapshots[account.Id].KeySuffix);
            var json = File.ReadAllText(Path.Combine(directory, "state.json"));
            Assert.DoesNotContain(rotatedSecret, json, StringComparison.Ordinal);
            Assert.Contains("9876", json, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void Rescan_updates_an_existing_environment_account_suffix_without_creating_a_duplicate()
    {
        var account = new Account
        {
            Id = "account",
            Provider = Provider.OpenRouter,
            KeySuffix = "1234",
            CredentialSource = CredentialSource.EnvironmentVariable,
            EnvironmentVariableName = "ROUTER_KEY"
        };
        using var coordinator = TestFactory.CreateCoordinator(new AppState
        {
            SafeKeySuffixVersion = 1,
            Accounts = [account],
            Snapshots = new Dictionary<string, BalanceSnapshot>
            {
                [account.Id] = new() { CredentialId = account.Id, KeySuffix = account.KeySuffix }
            }
        });

        coordinator.ImportEnvironmentAccounts(
        [
            new EnvironmentCredentialCandidate(
                Provider.OpenRouter,
                "ROUTER_KEY",
                "rotated-secret-5678",
                "User",
                "标准变量名")
        ]);

        Assert.Single(coordinator.State.Accounts);
        Assert.Equal("5678", coordinator.State.Accounts[0].KeySuffix);
        Assert.Equal("5678", coordinator.State.Snapshots[account.Id].KeySuffix);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(1);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("Timed out waiting for the imported-account refresh batch.");
            await Task.Delay(10);
        }
    }

    private sealed class BlockingProviderClient : ProviderClient
    {
        private readonly TaskCompletionSource<bool> _firstRequestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _secondRequestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _completeFirstRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstRequestStarted => _firstRequestStarted.Task;
        public Task SecondRequestStarted => _secondRequestStarted.Task;
        public List<string> RequestedCredentialIds { get; } = [];

        public override async Task<BalanceSnapshot> FetchAsync(ApiCredential credential, CancellationToken token)
        {
            RequestedCredentialIds.Add(credential.Account.Id);
            if (RequestedCredentialIds.Count == 1)
            {
                _firstRequestStarted.TrySetResult(true);
                await _completeFirstRequest.Task;
            }
            else
            {
                _secondRequestStarted.TrySetResult(true);
            }

            return new BalanceSnapshot
            {
                Provider = credential.Account.Provider,
                CredentialId = credential.Account.Id,
                AccountLabel = credential.Account.Label,
                KeySuffix = credential.Account.KeySuffix,
                PrimaryText = "已刷新",
                CurrencyCode = credential.Account.Provider.DefaultCurrency(),
                Status = SnapshotStatus.Ok,
                UpdatedAt = DateTimeOffset.Now
            };
        }

        public void CompleteFirstRequest() => _completeFirstRequest.TrySetResult(true);
    }

    private sealed class CapturingProviderClient : ProviderClient
    {
        public string? RequestedSecret { get; private set; }

        public override Task<BalanceSnapshot> FetchAsync(ApiCredential credential, CancellationToken token)
        {
            RequestedSecret = credential.ApiKey;
            return Task.FromResult(new BalanceSnapshot
            {
                Provider = credential.Account.Provider,
                CredentialId = credential.Account.Id,
                AccountLabel = credential.Account.Label,
                KeySuffix = credential.Account.KeySuffix,
                BalanceAmount = 30,
                CurrencyCode = "USD",
                PrimaryText = "$30.00",
                Status = SnapshotStatus.Ok,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
    }
}
