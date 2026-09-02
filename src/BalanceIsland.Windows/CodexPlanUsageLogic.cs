using System.Text.Json;
using System.Text.Json.Nodes;

namespace BalanceIsland.Windows;

public static class CodexPlanUsageParser
{
    public static CodexPlanUsage Parse(string json, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            throw new FormatException("套餐响应不是合法 JSON。");
        }
        if (root is not JsonObject rootObject)
            throw new FormatException("套餐响应必须是 JSON 对象。");

        var planType = rootObject["plan_type"]?.GetValue<string>() ?? "";
        var rateLimit = rootObject["rate_limit"] as JsonObject ?? rootObject;
        var primary = ParseWindow(rateLimit["primary_window"] as JsonObject, now);
        var secondary = ParseWindow(rateLimit["secondary_window"] as JsonObject, now);
        if (primary is null && secondary is null)
            throw new FormatException("套餐响应没有可用额度窗口");

        return new CodexPlanUsage
        {
            PlanType = planType,
            Primary = primary,
            Secondary = secondary,
            UpdatedAt = now
        };
    }

    private static CodexPlanQuotaWindow? ParseWindow(JsonObject? window, DateTimeOffset now)
    {
        if (window is null) return null;
        if (!TryReadDouble(window, "used_percent", out var used) || !double.IsFinite(used))
            return null;
        var remaining = Math.Clamp((int)Math.Round(100d - used, MidpointRounding.AwayFromZero), 0, 100);
        long? resetAt = null;
        if (TryReadLong(window, "reset_at", out var reset)) resetAt = reset;
        else if (TryReadLong(window, "reset_after_seconds", out var resetAfter))
            resetAt = now.ToUnixTimeSeconds() + resetAfter;
        long? windowSeconds = TryReadLong(window, "limit_window_seconds", out var seconds)
            ? seconds : null;
        return new CodexPlanQuotaWindow
        {
            RemainingPercent = remaining,
            ResetAtUnixSeconds = resetAt,
            WindowSeconds = windowSeconds
        };
    }

    private static bool TryReadDouble(JsonObject obj, string name, out double value)
    {
        value = 0;
        return obj[name] is JsonValue node &&
            (node.TryGetValue<double>(out value) || double.TryParse(node.ToJsonString(), out value));
    }

    private static bool TryReadLong(JsonObject obj, string name, out long value)
    {
        value = 0;
        return obj[name] is JsonValue node &&
            (node.TryGetValue<long>(out value) || long.TryParse(node.ToJsonString(), out value));
    }
}

public static class CodexPlanWindowClassifier
{
    public static CodexPlanWindowKind Classify(long? windowSeconds)
    {
        if (windowSeconds is { } seconds)
        {
            if (seconds is >= 14_400 and <= 21_600) return CodexPlanWindowKind.FiveHour;
            if (seconds is >= 518_400 and <= 691_200) return CodexPlanWindowKind.Weekly;
        }
        return CodexPlanWindowKind.Unknown;
    }
}

public static class CodexPlanOverlayFormatter
{
    public static string Format(CodexPlanUsage usage, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(usage);
        var stale = now - usage.UpdatedAt > TimeSpan.FromMinutes(15);
        var parts = new List<string> { $"套餐 {usage.PlanType}" };
        if (usage.Primary is { } primary)
            parts.Add($"5h 剩 {primary.RemainingPercent}%");
        if (usage.Secondary is { } secondary)
            parts.Add($"周 剩 {secondary.RemainingPercent}%");
        if (stale) parts.Add("数据较旧");
        return string.Join(" · ", parts);
    }
}

public static class CodexPlanOriginPolicy
{
    public static bool CanRead(Uri? uri) =>
        uri is not null &&
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        uri.IsDefaultPort &&
        string.Equals(uri.Host, "chatgpt.com", StringComparison.OrdinalIgnoreCase);
}

public sealed record CodexPlanScriptEnvelope(int StatusCode, string BodyJson);

public static class CodexPlanScriptEnvelopeParser
{
    private const int MaxEnvelopeBytes = 64 * 1024;

    public static CodexPlanScriptEnvelope Parse(string executeScriptResult)
    {
        ArgumentNullException.ThrowIfNull(executeScriptResult);
        if (System.Text.Encoding.UTF8.GetByteCount(executeScriptResult) > MaxEnvelopeBytes)
            throw new FormatException("脚本执行结果超过 64 KiB 上限。");

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(executeScriptResult);
        }
        catch (JsonException)
        {
            throw new FormatException("脚本执行结果不是合法 JSON。");
        }
        if (node is not JsonObject root) throw new FormatException("脚本执行结果必须是 JSON 对象。");

        var status = root["status"]?.GetValue<int>()
            ?? throw new FormatException("脚本执行结果缺少 status。");
        var body = (root["body"] as JsonObject)?.DeepClone() ?? new JsonObject();
        var projected = new JsonObject
        {
            ["status"] = status,
            ["body"] = body
        };
        return new CodexPlanScriptEnvelope(status, projected.ToJsonString());
    }
}
