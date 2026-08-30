using System.Drawing;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace YingKe.Core.Recognition;

/// <summary>Anthropic Claude Messages 协议。</summary>
public sealed class AnthropicProvider : VisionProviderBase
{
    public AnthropicProvider(string baseUrl, string model, string apiKey)
        : base(baseUrl, model, apiKey)
    {
    }

    public override string Name => $"Anthropic · {Model}";

    protected override void ConfigureRequest(HttpRequestMessage request)
    {
        request.Headers.Add("x-api-key", ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
    }

    public override async Task<string> ChatAsync(string systemPrompt, string? userText, Bitmap? image, CancellationToken cancellationToken = default)
    {
        var payload = BuildPayload(Model, systemPrompt, userText, image != null ? ToBase64Png(image) : null);
        // BaseUrl 容错：用户可能直接粘贴完整端点（如智谱的 .../anthropic/v1/messages）
        var url = EnsureSuffix(BaseUrl, "/messages");
        var body = await SendAsync(url, payload, cancellationToken);
        return ParseResponse(body);
    }

    public static string BuildPayload(string model, string system, string? userText, string? imageBase64)
    {
        var parts = new JsonArray();
        if (imageBase64 != null)
        {
            parts.Add(Obj(
                ("type", "image"),
                ("source", Obj(
                    ("type", "base64"),
                    ("media_type", "image/png"),
                    ("data", imageBase64)))));
        }
        parts.Add(Obj(("type", "text"), ("text", string.IsNullOrEmpty(userText) ? " " : userText)));

        var root = Obj(
            ("model", model),
            ("max_tokens", 2048),
            ("system", system),
            ("messages", (JsonNode)Arr(Obj(("role", "user"), ("content", (JsonNode)parts)))));
        return root.ToJsonString();
    }

    public static string ParseResponse(string json)
    {
        var root = ParseJson(json);
        var content = root["content"] as JsonArray
            ?? throw new InvalidOperationException("Anthropic 响应缺少 content");

        var text = string.Concat(content
            .OfType<JsonObject>()
            .Where(o => o["type"]?.GetValue<string>() == "text")
            .Select(o => o["text"]?.GetValue<string>() ?? string.Empty));

        return text.Length > 0 ? text : throw new InvalidOperationException("Anthropic 响应无文本内容");
    }
}
