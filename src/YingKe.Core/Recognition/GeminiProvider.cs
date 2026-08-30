using System.Drawing;
using System.Text.Json.Nodes;

namespace YingKe.Core.Recognition;

/// <summary>Google Gemini generateContent 协议。</summary>
public sealed class GeminiProvider : VisionProviderBase
{
    public GeminiProvider(string baseUrl, string model, string apiKey)
        : base(baseUrl, model, apiKey)
    {
    }

    public override string Name => $"Gemini · {Model}";

    public override async Task<string> ChatAsync(string systemPrompt, string? userText, Bitmap? image, CancellationToken cancellationToken = default)
    {
        var payload = BuildPayload(systemPrompt, userText, image != null ? ToBase64Png(image) : null);
        var endpoint = BaseUrl.Contains(":generateContent")
            ? BaseUrl
            : $"{BaseUrl}/models/{Model}:generateContent";
        var url = AppendQuery(endpoint, $"key={Uri.EscapeDataString(ApiKey)}");
        var body = await SendAsync(url, payload, cancellationToken);
        return ParseResponse(body);
    }

    public static string BuildPayload(string system, string? userText, string? imageBase64)
    {
        var parts = new JsonArray();
        if (imageBase64 != null)
            parts.Add(Obj(("inline_data", Obj(("mime_type", "image/png"), ("data", imageBase64)))));
        parts.Add(Obj(("text", userText ?? " ")));

        var root = Obj(
            ("system_instruction", Obj(("parts", (JsonNode)Arr(Obj(("text", system)))))),
            ("contents", (JsonNode)Arr(Obj(("role", "user"), ("parts", (JsonNode)parts)))));
        return root.ToJsonString();
    }

    public static string ParseResponse(string json)
    {
        var root = ParseJson(json);
        var parts = root["candidates"]?[0]?["content"]?["parts"] as JsonArray
            ?? throw new InvalidOperationException("Gemini 响应缺少 candidates[0].content.parts");

        var text = string.Concat(parts
            .OfType<JsonObject>()
            .Select(o => o["text"]?.GetValue<string>() ?? string.Empty));

        return text.Length > 0 ? text : throw new InvalidOperationException("Gemini 响应无文本内容");
    }
}
