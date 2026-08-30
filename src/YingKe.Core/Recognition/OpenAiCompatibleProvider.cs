using System.Drawing;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace YingKe.Core.Recognition;

/// <summary>OpenAI 兼容协议（/chat/completions），也作为 Azure 的载荷基类。</summary>
public class OpenAiCompatibleProvider : VisionProviderBase
{
    public OpenAiCompatibleProvider(string baseUrl, string model, string apiKey)
        : base(baseUrl, model, apiKey)
    {
    }

    public override string Name => $"OpenAI 兼容 · {Model}";

    protected virtual string EndpointUrl => EnsureSuffix(BaseUrl, "/chat/completions");

    protected override void ConfigureRequest(HttpRequestMessage request)
        => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

    public override async Task<string> ChatAsync(string systemPrompt, string? userText, Bitmap? image, CancellationToken cancellationToken = default)
    {
        var payload = BuildPayload(Model, systemPrompt, userText, image != null ? ToBase64Png(image) : null);
        var body = await SendAsync(EndpointUrl, payload, cancellationToken);
        return ParseResponse(body);
    }

    public static string BuildPayload(string model, string system, string? userText, string? imageBase64)
    {
        JsonNode content;
        if (imageBase64 != null)
        {
            var parts = new JsonArray
            {
                Obj(("type", "image_url"), ("image_url", Obj(("url", $"data:image/png;base64,{imageBase64}")))),
                Obj(("type", "text"), ("text", string.IsNullOrEmpty(userText) ? " " : userText)),
            };
            content = parts;
        }
        else
        {
            content = userText ?? " ";
        }

        var root = Obj(
            ("model", model),
            ("messages", (JsonNode)Arr(
                Obj(("role", "system"), ("content", system)),
                Obj(("role", "user"), ("content", content)))),
            ("max_tokens", 2048));
        return root.ToJsonString();
    }

    public static string ParseResponse(string json)
    {
        var root = ParseJson(json);
        var choice = (root["choices"] as JsonArray) is { Count: > 0 } choices
            ? choices[0] as JsonObject
            : throw new InvalidOperationException("OpenAI 响应缺少 choices[0]");
        return choice["message"]?["content"]?.GetValue<string>()
            ?? throw new InvalidOperationException("OpenAI 响应缺少 message.content");
    }
}

/// <summary>Azure OpenAI：URL 指向部署（带 api-version 查询参数），用 api-key 头鉴权。</summary>
public sealed class AzureOpenAiProvider : OpenAiCompatibleProvider
{
    public AzureOpenAiProvider(string deploymentUrl, string model, string apiKey)
        : base(deploymentUrl, model, apiKey)
    {
    }

    public override string Name => $"Azure OpenAI · {Model}";

    protected override string EndpointUrl
        => BaseUrl.Contains("chat/completions") || BaseUrl.Contains('?')
            ? BaseUrl // 用户粘贴的是完整端点（可能已带 api-version）
            : $"{EnsureSuffix(BaseUrl, "/chat/completions")}?api-version=2024-02-01";

    protected override void ConfigureRequest(HttpRequestMessage request)
        => request.Headers.Add("api-key", ApiKey);
}
