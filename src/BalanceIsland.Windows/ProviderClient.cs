using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace BalanceIsland.Windows;

public sealed class ProviderClient
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    public Task<BalanceSnapshot> FetchAsync(ApiCredential credential, CancellationToken token) =>
        credential.Account.Provider switch
        {
            Provider.DeepSeek => FetchDeepSeekAsync(credential, token),
            Provider.OpenAI when credential.ApiKey.StartsWith("sk-admin-", StringComparison.Ordinal)
                => FetchOpenAiAsync(credential, token),
            Provider.OpenAI => VerifyBearerAsync(credential, "https://api.openai.com/v1/models", token),
            Provider.OpenRouter => FetchOpenRouterAsync(credential, token),
            Provider.SiliconFlow => FetchSiliconFlowAsync(credential, token),
            Provider.Moonshot => FetchMoonshotAsync(credential, token),
            Provider.MiMo => VerifyMiMoAsync(credential, token),
            Provider.Anthropic => VerifyAnthropicAsync(credential, token),
            Provider.Gemini => VerifyGeminiAsync(credential, token),
            Provider.XAI => VerifyBearerAsync(credential, "https://api.x.ai/v1/models", token),
            _ => throw new ArgumentOutOfRangeException()
        };

    private async Task<BalanceSnapshot> FetchDeepSeekAsync(ApiCredential credential, CancellationToken token)
    {
        using var json = await GetJsonAsync(
            "https://api.deepseek.com/user/balance", Bearer(credential.ApiKey), token);
        var root = json.RootElement;
        if (!root.TryGetProperty("balance_infos", out var infos) || infos.GetArrayLength() == 0)
            throw new ProviderApiException("响应中没有余额币种");

        var selected = infos.EnumerateArray().FirstOrDefault(item =>
            item.TryGetProperty("currency", out var currency) && currency.GetString() == "CNY");
        if (selected.ValueKind == JsonValueKind.Undefined) selected = infos.EnumerateArray().First();

        var code = selected.StringOr("currency", "CNY");
        var total = selected.NumberOr("total_balance", 0);
        var granted = selected.NumberOr("granted_balance", 0);
        var toppedUp = selected.NumberOr("topped_up_balance", 0);
        var available = root.BoolOr("is_available", total > 0);
        var sign = BalanceSnapshot.CurrencySymbol(code);
        return Snapshot(credential, $"{sign}{total:0.00}",
            $"充值 {sign}{toppedUp:0.00} · 赠送 {sign}{granted:0.00}", total, code,
            available ? SnapshotStatus.Ok : SnapshotStatus.Warning);
    }

    private async Task<BalanceSnapshot> FetchOpenAiAsync(ApiCredential credential, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var todayStart = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var costsUrl = "https://api.openai.com/v1/organization/costs" +
            $"?start_time={monthStart.ToUnixTimeSeconds()}&end_time={now.ToUnixTimeSeconds()}" +
            "&bucket_width=1d&limit=31";
        using var costs = await GetJsonAsync(costsUrl, Bearer(credential.ApiKey), token);
        if (!costs.RootElement.TryGetProperty("data", out var buckets))
            throw new ProviderApiException("响应中没有用量数据");

        var spent = 0d;
        var todaySpent = 0d;
        foreach (var bucket in buckets.EnumerateArray())
        {
            var bucketSpent = 0d;
            if (bucket.TryGetProperty("results", out var results))
            {
                foreach (var result in results.EnumerateArray())
                {
                    if (result.TryGetProperty("amount", out var amount))
                        bucketSpent += amount.NumberOr("value", 0);
                }
            }
            spent += bucketSpent;
            if (bucket.LongOr("start_time", 0) >= todayStart.ToUnixTimeSeconds())
                todaySpent += bucketSpent;
        }

        double? limit = null;
        try
        {
            using var spendLimit = await GetJsonAsync(
                "https://api.openai.com/v1/organization/spend_limit", Bearer(credential.ApiKey), token);
            if (spendLimit.RootElement.TryNumber("threshold_amount", out var cents)) limit = cents / 100d;
        }
        catch (ProviderApiException)
        {
            // Some organizations do not expose a hard spend limit.
        }

        if (limit is not null)
        {
            var remaining = Math.Max(0, limit.Value - spent);
            return Snapshot(credential, $"可用 ${remaining:0.00}",
                $"本月已用 ${spent:0.00} / ${limit:0.00}", remaining, "USD",
                remaining <= limit.Value * .1 ? SnapshotStatus.Warning : SnapshotStatus.Ok,
                todaySpent);
        }

        return Snapshot(credential, $"本月消费 ${spent:0.00}", "无硬性消费上限", null,
            "USD", SnapshotStatus.Ok, todaySpent);
    }

    private async Task<BalanceSnapshot> FetchOpenRouterAsync(ApiCredential credential, CancellationToken token)
    {
        using var credits = await GetJsonAsync(
            "https://openrouter.ai/api/v1/credits", Bearer(credential.ApiKey), token);
        if (!credits.RootElement.TryGetProperty("data", out var data))
            throw new ProviderApiException("响应中没有额度数据");
        var purchased = data.RequiredNumber("total_credits");
        var used = data.RequiredNumber("total_usage");
        var remaining = Math.Max(0, purchased - used);

        double? todayUsed = null;
        try
        {
            using var keys = await GetJsonAsync(
                "https://openrouter.ai/api/v1/keys", Bearer(credential.ApiKey), token);
            if (keys.RootElement.TryGetProperty("data", out var keyRows))
            {
                var values = keyRows.EnumerateArray()
                    .Select(row => row.TryNumber("usage_daily", out var value) ? value : (double?)null)
                    .Where(value => value is not null)
                    .Select(value => value!.Value)
                    .ToArray();
                if (values.Length > 0) todayUsed = values.Sum();
            }
        }
        catch (ProviderApiException)
        {
            // The credits response is still useful when per-key records are unavailable.
        }

        return Snapshot(credential, $"${remaining:0.00}",
            $"累计购买 ${purchased:0.00} · 已用 ${used:0.00}", remaining, "USD",
            SnapshotStatus.Ok, todayUsed);
    }

    private async Task<BalanceSnapshot> FetchMoonshotAsync(ApiCredential credential, CancellationToken token)
    {
        JsonDocument json;
        string currency;
        try
        {
            json = await GetJsonAsync(
                "https://api.moonshot.cn/v1/users/me/balance", Bearer(credential.ApiKey), token);
            currency = "CNY";
        }
        catch (ProviderApiException)
        {
            json = await GetJsonAsync(
                "https://api.moonshot.ai/v1/users/me/balance", Bearer(credential.ApiKey), token);
            currency = "USD";
        }
        using (json)
        {
            if (!json.RootElement.TryGetProperty("data", out var data))
                throw new ProviderApiException("响应中没有余额数据");
            var available = data.RequiredNumber("available_balance");
            var cash = data.RequiredNumber("cash_balance");
            var voucher = data.RequiredNumber("voucher_balance");
            var sign = BalanceSnapshot.CurrencySymbol(currency);
            return Snapshot(credential, $"{sign}{available:0.00}",
                $"现金 {sign}{cash:0.00} · 赠金 {sign}{voucher:0.00}", available, currency,
                available > 0 ? SnapshotStatus.Ok : SnapshotStatus.Warning);
        }
    }

    private async Task<BalanceSnapshot> FetchSiliconFlowAsync(ApiCredential credential, CancellationToken token)
    {
        using var json = await GetJsonAsync(
            "https://api.siliconflow.cn/v1/user/info", Bearer(credential.ApiKey), token);
        if (!json.RootElement.TryGetProperty("data", out var data))
            throw new ProviderApiException("响应中没有账户数据");
        var total = FirstNumber(data, "totalBalance", "total_balance", "balance")
            ?? throw new ProviderApiException("响应中没有可识别余额");
        var charged = FirstNumber(data, "chargeBalance", "charge_balance");
        var gifted = FirstNumber(data, "balance");
        var detail = new List<string>();
        if (charged is not null) detail.Add($"充值 ¥{charged:0.00}");
        if (gifted is not null) detail.Add($"赠送 ¥{gifted:0.00}");
        return Snapshot(credential, $"¥{total:0.00}",
            detail.Count == 0 ? "官方账户总余额" : string.Join(" · ", detail),
            total, "CNY", SnapshotStatus.Ok);
    }

    private async Task<BalanceSnapshot> VerifyBearerAsync(
        ApiCredential credential, string url, CancellationToken token)
    {
        using var ignored = await GetJsonAsync(url, Bearer(credential.ApiKey), token);
        return Verified(credential);
    }

    private async Task<BalanceSnapshot> VerifyAnthropicAsync(ApiCredential credential, CancellationToken token)
    {
        using var ignored = await GetJsonAsync("https://api.anthropic.com/v1/models",
            new Dictionary<string, string>
            {
                ["x-api-key"] = credential.ApiKey,
                ["anthropic-version"] = "2023-06-01"
            }, token);
        return Verified(credential);
    }

    private async Task<BalanceSnapshot> VerifyGeminiAsync(ApiCredential credential, CancellationToken token)
    {
        using var ignored = await GetJsonAsync(
            "https://generativelanguage.googleapis.com/v1beta/models",
            new Dictionary<string, string> { ["x-goog-api-key"] = credential.ApiKey }, token);
        return Verified(credential);
    }

    private async Task<BalanceSnapshot> VerifyMiMoAsync(ApiCredential credential, CancellationToken token)
    {
        if (credential.ApiKey.StartsWith("tp-", StringComparison.OrdinalIgnoreCase))
            throw new ProviderApiException("MiMo Token Plan Key 不支持普通余额查询");
        using var ignored = await GetJsonAsync("https://api.xiaomimimo.com/v1/models",
            new Dictionary<string, string> { ["api-key"] = credential.ApiKey }, token);
        return Verified(credential);
    }

    private static BalanceSnapshot Verified(ApiCredential credential) => Snapshot(
        credential, "Key 有效", "该 Provider 需要手动余额", null,
        credential.Account.Provider.DefaultCurrency(), SnapshotStatus.Ok);

    private async Task<JsonDocument> GetJsonAsync(
        string url, IReadOnlyDictionary<string, string> headers, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        foreach (var pair in headers) request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        var body = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
        {
            var detail = TryReadError(body);
            var message = string.IsNullOrWhiteSpace(detail)
                ? $"HTTP {(int)response.StatusCode}"
                : $"HTTP {(int)response.StatusCode}: {detail}";
            throw new ProviderApiException(message, (int)response.StatusCode, ParseRetryAfter(response));
        }
        if (string.IsNullOrWhiteSpace(body)) throw new ProviderApiException("服务器返回空响应");
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new ProviderApiException($"无法解析服务器响应：{exception.Message}");
        }
    }

    private static IReadOnlyDictionary<string, string> Bearer(string key) =>
        new Dictionary<string, string> { ["Authorization"] = "Bearer " + key };

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta) return delta;
        if (response.Headers.RetryAfter?.Date is { } date)
            return date - DateTimeOffset.UtcNow > TimeSpan.Zero
                ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(1);
        return null;
    }

    private static string? TryReadError(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String) return error.GetString();
                if (error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("message", out var message)) return message.GetString();
            }
        }
        catch (JsonException) { }
        return null;
    }

    private static double? FirstNumber(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.TryNumber(name, out var result)) return result;
        return null;
    }

    private static BalanceSnapshot Snapshot(
        ApiCredential credential,
        string primary,
        string secondary,
        double? balance,
        string currency,
        SnapshotStatus status,
        double? todayUsed = null) => new()
    {
        Provider = credential.Account.Provider,
        CredentialId = credential.Account.Id,
        AccountLabel = credential.Account.Label,
        KeySuffix = credential.Account.KeySuffix,
        PrimaryText = primary,
        SecondaryText = secondary,
        BalanceAmount = balance,
        CurrencyCode = currency,
        Status = status,
        UpdatedAt = DateTimeOffset.Now,
        TodayUsedAmount = todayUsed
    };
}

internal static class JsonElementExtensions
{
    public static bool TryNumber(this JsonElement element, string name, out double result)
    {
        result = 0;
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return false;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetDouble(out result),
            JsonValueKind.String => double.TryParse(value.GetString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out result),
            _ => false
        };
    }

    public static double NumberOr(this JsonElement element, string name, double fallback) =>
        element.TryNumber(name, out var result) ? result : fallback;

    public static double RequiredNumber(this JsonElement element, string name) =>
        element.TryNumber(name, out var result)
            ? result : throw new ProviderApiException($"响应缺少字段：{name}");

    public static string StringOr(this JsonElement element, string name, string fallback) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback : fallback;

    public static bool BoolOr(this JsonElement element, string name, bool fallback) =>
        element.TryGetProperty(name, out var value) &&
        (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean() : fallback;

    public static long LongOr(this JsonElement element, string name, long fallback) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result)
            ? result : fallback;
}
