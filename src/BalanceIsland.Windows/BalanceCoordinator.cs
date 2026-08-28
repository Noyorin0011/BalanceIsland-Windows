using System.Net.Http;

namespace BalanceIsland.Windows;

public sealed class BalanceCoordinator : IDisposable
{
    private static readonly HashSet<Provider> LocallyTrackedProviders =
        [Provider.DeepSeek, Provider.Moonshot, Provider.SiliconFlow];

    private readonly AppDataStore _store;
    private readonly WindowsCredentialStore _credentials;
    private readonly ProviderClient _client;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly System.Threading.Timer _timer;

    public AppState State { get; }
    public bool IsExiting { get; set; }
    public event EventHandler? StateChanged;
    public event EventHandler<BalanceAlertEventArgs>? AlertRaised;

    public BalanceCoordinator(
        AppDataStore store,
        WindowsCredentialStore credentials,
        ProviderClient client)
    {
        _store = store;
        _credentials = credentials;
        _client = client;
        State = store.Load();
        if (State.IslandLayoutVersion < 1)
        {
            State.IslandPositionPreset = IslandPositionPreset.Left;
            State.IslandSizePreset = IslandSizePreset.Standard;
            State.IslandWidth = 225;
            State.IslandHeight = 38;
            State.IslandEditMode = false;
            State.IslandLayoutVersion = 1;
        }
        else if (State.IslandEditMode)
        {
            // Editing is session-only so the overlay always starts locked and click-through.
            State.IslandEditMode = false;
        }
        if (State.EnvironmentAutoImportEnabled)
            ImportEnvironmentAccounts(saveAndNotify: false);
        _store.Save(State);
        _timer = new System.Threading.Timer(async _ => await RefreshFromTimerAsync(), null,
            TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
    }

    public IReadOnlyList<BalanceSnapshot> CurrentSnapshots => State.Accounts
        .Select(account => State.Snapshots.TryGetValue(account.Id, out var snapshot)
            ? ApplyUserSettings(account, snapshot)
            : Waiting(account))
        .ToArray();

    public async Task AddAccountAsync(
        Provider provider,
        string label,
        string rawApiKey,
        double? manualBalance,
        int refreshIntervalMinutes)
    {
        var key = ApiKeySanitizer.Clean(rawApiKey);
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("请输入 API Key");
        var account = new Account
        {
            Provider = provider,
            Label = label.Trim(),
            KeySuffix = key.Length <= 4 ? key : key[^4..],
            ManualBalance = manualBalance,
            RefreshIntervalMinutes = refreshIntervalMinutes == 0
                ? 0 : Math.Clamp(refreshIntervalMinutes, 1, 1440),
            CredentialSource = CredentialSource.WindowsCredentialManager
        };

        _credentials.Write(account.Id, key);
        State.Accounts.Add(account);
        State.Snapshots[account.Id] = Waiting(account);
        SaveAndNotify();
        await RefreshDueAsync(force: true, targetCredentialId: account.Id);
    }

    public void RemoveAccount(string credentialId)
    {
        var account = State.Accounts.FirstOrDefault(item => item.Id == credentialId);
        State.Accounts.RemoveAll(item => item.Id == credentialId);
        State.Snapshots.Remove(credentialId);
        State.Schedules.Remove(credentialId);
        State.DailyUsage.Remove(credentialId);
        State.Alerts.Remove(credentialId);
        if (account?.CredentialSource == CredentialSource.WindowsCredentialManager)
            _credentials.Delete(credentialId);
        SaveAndNotify();
    }

    public void SetIslandEnabled(bool enabled)
    {
        State.IslandEnabled = enabled;
        SaveAndNotify();
    }

    public void SetIslandDisplayMode(IslandDisplayMode mode)
    {
        if (State.IslandDisplayMode == mode) return;
        State.IslandDisplayMode = mode;
        SaveAndNotify();
    }

    public void SetIslandEditMode(bool enabled)
    {
        if (State.IslandEditMode == enabled) return;
        State.IslandEditMode = enabled;
        SaveAndNotify();
    }

    public void SetIslandPositionPreset(IslandPositionPreset preset)
    {
        if (State.IslandPositionPreset == preset) return;
        State.IslandPositionPreset = preset;
        SaveAndNotify();
    }

    public void SetIslandSizePreset(IslandSizePreset preset)
    {
        if (preset == IslandSizePreset.Custom) return;
        var size = preset switch
        {
            IslandSizePreset.Compact => (Width: 190d, Height: 32d),
            IslandSizePreset.Large => (Width: 285d, Height: 48d),
            _ => (Width: 225d, Height: 38d)
        };
        State.IslandSizePreset = preset;
        State.IslandWidth = size.Width;
        State.IslandHeight = size.Height;
        SaveAndNotify();
    }

    public void SetIslandSize(double width, double height)
    {
        State.IslandSizePreset = IslandSizePreset.Custom;
        State.IslandWidth = Math.Clamp(width, 160, 480);
        State.IslandHeight = Math.Clamp(height, 28, 100);
        SaveAndNotify();
    }

    public void SaveIslandCustomLayout(double leftDip, double topDip, double widthDip, double heightDip)
    {
        State.IslandPositionPreset = IslandPositionPreset.Custom;
        State.IslandSizePreset = IslandSizePreset.Custom;
        State.IslandCustomLeftDip = leftDip;
        State.IslandCustomTopDip = topDip;
        State.IslandWidth = Math.Clamp(widthDip, 160, 480);
        State.IslandHeight = Math.Clamp(heightDip, 28, 100);
        SaveAndNotify();
    }

    public void SaveIslandEditBounds(double left, double top, double width, double height)
    {
        State.IslandEditLeft = left;
        State.IslandEditTop = top;
        State.IslandWidth = Math.Clamp(width, 160, 480);
        State.IslandHeight = Math.Clamp(height, 28, 100);
        _store.Save(State);
    }

    public void SetEnvironmentAutoImport(bool enabled)
    {
        State.EnvironmentAutoImportEnabled = enabled;
        if (enabled) ImportEnvironmentAccounts(saveAndNotify: false);
        SaveAndNotify();
    }

    public EnvironmentImportResult ImportEnvironmentAccounts() => ImportEnvironmentAccounts(true);

    private EnvironmentImportResult ImportEnvironmentAccounts(bool saveAndNotify)
    {
        var found = EnvironmentCredentialDiscovery.Scan();
        var added = new List<string>();
        var refreshed = new List<string>();
        foreach (var candidate in found)
        {
            var envAccount = State.Accounts.FirstOrDefault(account =>
                account.CredentialSource == CredentialSource.EnvironmentVariable &&
                account.Provider == candidate.Provider &&
                string.Equals(account.EnvironmentVariableName, candidate.VariableName, StringComparison.OrdinalIgnoreCase));
            if (envAccount is not null)
            {
                var suffix = candidate.ApiKey.Length <= 4 ? candidate.ApiKey : candidate.ApiKey[^4..];
                if (envAccount.KeySuffix != suffix)
                {
                    envAccount.KeySuffix = suffix;
                    refreshed.Add(candidate.Provider.DisplayName());
                }
                continue;
            }

            var duplicateExplicit = State.Accounts.Any(account =>
                account.Provider == candidate.Provider &&
                account.CredentialSource == CredentialSource.WindowsCredentialManager &&
                string.Equals(_credentials.Read(account.Id), candidate.ApiKey, StringComparison.Ordinal));
            if (duplicateExplicit) continue;

            var account = new Account
            {
                Provider = candidate.Provider,
                Label = $"环境变量：{candidate.VariableName}",
                KeySuffix = candidate.ApiKey.Length <= 4 ? candidate.ApiKey : candidate.ApiKey[^4..],
                CredentialSource = CredentialSource.EnvironmentVariable,
                EnvironmentVariableName = candidate.VariableName
            };
            State.Accounts.Add(account);
            State.Snapshots[account.Id] = Waiting(account);
            added.Add(candidate.Provider.DisplayName());
        }

        if (saveAndNotify && (added.Count > 0 || refreshed.Count > 0)) SaveAndNotify();
        return new EnvironmentImportResult(found.Count, added, refreshed);
    }

    public void UpdateAlertSettings(
        string credentialId,
        bool enabled,
        double warningLine,
        double dropStep,
        bool anomalyEnabled,
        double anomalyThreshold,
        double anomalyPercentThreshold,
        AnomalyMode anomalyMode,
        int cooldownMinutes)
    {
        var account = State.Accounts.FirstOrDefault(item => item.Id == credentialId)
            ?? throw new ArgumentException("账户不存在");
        account.AlertEnabled = enabled;
        account.WarningLine = Math.Max(0, warningLine);
        account.DropStep = Math.Max(0.01, dropStep);
        account.AnomalyEnabled = anomalyEnabled;
        account.AnomalyThreshold = Math.Max(0.01, anomalyThreshold);
        account.AnomalyPercentThreshold = Math.Max(0.01, anomalyPercentThreshold);
        account.AnomalyMode = anomalyMode;
        account.AnomalyCooldownMinutes = Math.Clamp(cooldownMinutes, 1, 10080);
        State.Alerts.Remove(account.Id);
        SaveAndNotify();
    }

    public async Task RefreshDueAsync(bool force, string? targetCredentialId = null)
    {
        if (!await _refreshLock.WaitAsync(0)) return;
        try
        {
            foreach (var account in State.Accounts.ToArray())
            {
                if (targetCredentialId is not null && account.Id != targetCredentialId) continue;
                var schedule = State.Schedules.TryGetValue(account.Id, out var stored)
                    ? stored : State.Schedules[account.Id] = new ScheduleState();
                if (!ShouldAttempt(schedule, account.EffectiveRefreshMinutes, force)) continue;

                var key = ResolveApiKey(account);
                if (string.IsNullOrWhiteSpace(key))
                {
                    var source = account.CredentialSource == CredentialSource.EnvironmentVariable
                        ? $"环境变量 {account.EnvironmentVariableName} 当前不存在或为空"
                        : "Windows 凭据管理器中没有该 API Key";
                    State.Snapshots[account.Id] = Error(account, source);
                    SaveAndNotify();
                    continue;
                }

                schedule.LastAttempt = DateTimeOffset.Now;
                try
                {
                    var raw = await _client.FetchAsync(new ApiCredential(account, key), CancellationToken.None);
                    raw = AttachDailyUsage(raw);
                    State.Snapshots[account.Id] = raw;
                    schedule.NextScheduled = DateTimeOffset.Now.AddMinutes(account.EffectiveRefreshMinutes);
                    schedule.RateLimitUntil = null;
                    schedule.BackoffLevel = 0;
                    EvaluateAlerts(account, ApplyUserSettings(account, raw));
                }
                catch (ProviderApiException exception) when (exception.StatusCode == 429)
                {
                    schedule.BackoffLevel = Math.Min(7, schedule.BackoffLevel + 1);
                    var exponential = TimeSpan.FromMinutes(
                        account.EffectiveRefreshMinutes * Math.Pow(2, schedule.BackoffLevel));
                    var delay = new[] { TimeSpan.FromMinutes(1), exception.RetryAfter ?? TimeSpan.Zero, exponential }
                        .Max();
                    if (delay > TimeSpan.FromHours(24)) delay = TimeSpan.FromHours(24);
                    schedule.RateLimitUntil = DateTimeOffset.Now.Add(delay);
                    schedule.NextScheduled = schedule.RateLimitUntil;
                    State.Snapshots[account.Id] = RateLimited(account, delay);
                }
                catch (Exception exception)
                {
                    schedule.NextScheduled = DateTimeOffset.Now.AddMinutes(account.EffectiveRefreshMinutes);
                    State.Snapshots[account.Id] = Error(account, ReadableError(exception));
                }
                finally
                {
                    SaveAndNotify();
                }
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private string? ResolveApiKey(Account account) => account.CredentialSource switch
    {
        CredentialSource.EnvironmentVariable => EnvironmentCredentialDiscovery.Read(account.EnvironmentVariableName),
        _ => _credentials.Read(account.Id)
    };

    private async Task RefreshFromTimerAsync()
    {
        try
        {
            if (State.EnvironmentAutoImportEnabled) ImportEnvironmentAccounts(saveAndNotify: true);
            await RefreshDueAsync(force: false);
        }
        catch
        {
            // A background persistence failure must not terminate the tray process.
        }
    }

    private static bool ShouldAttempt(ScheduleState state, int intervalMinutes, bool force)
    {
        var now = DateTimeOffset.Now;
        if (state.RateLimitUntil is { } retryAt && retryAt > now) return false;
        if (force)
            return state.LastAttempt is null || now - state.LastAttempt >= TimeSpan.FromSeconds(30);
        return state.NextScheduled is null || now >= state.NextScheduled;
    }

    private BalanceSnapshot RateLimited(Account account, TimeSpan delay)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(delay.TotalMinutes));
        if (State.Snapshots.TryGetValue(account.Id, out var cached) && cached.UpdatedAt != default)
        {
            var copy = Copy(cached);
            copy.SecondaryText = $"请求受限，约 {minutes} 分钟后自动重试";
            if (copy.Status != SnapshotStatus.Critical) copy.Status = SnapshotStatus.Warning;
            return copy;
        }
        return new BalanceSnapshot
        {
            Provider = account.Provider,
            CredentialId = account.Id,
            AccountLabel = account.Label,
            KeySuffix = account.KeySuffix,
            PrimaryText = "查询已推迟",
            SecondaryText = $"请求受限，约 {minutes} 分钟后自动重试",
            CurrencyCode = account.Provider.DefaultCurrency(),
            Status = SnapshotStatus.Warning,
            UpdatedAt = DateTimeOffset.Now
        };
    }

    private BalanceSnapshot AttachDailyUsage(BalanceSnapshot snapshot)
    {
        if (snapshot.Status == SnapshotStatus.Error || snapshot.BalanceAmount is null ||
            !LocallyTrackedProviders.Contains(snapshot.Provider)) return snapshot;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var current = snapshot.BalanceAmount.Value;
        State.DailyUsage.TryGetValue(snapshot.CredentialId, out var previous);
        var sameDay = previous?.Date == today;
        var mayCarryAcrossMidnight = previous?.Date == today.AddDays(-1);
        var opening = sameDay
            ? previous!.OpeningBalance
            : mayCarryAcrossMidnight ? previous!.LastBalance : current;
        var topUpsBefore = sameDay ? previous!.ObservedTopUps : 0;
        var observedTopUp = (sameDay || mayCarryAcrossMidnight) && previous is not null
            ? Math.Max(0, current - previous.LastBalance) : 0;
        var observedTopUps = topUpsBefore + observedTopUp;
        var used = Math.Max(0, opening + observedTopUps - current);
        State.DailyUsage[snapshot.CredentialId] = new DailyUsageState
        {
            Date = today,
            OpeningBalance = opening,
            LastBalance = current,
            ObservedTopUps = observedTopUps,
            UsedToday = used
        };
        snapshot.TodayUsedAmount = used;
        snapshot.TodayUsageIsEstimated = true;
        return snapshot;
    }

    private static BalanceSnapshot ApplyUserSettings(Account account, BalanceSnapshot raw)
    {
        var copy = Copy(raw);
        var amount = account.ManualBalance ?? raw.BalanceAmount;
        if (account.AlertEnabled && amount is not null)
        {
            copy.Status = amount <= account.WarningLine
                ? SnapshotStatus.Critical
                : amount <= account.WarningLine * 1.5
                    ? SnapshotStatus.Warning : raw.Status;
        }
        if (account.ManualBalance is null) return copy;

        copy.PrimaryText = $"{BalanceSnapshot.CurrencySymbol(raw.CurrencyCode)}{account.ManualBalance:0.00}";
        copy.SecondaryText = $"手动余额 · 接口状态：{raw.PrimaryText}";
        copy.BalanceAmount = account.ManualBalance;
        copy.IsManualBalance = true;
        copy.TodayUsedAmount = null;
        copy.TodayUsageIsEstimated = false;
        return copy;
    }

    private void EvaluateAlerts(Account account, BalanceSnapshot snapshot)
    {
        if (!account.AlertEnabled || snapshot.BalanceAmount is not { } amount ||
            snapshot.Status == SnapshotStatus.Error) return;
        var previous = State.Alerts.TryGetValue(account.Id, out var alert)
            ? alert : new BalanceAlertState();
        var level = amount <= account.WarningLine ? 2 : amount <= account.WarningLine * 1.5 ? 1 : 0;
        var reasons = new List<string>();
        if (level > previous.LastLevel)
            reasons.Add(level == 2 ? $"低于警告线 {account.WarningLine:0.00}" : "余额接近警告线");
        if (previous.LastNotifiedAmount is { } reference && amount <= reference - account.DropStep)
            reasons.Add($"余额下降 {reference - amount:0.00}");

        var now = DateTimeOffset.Now;
        var anomaly = false;
        if (account.AnomalyEnabled && previous.LastSeenAmount is { } last && amount != last)
        {
            var change = Math.Abs(amount - last);
            var absolute = change >= account.AnomalyThreshold;
            var percent = last > 0
                ? change >= last * account.AnomalyPercentThreshold / 100d : absolute;
            var over = account.AnomalyMode switch
            {
                AnomalyMode.Absolute => absolute,
                AnomalyMode.Percent => percent,
                _ => absolute || percent
            };
            var coolingDown = previous.LastAnomalyAt is { } lastAt &&
                now - lastAt < TimeSpan.FromMinutes(account.AnomalyCooldownMinutes);
            anomaly = over && !coolingDown;
            if (anomaly)
            {
                AlertRaised?.Invoke(this, new BalanceAlertEventArgs(
                    $"{account.Provider.DisplayName()} {account.DisplayLabel} 异常变动",
                    $"{BalanceSnapshot.CurrencySymbol(snapshot.CurrencyCode)}{last:0.00} → " +
                    $"{BalanceSnapshot.CurrencySymbol(snapshot.CurrencyCode)}{amount:0.00}"));
            }
        }

        var shouldNotify = reasons.Count > 0;
        var resetReference = previous.LastNotifiedAmount is null || amount > previous.LastNotifiedAmount;
        previous.LastNotifiedAmount = shouldNotify || resetReference
            ? amount : previous.LastNotifiedAmount;
        previous.LastLevel = level;
        previous.LastSeenAmount = amount;
        if (anomaly) previous.LastAnomalyAt = now;
        State.Alerts[account.Id] = previous;
        if (shouldNotify)
            AlertRaised?.Invoke(this, new BalanceAlertEventArgs(
                $"{account.Provider.DisplayName()} {account.DisplayLabel}",
                $"{snapshot.PrimaryText} · {string.Join("；", reasons)}"));
    }

    private static string ReadableError(Exception exception) => exception switch
    {
        TaskCanceledException => "网络请求超时",
        HttpRequestException => "网络连接失败",
        ProviderApiException api => api.Message,
        _ => exception.Message.Length > 120 ? exception.Message[..120] : exception.Message
    };

    private static BalanceSnapshot Waiting(Account account) => new()
    {
        Provider = account.Provider,
        CredentialId = account.Id,
        AccountLabel = account.Label,
        KeySuffix = account.KeySuffix,
        PrimaryText = "等待刷新",
        SecondaryText = "点击立即刷新",
        CurrencyCode = account.Provider.DefaultCurrency(),
        Status = SnapshotStatus.NotConfigured
    };

    private static BalanceSnapshot Error(Account account, string message) => new()
    {
        Provider = account.Provider,
        CredentialId = account.Id,
        AccountLabel = account.Label,
        KeySuffix = account.KeySuffix,
        PrimaryText = "查询失败",
        SecondaryText = message,
        CurrencyCode = account.Provider.DefaultCurrency(),
        Status = SnapshotStatus.Error,
        UpdatedAt = DateTimeOffset.Now
    };

    private static BalanceSnapshot Copy(BalanceSnapshot source) => new()
    {
        Provider = source.Provider,
        CredentialId = source.CredentialId,
        AccountLabel = source.AccountLabel,
        KeySuffix = source.KeySuffix,
        PrimaryText = source.PrimaryText,
        SecondaryText = source.SecondaryText,
        BalanceAmount = source.BalanceAmount,
        CurrencyCode = source.CurrencyCode,
        IsManualBalance = source.IsManualBalance,
        Status = source.Status,
        UpdatedAt = source.UpdatedAt,
        TodayUsedAmount = source.TodayUsedAmount,
        TodayUsageIsEstimated = source.TodayUsageIsEstimated
    };

    private void SaveAndNotify()
    {
        _store.Save(State);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _timer.Dispose();
        _refreshLock.Dispose();
    }
}

public sealed class BalanceAlertEventArgs(string title, string message) : EventArgs
{
    public string Title { get; } = title;
    public string Message { get; } = message;
}

public sealed record EnvironmentImportResult(
    int FoundCount,
    IReadOnlyList<string> AddedProviders,
    IReadOnlyList<string> RefreshedProviders);
