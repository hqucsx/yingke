using YingKe.Core.Configuration;
using YingKe.Core.Security;

namespace YingKe.Core.Recognition;

/// <summary>按配置与凭据库产出 Provider；未配置 API Key 时返回 null（调用方降级本地能力）。</summary>
public static class ProviderFactory
{
    public const string ApiKeyCredentialName = "ai.apikey";

    public static IVisionLanguageProvider? FromConfig(AppConfig config)
    {
        // 旧版凭据名迁移：Ta/ai.apikey → YingKe/ai.apikey
        var key = CredentialStore.Read(ApiKeyCredentialName);
        if (string.IsNullOrWhiteSpace(key))
        {
            var legacy = CredentialStore.Read("ai.apikey");
            if (!string.IsNullOrWhiteSpace(legacy))
            {
                CredentialStore.Save(ApiKeyCredentialName, legacy);
                CredentialStore.Delete("ai.apikey");
                key = legacy;
            }
        }
        if (string.IsNullOrWhiteSpace(key))
            return null;

        return config.Ai.Provider switch
        {
            AiProvider.OpenAiCompatible => new OpenAiCompatibleProvider(config.Ai.BaseUrl, config.Ai.Model, key),
            AiProvider.AzureOpenAi => new AzureOpenAiProvider(config.Ai.BaseUrl, config.Ai.Model, key),
            AiProvider.Anthropic => new AnthropicProvider(config.Ai.BaseUrl, config.Ai.Model, key),
            AiProvider.Gemini => new GeminiProvider(config.Ai.BaseUrl, config.Ai.Model, key),
            _ => new OpenAiCompatibleProvider(config.Ai.BaseUrl, config.Ai.Model, key),
        };
    }
}
