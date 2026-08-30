using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace YingKe.Core.Recognition;

/// <summary>四协议共享：图像编码、降采样、HTTP 收发。</summary>
public abstract class VisionProviderBase : IVisionLanguageProvider
{
    protected static readonly HttpClient Http = CreateClient(useSystemProxy: true);
    protected static readonly HttpClient DirectClient = CreateClient(useSystemProxy: false);

    protected readonly string BaseUrl;
    protected readonly string Model;
    protected readonly string ApiKey;

    private const int MaxImageSide = 2000;

    protected VisionProviderBase(string baseUrl, string model, string apiKey)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        Model = model;
        ApiKey = apiKey;
    }

    public abstract string Name { get; }

    public abstract Task<string> ChatAsync(string systemPrompt, string? userText, Bitmap? image, CancellationToken cancellationToken = default);

    private static HttpClient CreateClient(bool useSystemProxy)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            UseProxy = useSystemProxy,
        };
        var client = new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(90);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("YingKe/0.1");
        return client;
    }

    /// <summary>PNG → base64（超长边先等比降采样，控制请求体积）。</summary>
    protected static string ToBase64Png(Bitmap image)
    {
        Bitmap toEncode = image;
        double scale = Math.Min(1.0,
            Math.Min((double)MaxImageSide / Math.Max(1, image.Width), (double)MaxImageSide / Math.Max(1, image.Height)));
        if (scale < 1.0)
        {
            int w = Math.Max(1, (int)Math.Round(image.Width * scale));
            int h = Math.Max(1, (int)Math.Round(image.Height * scale));
            toEncode = new Bitmap(w, h);
            using var g = Graphics.FromImage(toEncode);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, 0, 0, w, h);
        }

        using var ms = new MemoryStream();
        toEncode.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return Convert.ToBase64String(ms.ToArray());
    }

    protected virtual void ConfigureRequest(HttpRequestMessage request) { }

    protected async Task<string> SendAsync(string url, string jsonBody, CancellationToken ct)
    {
        try
        {
            return await SendWithAsync(Http, url, jsonBody, ct);
        }
        catch (HttpRequestException ex) when (IsSslError(ex))
        {
            // 用户常开本地代理（Clash 等，127.0.0.1:7897 之类）；代理对 TLS 握手
            // 偶发失败时直连重试一次。国内端点直连通常更稳。
            try
            {
                return await SendWithAsync(DirectClient, url, jsonBody, ct);
            }
            catch (HttpRequestException directEx) when (IsSslError(directEx))
            {
                throw new HttpRequestException(
                    $"SSL 连接失败（已尝试直连重试）。常见原因：本地代理/VPN 对 HTTPS 的拦截或节点异常。{Truncate(directEx.Message, 200)}", directEx);
            }
        }
    }

    private static bool IsSslError(HttpRequestException? ex)
        => ex != null && (ex.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("secure channel", StringComparison.OrdinalIgnoreCase)
            || (ex.InnerException?.Message?.Contains("SSL", StringComparison.OrdinalIgnoreCase) ?? false));

    private async Task<string> SendWithAsync(HttpClient client, string url, string jsonBody, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
        };
        ConfigureRequest(request);

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} @ {SafeUrl(url)}：{Truncate(body, 300)}");
        return body;
    }


    private static string Truncate(string text, int max) =>
        string.IsNullOrEmpty(text) || text.Length <= max ? text ?? string.Empty : text[..max] + "…";

    // ---- JSON 构建辅助（子类与单测共用） ----

    /// <summary>BaseUrl 容错：用户常直接粘贴完整端点，已含后缀时不再追加。</summary>
    protected static string EnsureSuffix(string baseUrl, string suffix)
        => baseUrl.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? baseUrl : baseUrl + suffix;

    protected static string AppendQuery(string url, string query)
        => url.Contains('?') ? $"{url}&{query}" : $"{url}?{query}";

    /// <summary>用于错误信息的安全 URL（去掉可能含 Key 的查询串）。</summary>
    protected static string SafeUrl(string url)
    {
        try { return new Uri(url).GetLeftPart(UriPartial.Path); }
        catch { return url;
        }
    }

    protected static JsonNode ParseJson(string json)
        => JsonNode.Parse(json) ?? throw new InvalidOperationException("响应不是有效 JSON。");

    protected static JsonObject Obj(params (string key, JsonNode? node)[] fields)
    {
        var o = new JsonObject();
        foreach (var (key, node) in fields) o[key] = node;
        return o;
    }

    protected static JsonArray Arr(params JsonNode?[] nodes)
    {
        var a = new JsonArray();
        foreach (var n in nodes) a.Add(n);
        return a;
    }
}
