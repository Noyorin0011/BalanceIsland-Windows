namespace BalanceIsland.Windows;

public static class EnvironmentCredentialDiscovery
{
    private static readonly IReadOnlyDictionary<Provider, string[]> Names =
        new Dictionary<Provider, string[]>
        {
            [Provider.DeepSeek] = ["DEEPSEEK_API_KEY"],
            [Provider.OpenAI] = ["OPENAI_ADMIN_KEY", "OPENAI_API_KEY"],
            [Provider.OpenRouter] = ["OPENROUTER_API_KEY"],
            [Provider.SiliconFlow] = ["SILICONFLOW_API_KEY"],
            [Provider.Moonshot] = ["MOONSHOT_API_KEY", "KIMI_API_KEY"],
            [Provider.MiMo] = ["MIMO_API_KEY", "XIAOMI_MIMO_API_KEY"],
            [Provider.Anthropic] = ["ANTHROPIC_API_KEY"],
            [Provider.Gemini] = ["GEMINI_API_KEY", "GOOGLE_API_KEY"],
            [Provider.XAI] = ["XAI_API_KEY", "GROK_API_KEY"]
        };

    public static IReadOnlyList<EnvironmentCredentialCandidate> Scan()
    {
        var result = new List<EnvironmentCredentialCandidate>();
        foreach (var pair in Names)
        {
            foreach (var name in pair.Value)
            {
                var value = Read(name);
                if (string.IsNullOrWhiteSpace(value)) continue;
                var key = ApiKeySanitizer.Clean(value);
                if (string.IsNullOrWhiteSpace(key)) continue;
                result.Add(new EnvironmentCredentialCandidate(pair.Key, name, key));
                break;
            }
        }
        return result;
    }

    public static string? Read(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try
        {
            var value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
            if (!string.IsNullOrWhiteSpace(value)) return value;
            if (OperatingSystem.IsWindows())
            {
                value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
                if (!string.IsNullOrWhiteSpace(value)) return value;
                value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
            }
            return value;
        }
        catch
        {
            return null;
        }
    }

    public static string SupportedVariablesText =>
        "DeepSeek: DEEPSEEK_API_KEY · OpenAI: OPENAI_ADMIN_KEY / OPENAI_API_KEY · " +
        "OpenRouter: OPENROUTER_API_KEY · SiliconFlow: SILICONFLOW_API_KEY · " +
        "Moonshot: MOONSHOT_API_KEY / KIMI_API_KEY · MiMo: MIMO_API_KEY / XIAOMI_MIMO_API_KEY · " +
        "Anthropic: ANTHROPIC_API_KEY · Gemini: GEMINI_API_KEY / GOOGLE_API_KEY · " +
        "xAI: XAI_API_KEY / GROK_API_KEY";

    public static string LimitationsText =>
        "环境变量凭据本身覆盖当前全部 Provider；但 MiMo、Anthropic、Gemini、xAI 当前仅验证 Key，" +
        "没有可用的官方余额接口。OpenAI 普通 OPENAI_API_KEY 仅验证 Key；余额/组织消费查询需要 OPENAI_ADMIN_KEY（sk-admin-）。";
}

public sealed record EnvironmentCredentialCandidate(Provider Provider, string VariableName, string ApiKey);
