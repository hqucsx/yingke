using System.Text;
using System.Text.Json.Nodes;

namespace YingKe.Core.Translation;

/// <summary>
/// 内置翻译引擎（参考 STranslate 免 Key 插件）：多服务自动故障转移。
/// ① 腾讯交互翻译 Transmart（国内直连）② Google gtx（经系统代理）。
/// 网络层韧性：SSL/连接失败自动切换直连与代理两条路径。
/// </summary>
public static class BuiltInTranslator
{
    public static string Name => "内置翻译";

    private const string TransmartUrl = "https://transmart.qq.com/api/imt";
    private const string GoogleUrl = "https://translate.googleapis.com/translate_a/single";

    private const string UA =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    private static readonly HttpClient ProxiedHttp = CreateClient(30, useProxy: true);
    private static readonly HttpClient DirectHttp = CreateClient(30, useProxy: false);

    private static HttpClient CreateClient(int timeoutSec, bool useProxy)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = useProxy,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        };
        var client = new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(timeoutSec);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UA);
        return client;
    }

    // ---- 语言映射 ----

    public static string ToTransmartCode(string uiLanguage) => uiLanguage switch
    {
        "简体中文" => "zh",
        "English" => "en",
        "日本語" => "ja",
        "한국어" => "ko",
        "Français" => "fr",
        "Deutsch" => "de",
        "Español" => "es",
        "Русский" => "ru",
        _ => "en",
    };

    public static string ToGoogleCode(string uiLanguage) => uiLanguage switch
    {
        "简体中文" => "zh-CN",
        "English" => "en",
        "日本語" => "ja",
        "한국어" => "ko",
        "Français" => "fr",
        "Deutsch" => "de",
        "Español" => "es",
        "Русский" => "ru",
        _ => "en",
    };

    // ---- 对外入口：腾讯优先，失败切 Google ----

    public static async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var errors = new List<string>();

        try
        {
            return await TransmartAsync(text, ToTransmartCode(targetLanguage), cancellationToken);
        }
        catch (Exception ex)
        {
            errors.Add($"腾讯: {Truncate(ex.Message, 80)}");
        }

        try
        {
            return await GoogleAsync(text, ToGoogleCode(targetLanguage), cancellationToken);
        }
        catch (Exception ex)
        {
            errors.Add($"Google: {Truncate(ex.Message, 80)}");
        }

        throw new InvalidOperationException("内置翻译全部服务失败 —— " + string.Join("；", errors));
    }

    // ---- 腾讯 Transmart ----

    public static string BuildTransmartPayload(string text, string sourceCode, string targetCode)
    {
        var root = new JsonObject
        {
            ["header"] = new JsonObject
            {
                ["fn"] = "auto_translation_block",
                ["client_key"] = "browser-chrome-110.0.0-Mac OS-df4bd4c5-a65d-44b2-a40f-42f34f3535f2-1677486696487",
            },
            ["type"] = "plain",
            ["model_category"] = "normal",
            ["source"] = new JsonObject
            {
                ["lang"] = sourceCode,
                ["text_block"] = text,
            },
            ["target"] = new JsonObject
            {
                ["lang"] = targetCode,
            },
        };
        return root.ToJsonString();
    }

    public static string ParseTransmart(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("Transmart 响应格式异常");
        var text = root["auto_translation"]?.GetValue<string>();
        return string.IsNullOrEmpty(text) ? throw new InvalidOperationException("Transmart 响应无译文") : text;
    }

    private static async Task<string> TransmartAsync(string text, string targetCode, CancellationToken ct)
    {
        var headers = new[] { ("Referer", "https://yi.qq.com/zh-CN/index") };
        var body = await SendAsync(HttpMethod.Post, TransmartUrl,
            BuildTransmartPayload(text, "auto", targetCode), headers, false, ct);
        return ParseTransmart(body);
    }

    // ---- Google gtx ----

    public static string BuildGoogleUrl(string text, string targetCode)
    {
        var q = Uri.EscapeDataString(text);
        return $"{GoogleUrl}?client=gtx&dt=t&dj=1&ie=UTF-8&oe=UTF-8&sl=auto&tl={targetCode}&q={q}";
    }

    public static string ParseGoogle(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("Google 响应格式异常");
        if (root["sentences"] is not JsonArray sentences || sentences.Count == 0)
            throw new InvalidOperationException("Google 响应无 sentences");

        var text = string.Concat(sentences
            .OfType<JsonObject>()
            .Select(o => o["trans"]?.GetValue<string>() ?? string.Empty));

        return text.Length > 0 ? text : throw new InvalidOperationException("Google 响应无译文");
    }

    private static async Task<string> GoogleAsync(string text, string targetCode, CancellationToken ct)
    {
        var body = await SendAsync(HttpMethod.Get, BuildGoogleUrl(text, targetCode), null,
            Array.Empty<(string name, string value)>(), false, ct);
        return ParseGoogle(body);
    }

    /// <summary>
    /// 统一发送：每条网络路径尝试一次（先优先路径再备用路径），返回响应正文。
    /// 正文必须在方法内部读完毕后返回——HttpResponseMessage 离开 using 作用域后不可再访问。
    /// </summary>
    private static async Task<string> SendAsync(HttpMethod method, string url, string? jsonBody,
        (string name, string value)[] headers, bool preferProxy, CancellationToken ct)
    {
        Exception? last = null;
        var clients = preferProxy ? new[] { ProxiedHttp, DirectHttp } : new[] { DirectHttp, ProxiedHttp };

        foreach (var client in clients)
        {
            try
            {
                using var request = new HttpRequestMessage(method, url);
                if (jsonBody != null)
                    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                foreach (var (name, value) in headers)
                    request.Headers.TryAddWithoutValidation(name, value);

                using var response = await client.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException(
                        $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} @ {url.Split('?')[0]}：{Truncate(body, 300)}");
                return body;
            }
            catch (Exception ex)
            {
                last = ex; // 换下一条网络路径重试
            }
        }
        throw new HttpRequestException(
            $"网络请求失败（代理与直连均尝试）：{Truncate(last?.Message ?? "", 200)}", last);
    }

    private static string Truncate(string text, int max)
        => string.IsNullOrEmpty(text) || text.Length <= max ? text ?? string.Empty : text[..max] + "…";
}
