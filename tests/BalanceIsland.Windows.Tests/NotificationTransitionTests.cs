using Xunit;
using BalanceIsland.Windows;

namespace BalanceIsland.Windows.Tests;

public sealed class NotificationTransitionTests
{
    [Fact]
    public async Task Entered_alert_is_persisted_before_delivery_and_exposes_a_masked_suffix()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BalanceIsland.Tests", Guid.NewGuid().ToString("N"));
        var variableName = $"BALANCE_ISLAND_TASK7_{Guid.NewGuid():N}";
        var account = new Account
        {
            Id = "account",
            Provider = Provider.OpenAI,
            Label = "Production",
            KeySuffix = "1234",
            AlertEnabled = true,
            WarningLine = 20,
            CredentialSource = CredentialSource.EnvironmentVariable,
            EnvironmentVariableName = variableName
        };
        var store = new AppDataStore(directory);
        store.Save(new AppState { Accounts = [account] });
        Environment.SetEnvironmentVariable(variableName, "test-key");
        try
        {
            using var coordinator = new BalanceCoordinator(store, new WindowsCredentialStore(), new FixedBalanceClient(22));
            BalanceAlertEventArgs? alert = null;
            BalanceAlertState? persistedDuringDelivery = null;
            coordinator.AlertRaised += (_, raised) =>
            {
                alert = raised;
                persistedDuringDelivery = store.LoadResult().State.Alerts[account.Id];
            };

            await coordinator.RefreshDueAsync(force: true, targetCredentialId: account.Id);

            Assert.NotNull(alert);
            Assert.NotNull(persistedDuringDelivery);
            Assert.Equal(BalanceVisualState.Warning15, persistedDuringDelivery!.LastBalanceBand);
            Assert.Equal(BalanceVisualState.Warning15, persistedDuringDelivery.LastVisualState);
            Assert.Equal("Production", alert!.AccountNote);
            Assert.Equal("••••-key", alert!.MaskedKeySuffix);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    [Fact]
    public void Island_uses_the_persisted_evaluator_visual_state_and_exact_warning_boundary()
    {
        var account = new Account { Id = "account", Provider = Provider.DeepSeek, AlertEnabled = true, WarningLine = 20 };
        using var coordinator = TestFactory.CreateCoordinator(new AppState
        {
            Accounts = [account],
            Snapshots = new Dictionary<string, BalanceSnapshot>
            {
                [account.Id] = new()
                {
                    CredentialId = account.Id,
                    Provider = account.Provider,
                    BalanceAmount = 24,
                    CurrencyCode = "USD",
                    Status = SnapshotStatus.Warning
                }
            },
            Alerts = new Dictionary<string, BalanceAlertState>
            {
                [account.Id] = new() { LastVisualState = BalanceVisualState.Anomaly }
            }
        });

        var item = Assert.Single(IslandAccountSelection.VisibleItems(coordinator));

        Assert.Equal(BalanceVisualState.Anomaly, item.VisualState);
        Assert.Equal(SnapshotStatus.Ok, Assert.Single(coordinator.CurrentSnapshots).Status);
    }

    [Fact]
    public void Aggregate_island_uses_the_highest_persisted_evaluator_visual_state()
    {
        var snapshots = new Dictionary<string, BalanceSnapshot>
        {
            ["warning"] = new() { CredentialId = "warning", Provider = Provider.DeepSeek, Status = SnapshotStatus.Ok, BalanceAmount = 100 },
            ["critical"] = new() { CredentialId = "critical", Provider = Provider.DeepSeek, Status = SnapshotStatus.Ok, BalanceAmount = 10 }
        };
        var visualStates = new Dictionary<string, BalanceVisualState>
        {
            ["warning"] = BalanceVisualState.Anomaly,
            ["critical"] = BalanceVisualState.Critical
        };

        var item = IslandDisplayGroups.Aggregate(new IslandDisplayGroup
        {
            Name = "Team",
            Mode = IslandGroupMode.Aggregate,
            AggregateProvider = Provider.DeepSeek,
            AccountIds = ["warning", "critical"]
        }, snapshots, visualStates);

        Assert.Equal(BalanceVisualState.Critical, item.VisualState);
    }

    [Theory]
    [InlineData(SnapshotStatus.Error)]
    [InlineData(SnapshotStatus.NotConfigured)]
    public void Invalid_snapshot_projects_normal_instead_of_a_stale_evaluator_state(SnapshotStatus status)
    {
        var account = new Account { Id = "account", Provider = Provider.DeepSeek };
        using var coordinator = TestFactory.CreateCoordinator(new AppState
        {
            Accounts = [account],
            Snapshots = new Dictionary<string, BalanceSnapshot>
            {
                [account.Id] = new()
                {
                    CredentialId = account.Id,
                    Provider = account.Provider,
                    Status = status,
                    PrimaryText = status == SnapshotStatus.Error ? "查询失败" : "等待刷新"
                }
            },
            Alerts = new Dictionary<string, BalanceAlertState>
            {
                [account.Id] = new() { LastVisualState = BalanceVisualState.Critical }
            }
        });

        var item = Assert.Single(IslandAccountSelection.VisibleItems(coordinator));

        Assert.Equal(BalanceVisualState.Normal, item.VisualState);
    }

    [Fact]
    public void Snapshot_without_a_balance_projects_normal_instead_of_a_stale_evaluator_state()
    {
        var account = new Account { Id = "account", Provider = Provider.DeepSeek };
        using var coordinator = TestFactory.CreateCoordinator(new AppState
        {
            Accounts = [account],
            Snapshots = new Dictionary<string, BalanceSnapshot>
            {
                [account.Id] = new()
                {
                    CredentialId = account.Id,
                    Provider = account.Provider,
                    Status = SnapshotStatus.Ok,
                    PrimaryText = "额度不可用"
                }
            },
            Alerts = new Dictionary<string, BalanceAlertState>
            {
                [account.Id] = new() { LastVisualState = BalanceVisualState.Anomaly }
            }
        });

        var item = Assert.Single(IslandAccountSelection.VisibleItems(coordinator));

        Assert.Equal(BalanceVisualState.Normal, item.VisualState);
    }

    [Fact]
    public void Aggregate_ignores_stale_critical_state_for_an_invalid_member()
    {
        var snapshots = new Dictionary<string, BalanceSnapshot>
        {
            ["valid"] = new()
            {
                CredentialId = "valid",
                Provider = Provider.DeepSeek,
                BalanceAmount = 30,
                Status = SnapshotStatus.Ok
            },
            ["failed"] = new()
            {
                CredentialId = "failed",
                Provider = Provider.DeepSeek,
                Status = SnapshotStatus.Error,
                PrimaryText = "查询失败"
            }
        };
        var visualStates = new Dictionary<string, BalanceVisualState>
        {
            ["valid"] = BalanceVisualState.Warning15,
            ["failed"] = BalanceVisualState.Critical
        };

        var item = IslandDisplayGroups.Aggregate(new IslandDisplayGroup
        {
            Name = "Team",
            Mode = IslandGroupMode.Aggregate,
            AggregateProvider = Provider.DeepSeek,
            AccountIds = ["valid", "failed"]
        }, snapshots, visualStates);

        Assert.Equal(BalanceVisualState.Warning15, item.VisualState);
    }

    private sealed class FixedBalanceClient(double balance) : ProviderClient
    {
        public override Task<BalanceSnapshot> FetchAsync(ApiCredential credential, CancellationToken token) =>
            Task.FromResult(new BalanceSnapshot
            {
                Provider = credential.Account.Provider,
                CredentialId = credential.Account.Id,
                AccountLabel = credential.Account.Label,
                KeySuffix = credential.Account.KeySuffix,
                BalanceAmount = balance,
                CurrencyCode = "USD",
                PrimaryText = $"${balance:0.00}",
                Status = SnapshotStatus.Ok,
                UpdatedAt = DateTimeOffset.UtcNow
            });
    }
}
