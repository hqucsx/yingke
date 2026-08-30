using System.Drawing.Imaging;
using System.IO;
using System.Drawing;
using YingKe.Core.Geometry;
using RapidOcrNet;
using SkiaSharp;

namespace YingKe.Core.Ocr;

/// <summary>
/// RapidOCR 本地离线引擎（PaddleOCR PP-OCRv5 ONNX，基于 RapidOcrNet，Apache-2.0）。
/// det/cls 模型随 NuGet 自带；中文 rec 模型与字典首次使用时自动下载（ModelScope / Gitee 镜像）。
/// </summary>
public sealed class RapidOcrEngine : IOcrEngine
{
    public string Name => "RapidOCR（本地离线）";

    private static string ModelsDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YingKe", "models");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private const string ModelBase =
        "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx";
    private const string DetModelUrl = $"{ModelBase}/PP-OCRv5/det/ch_PP-OCRv5_det_mobile.onnx";
    private const string ClsModelUrl = $"{ModelBase}/PP-OCRv4/cls/ch_ppocr_mobile_v2.0_cls_mobile.onnx";
    private const string RecModelUrl = $"{ModelBase}/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile.onnx";
    private const string DictUrl =
        "https://gitee.com/paddlepaddle/PaddleOCR/raw/main/ppocr/utils/dict/ppocrv5_dict.txt";
    private const string DictUrlFallback =
        "https://cdn.jsdelivr.net/gh/PaddlePaddle/PaddleOCR@main/ppocr/utils/dict/ppocrv5_dict.txt";

    /// <summary>中文 rec 模型与字典是否已就绪。</summary>
    public static bool IsModelReady()
        => File.Exists(Path.Combine(ModelsDirectory, "ch_PP-OCRv5_rec_mobile.onnx"))
           && File.Exists(Path.Combine(ModelsDirectory, "ppocrv5_dict.txt"));

    // ONNX 会话只初始化一次并常驻复用（每次重建要重新加载模型，开销数秒）
    private static readonly object InitLock = new();
    private static RapidOcr? _sharedOcr;

    private static RapidOcr GetOcr(string detPath, string clsPath, string recPath, string keysPath)
    {
        lock (InitLock)
        {
            if (_sharedOcr == null)
            {
                var ocr = new RapidOcr();
                // 按 CPU 核心数配置推理线程：默认单线程在 CPU 上识别一屏要 7 秒以上
                using var options = RapidOcr.GetDefaultSessionOptions(Environment.ProcessorCount);
                ocr.InitModels(detPath, clsPath, recPath, keysPath, options);
                _sharedOcr = ocr;
            }
            return _sharedOcr;
        }
    }

    // 闲置回收：连续 5 分钟无识别时释放 ONNX 会话（模型常驻约几十 MB）；
    // 下次识别 GetOcr 自动重建（需数秒，RapidOcr 支持 Dispose 时连同原生内存一并释放）
    private const int IdleReleaseMinutes = 5;
    private static System.Threading.Timer? _idleTimer;

    private static void TouchIdleRelease()
    {
        lock (InitLock)
        {
            _idleTimer?.Dispose();
            _idleTimer = new System.Threading.Timer(_ =>
            {
                lock (InitLock)
                {
                    (_sharedOcr as IDisposable)?.Dispose();
                    _sharedOcr = null;
                }
            }, null, TimeSpan.FromMinutes(IdleReleaseMinutes), Timeout.InfiniteTimeSpan);
        }
    }

    public async Task<OcrResult> RecognizeAsync(Bitmap image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        var modelsDir = ModelsDirectory;
        var detPath = Path.Combine(modelsDir, "ch_PP-OCRv5_det_mobile.onnx");
        var clsPath = Path.Combine(modelsDir, "ch_ppocr_mobile_v2.0_cls_mobile.onnx");
        var recPath = Path.Combine(modelsDir, "ch_PP-OCRv5_rec_mobile.onnx");
        var keysPath = Path.Combine(modelsDir, "ppocrv5_dict.txt");

        await DownloadIfNeededAsync(detPath, DetModelUrl, cancellationToken);
        await DownloadIfNeededAsync(clsPath, ClsModelUrl, cancellationToken);
        await DownloadIfNeededAsync(recPath, RecModelUrl, cancellationToken);
        await DownloadDictIfNeededAsync(keysPath, cancellationToken);

        string pngPath = Path.Combine(Path.GetTempPath(), $"yingke-rapidocr-{Guid.NewGuid():N}.png");
        var diag = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            image.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);

            var ocr = GetOcr(detPath, clsPath, recPath, keysPath);
            TouchIdleRelease();
            diag.Stop();
            long initMs = diag.ElapsedMilliseconds;

            diag.Restart();
            var result = ocr.Detect(pngPath, RapidOcrOptions.Default);
            long detectMs = diag.ElapsedMilliseconds;

            // 诊断埋点：定位识别耗时分布（模型加载 vs 推理）
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "yingke-error.log"),
                    $"[rapid-diag] init={initMs}ms detect={detectMs}ms img={image.Width}x{image.Height}\r\n");
            }
            catch { }

            var lines = (result.StrRes ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => new OcrLine(t, default(PixelRect)))
                .ToList();

            var text = OcrTextCleaner.Clean(lines.Select(l => l.Text));
            return new OcrResult(Name, text, lines, "ppocrv5");
        }
        finally
        {
            try { File.Delete(pngPath); } catch { /* 清理失败不影响结果 */ }
        }
    }

    private static readonly HttpClient ProxiedHttp = CreateClient(TimeSpan.FromMinutes(5), useProxy: true);
    private static readonly HttpClient DirectHttp = CreateClient(TimeSpan.FromMinutes(5), useProxy: false);

    private const string DownloadUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    private static HttpClient CreateClient(TimeSpan timeout, bool useProxy)
    {
        var handler = new SocketsHttpHandler { UseProxy = useProxy };
        var client = new HttpClient(handler);
        client.Timeout = timeout;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(DownloadUserAgent); // ModelScope 等端点对 GET 要求 UA，否则 403
        return client;
    }

    /// <summary>下载（先走系统代理，SSL/网络失败时直连重试一次）。</summary>
    private static async Task DownloadIfNeededAsync(string targetPath, string url, CancellationToken ct)
    {
        if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var tmp = targetPath + ".download";
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var client = attempt == 0 ? ProxiedHttp : DirectHttp;
            try
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                await using (var fs = File.Create(tmp))
                    await response.Content.CopyToAsync(fs, ct);
                File.Move(tmp, targetPath, overwrite: true);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) when (attempt == 0)
            {
                try { File.Delete(tmp); } catch { }
            }
        }
        throw new HttpRequestException($"模型下载失败：{url}");
    }

    private static async Task DownloadDictIfNeededAsync(string targetPath, CancellationToken ct)
    {
        if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
            return;
        foreach (var url in new[] { DictUrl, DictUrlFallback })
        {
            try
            {
                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(30);
                var text = await http.GetStringAsync(url, ct);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    await File.WriteAllTextAsync(targetPath, text, ct);
                    return;
                }
            }
            catch
            {
                // 换下一个源
            }
        }
        throw new InvalidOperationException("RapidOCR 字典下载失败（Gitee/jsdelivr 均不可达）。");
    }
}
