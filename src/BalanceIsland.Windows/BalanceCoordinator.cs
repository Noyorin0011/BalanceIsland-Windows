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
        var loadResult = store.LoadResult();
        State = loadResult.State;
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
        if (loadResult.LoadedFromDisk || loadResult.Error is null)
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
            KeySuffix = ApiKeySanitizer.SafeKeySuffix(key),
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
        IslandDisplayGroups.RemoveAccount(State, credentialId);
        State.Accounts.RemoveAll(item => item.Id == credentialId);
        State.Snapshots.Remove(credentialId);
        State.Schedules.Remove(credentialId);
        State.DailyUsage.Remove(credentialId);
        State.Alerts.Remove(credentialId);
        if (account?.CredentialSource == CredentialSource.WindowsCredentialManager)
            _credentials.Delete(credentialId);
        SaveAndNotify();
    }

    public void SetAccountEnabled(string credentialId, bool enabled)
    {
        var account = State.Accounts.FirstOrDefault(item => item.Id == credentialId)
            ?? throw new ArgumentException("账户不存在", nameof(credentialId));
        if (account.IsEnabled == enabled) return;
        account.IsEnabled = enabled;
        if (enabled) State.Schedules.Remove(account.Id);
        SaveAndNotify();
    }

    public void SetAccountShowInIsland(string credentialId, bool showInIsland)
    {
        var account = State.Accounts.FirstOrDefault(item => item.Id == credentialId)
            ?? throw new ArgumentException("账户不存在", nameof(credentialId));
        if (account.ShowInIsland == showInIsland) return;
        account.ShowInIsland = showInIsland;
        SaveAndNotify();
    }

    public async Task UpdateAccountAsync(
        string credentialId,
        string label,
        string rawApiKey,
        double? manualBalance,
        int refreshIntervalMinutes)
    {
        var account = State.Accounts.FirstOrDefault(item => item.Id == credentialId)
            ?? throw new ArgumentException("账户不存在", nameof(credentialId));
        if (refreshIntervalMinutes is < 0 or > 1440)
            throw new ArgumentOutOfRangeException(nameof(refreshIntervalMinutes));

        var key = ApiKeySanitizer.Clean(rawApiKey);
        if (!string.IsNullOrWhiteSpace(key))
        {
            _credentials.Write(account.Id, key);
            account.KeySuffix = ApiKeySanitizer.SafeKeySuffix(key);
            account.CredentialSource = CredentialSource.WindowsCredentialManager;
            account.EnvironmentVariableName = null;
        }

        account.Label = label.Trim();
        account.ManualBalance = manualBalance;
        account.RefreshIntervalMinutes = refreshIntervalMinutes;
        State.Schedules.Remove(account.Id);
        State.Snapshots[account.Id] = Waiting(account);
        SaveAndNotify();
        if (account.IsEnabled) await RefreshDueAsync(force: true, targetCredentialId: account.Id);
    }

    public void SetIslandEnabled(bool enabled)
    {
        State.IslandEnabled = enabled;
        SaveAndNotify();
    }

    public void SetThemeMode(AppThemeMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (State.ThemeMode == mode) return;
        State.ThemeMode = mode;
        SaveAndNotify();
    }

    public void SetIslandColorTheme(IslandColorTheme theme)
    {
        if (!Enum.IsDefined(theme)) throw new ArgumentOutOfRangeException(nameof(theme));
        if (State.IslandColorTheme == theme) return;
        State.IslandColorTheme = theme;
        SaveAndNotify();
    }

    public void SetCustomIslandColors(string normal, string anomaly, string warning15, string critical)
    {
        var normalizedNormal = NormalizeIslandColor(normal, nameof(normal));
        var normalizedAnomaly = NormalizeIslandColor(anomaly, nameof(anomaly));
        var normalizedWarning15 = NormalizeIslandColor(warning15, nameof(warning15));
        var normalizedCritical = NormalizeIslandColor(critical, nameof(critical));

        State.CustomNormalColor = normalizedNormal;
        State.CustomAnomalyColor = normalizedAnomaly;
        State.CustomWarning15Color = normalizedWarning15;
        State.CustomCriticalColor = normalizedCritical;
        SaveAndNotify();
    }

    public IslandDisplayGroup CreateDisplayGroup(
        string name,
        IslandGroupMode mode,
        IEnumerable<string> accountIds)
    {
        var group = IslandDisplayGroups.Create(State, name, mode, accountIds);
        SaveAndNotify();
        return group;
    }

    public IslandDisplayGroup UpdateDisplayGroup(
        string groupId,
        string name,
        IslandGroupMode mode,
        IEnumerable<string> accountIds)
    {
        var group = IslandDisplayGroups.Update(State, groupId, name, mode, accountIds);
        SaveAndNotify();
        return group;
    }

    public void DeleteDisplayGroup(string groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId) ||
            State.DisplayGroups.All(group => group.Id != groupId))
            throw new ArgumentException("要删除的分组不存在", nameof(groupId));

        IslandDisplayGroups.Delete(State, groupId);
        SaveAndNotify();
    }

    public void SetActiveDisplayGroup(string? groupId)
    {
        IslandDisplayGroups.SetActive(State, groupId);
        SaveAndNotify();
    }

    public void SetNotificationSettings(bool warning15, bool critical, bool anomaly)
    {
        State.NotifyWarning15 = warning15;
        State.NotifyCritical = critical;
        State.NotifyAnomaly = anomaly;
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

    public void SetSilentStartup(bool enabled)
    {
        if (State.SilentStartupEnabled == enabled) return;
        State.SilentStartupEnabled = enabled;
        SaveAndNotify();
    }

    public void SetEnvironmentAutoImport(bool enabled)
    {
        if (State.EnvironmentAutoImportEnabled == enabled) return;
        State.EnvironmentAutoImportEnabled = enabled;
        SaveAndNotify();
    }

    public IReadOnlyList<EnvironmentCredentialCandidate> FindNewEnvironmentCandidates(
        IEnumerable<EnvironmentCredentialCandidate> candidates)
    {
        return EnvironmentImportPlanner.FindNew(
            candidates,
            State.Accounts,
            account => account.CredentialSource == CredentialSource.EnvironmentVariable
                ? EnvironmentCredentialDiscovery.Read(account.EnvironmentVariableName)
                : _credentials.Read(account.Id));
    }

    public EnvironmentImportResult ImportEnvironmentAccounts(
        IEnumerable<EnvironmentCredentialCandidate> selectedCandidates)
    {
        ArgumentNullException.ThrowIfNull(selectedCandidates);

        var selected = selectedCandidates.ToArray();
        if (selected.Any(candidate => candidate.Provider is null))
            throw new ArgumentException("未分类的环境凭据必须先明确选择 Provider。", nameof(selectedCandidates));
        var added = new List<string>();
        var addedCredentialIds = new List<string>();
        var suffixChanged = false;
        var importedVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in selected)
        {
            var provider = candidate.Provider
                ?? throw new ArgumentException("未分类的环境凭据必须先明确选择 Provider。", nameof(selectedCandidates));
            if (!importedVariables.Add($"{provider}\0{candidate.VariableName}")) continue;

            var envAccount = State.Accounts.FirstOrDefault(account =>
                account.CredentialSource == CredentialSource.EnvironmentVariable &&
                account.Provider == provider &&
                string.Equals(account.EnvironmentVariableName, candidate.VariableName, StringComparison.OrdinalIgnoreCase));
            if (envAccount is not null)
            {
                suffixChanged |= UpdateSafeKeySuffix(envAccount, candidate.ApiKey);
                continue;
            }

            var duplicateExplicit = State.Accounts.Any(account =>
                account.Provider == provider &&
                account.CredentialSource == CredentialSource.WindowsCredentialManager &&
                string.Equals(_credentials.Read(account.Id), candidate.ApiKey, StringComparison.Ordinal));
            if (duplicateExplicit) continue;

            var account = new Account
            {
                Provider = provider,
                Label = $"环境变量：{candidate.VariableName}",
                KeySuffix = ApiKeySanitizer.SafeKeySuffix(candidate.ApiKey),
                CredentialSource = CredentialSource.EnvironmentVariable,
                EnvironmentVariableName = candidate.VariableName
            };
            State.Accounts.Add(account);
            State.Snapshots[account.Id] = Waiting(account);
            added.Add(provider.DisplayName());
            if (account.IsEnabled) addedCredentialIds.Add(account.Id);
        }

        if (added.Count > 0 || suffixChanged)
        {
            SaveAndNotify();
            if (addedCredentialIds.Count > 0)
                _ = RefreshDueAsync(force: true,
                    new HashSet<string>(addedCredentialIds, StringComparer.Ordinal), waitForLock: true);
        }
        return new EnvironmentImportResult(selected.Length, added, []);
    }

    public void UpdateAlertSettings(
        string credentialId,
        bool enabled,
        double warningLine,
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
        account.AnomalyEnabled = anomalyEnabled;
        account.AnomalyThreshold = Math.Max(0.01, anomalyThreshold);
        account.AnomalyPercentThreshold = Math.Max(0.01, anomalyPercentThreshold);
        account.AnomalyMode = anomalyMode;
        account.AnomalyCooldownMinutes = Math.Clamp(cooldownMinutes, 1, 10080);
        State.Alerts.Remove(account.Id);
        SaveAndNotify();
    }

    public Task RefreshDueAsync(bool force, string? targetCredentialId = null) =>
        RefreshDueAsync(force, targetCredentialId is null
            ? null
            : new HashSet<string>([targetCredentialId], StringComparer.Ordinal), waitForLock: false);

    private async Task RefreshDueAsync(
        bool force,
        IReadOnlySet<string>? targetCredentialIds,
        bool waitForLock)
    {
        if (waitForLock)
            await _refreshLock.WaitAsync();
        else if (!await _refreshLock.WaitAsync(0))
            return;
        try
        {
            foreach (var account in State.Accounts.ToArray())
            {
                if (targetCredentialIds is not null && !targetCredentialIds.Contains(account.Id)) continue;
                if (!account.IsEnabled) continue;
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
                    State.Snapshots[account.Id] = Error(account, ReadableError(exception, key));
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

    private string? ResolveApiKey(Account account)
    {
        var key = account.CredentialSource switch
        {
            CredentialSource.EnvironmentVariable => EnvironmentCredentialDiscovery.Read(account.EnvironmentVariableName),
            _ => _credentials.Read(account.Id)
        };
        UpdateSafeKeySuffix(account, key);
        return key;
    }

    private bool UpdateSafeKeySuffix(Account account, string? secret)
    {
        var suffix = ApiKeySanitizer.SafeKeySuffix(secret);
        var changed = !string.Equals(account.KeySuffix, suffix, StringComparison.Ordinal);
        account.KeySuffix = suffix;
        if (State.Snapshots.TryGetValue(account.Id, out var snapshot) && snapshot is not null)
        {
            changed |= !string.Equals(snapshot.KeySuffix, suffix, StringComparison.Ordinal);
            snapshot.KeySuffix = suffix;
        }
        return changed;
    }

    private async Task RefreshFromTimerAsync()
    {
        try
        {
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
        if (account.AlertEnabled && amount is not null && raw.Status != SnapshotStatus.Error)
        {
            copy.Status = amount <= account.WarningLine
                ? SnapshotStatus.Critical
                : amount <= account.WarningLine * 1.15d
                    ? SnapshotStatus.Warning : SnapshotStatus.Ok;
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
        var previous = State.Alerts.TryGetValue(account.Id, out var alert)
            ? alert : new BalanceAlertState();
        var evaluation = BalanceStateEvaluator.Evaluate(account, snapshot, previous, DateTimeOffset.Now);
        State.Alerts[account.Id] = evaluation.NextState;
        var deliverableAlerts = evaluation.EnteredAlerts
            .Where(IsNotificationEnabled)
            .Select(kind => CreateAlertEvent(account, snapshot, previous, kind))
            .ToArray();

        if (deliverableAlerts.Length > 0)
        {
            // Delivery is at-most-once per persisted transition: the application attempts
            // native Toast delivery and, on failure, one tray fallback without reopening it.
            _store.Save(State);
            foreach (var alertEvent in deliverableAlerts)
                AlertRaised?.Invoke(this, alertEvent);
        }
    }

    private bool IsNotificationEnabled(BalanceAlertKind kind) => kind switch
    {
        BalanceAlertKind.Warning15 => State.NotifyWarning15,
        BalanceAlertKind.Critical => State.NotifyCritical,
        BalanceAlertKind.Anomaly => State.NotifyAnomaly,
        _ => false
    };

    private static BalanceAlertEventArgs CreateAlertEvent(
        Account account,
        BalanceSnapshot snapshot,
        BalanceAlertState previous,
        BalanceAlertKind kind)
    {
        var provider = account.Provider.DisplayName();
        var title = $"{provider} · {AlertTitle(kind)}";
        var current = snapshot.PrimaryText;
        var message = kind == BalanceAlertKind.Anomaly && previous.LastSeenAmount is { } last
            ? $"{BalanceSnapshot.CurrencySymbol(snapshot.CurrencyCode)}{last:0.00} → {current} · 异常变动"
            : kind == BalanceAlertKind.Critical
                ? $"{current} · 已到达警戒线"
                : $"{current} · 接近警戒线";
        return new BalanceAlertEventArgs(
            kind,
            title,
            message,
            account.Id,
            account.Label?.Trim() ?? "",
            ApiKeySanitizer.MaskSuffix(account.KeySuffix));
    }

    private static string AlertTitle(BalanceAlertKind kind) => kind switch
    {
        BalanceAlertKind.Warning15 => "余额预警",
        BalanceAlertKind.Critical => "余额临界",
        BalanceAlertKind.Anomaly => "异常变动",
        _ => "余额通知"
    };

    private static string ReadableError(Exception exception, string? credentialKey = null)
    {
        var message = exception switch
        {
            TaskCanceledException => "网络请求超时",
            HttpRequestException => "网络连接失败",
            ProviderApiException api => api.Message,
            _ => exception.Message
        };
        // Provider responses may echo the credential back (e.g. 401 "invalid key <key>").
        // Redact it before it can reach state.json, the island or notifications.
        message = ApiKeySanitizer.RedactSecret(message, credentialKey);
        return message.Length > 120 ? message[..120] : message;
    }

    private static string NormalizeIslandColor(string color, string parameterName)
    {
        if (IslandColorPalettes.TryNormalizeColor(color, out var normalized)) return normalized;
        throw new ArgumentException("颜色必须是 #RRGGBB 或 #AARRGGBB", parameterName);
    }

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

public sealed class BalanceAlertEventArgs(
    BalanceAlertKind kind,
    string title,
    string message,
    string accountId,
    string accountNote,
    string maskedKeySuffix) : EventArgs
{
    public BalanceAlertKind Kind { get; } = kind;
    public string Title { get; } = title;
    public string Message { get; } = message;
    public string AccountId { get; } = accountId;
    public string AccountNote { get; } = accountNote;
    public string MaskedKeySuffix { get; } = maskedKeySuffix;
}

public sealed record EnvironmentImportResult(
    int FoundCount,
    IReadOnlyList<string> AddedProviders,
    IReadOnlyList<string> RefreshedProviders);
