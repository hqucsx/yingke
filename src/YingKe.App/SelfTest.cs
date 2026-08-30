using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using YingKe.App.Capture;
using YingKe.App.Media;
using YingKe.Core.Configuration;
using YingKe.Core.Geometry;
using WmfColor = System.Windows.Media.Color;
using WmfPoint = System.Windows.Point;
using WmfSize = System.Windows.Size;

namespace YingKe.App;

/// <summary>
/// --selftest：程序内直接驱动标注管线（换色矩形/马赛克/放大镜）→ RTB 压平导出 → 像素断言。
/// --apitest：用真实配置走一次带图 Provider 调用（连通性验证）。
/// 结果写 %TEMP%\ta-selftest.png / ta-selftest-result.txt / ta-apitest-result.txt。
/// </summary>
public static class SelfTest
{
    public static void Run(AppConfig config)
    {
        var lines = new List<string>();
        try
        {
            // 合成底图（不依赖屏幕内容，测试完全确定性）：白底 + 灰色网格 + 深色文字块
            using var crop = CreateSyntheticBase();
            double scale = 1.0;
            double w = crop.Width;
            double h = crop.Height;

            // 复刻 OverlayWindow 的宿主结构：host(定位层) → renderRoot(零偏移) → 底图+图形
            var host = new Canvas { Width = w, Height = h };
            var root = new Canvas { Width = w, Height = h };
            host.Children.Add(root);
            var baseImage = new System.Windows.Controls.Image
            {
                Source = BitmapConversion.ToBitmapSource(crop),
                Width = w,
                Height = h,
                Stretch = Stretch.Fill,
            };
            root.Children.Add(baseImage);
            var shapes = new Canvas();
            root.Children.Add(shapes);
            host.Measure(new WmfSize(double.PositiveInfinity, double.PositiveInfinity));
            host.Arrange(new Rect(0, 0, w, h));

            // 挂到屏外隐藏窗口：游离视觉树里 WPF Shape 不会被 RTB 渲染，
            // 生产路径（遮罩窗口内）行为一致，这里保持同构
            var offscreen = new Window
            {
                Width = w + 20,
                Height = h + 20,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Content = host,
                Left = -20000,
                Top = -20000,
            };
            offscreen.Show();

            var controller = new AnnotationController(shapes, crop, scale, w, h);

            // 1) 蓝色矩形（颜色可调验证）
            controller.SetStrokeColor(WmfColor.FromRgb(0x00, 0x64, 0xC8));
            controller.SetTool(AnnotationTool.Rectangle);
            controller.Begin(new WmfPoint(50, 50));
            controller.Move(new WmfPoint(200, 130));
            controller.End(new WmfPoint(200, 130));

            // 2) 马赛克（效果生效验证）
            controller.SetTool(AnnotationTool.Mosaic);
            controller.Begin(new WmfPoint(220, 40));
            controller.Move(new WmfPoint(360, 160));
            controller.End(new WmfPoint(360, 160));

            // 3) 放大镜（效果生效验证）
            controller.SetTool(AnnotationTool.Magnifier);
            controller.Begin(new WmfPoint(110, 180));
            controller.End(new WmfPoint(110, 180));

            // 添加图形后同步冲刷布局：RTB 只渲染已布局的视觉，否则形状停留在零尺寸状态
            host.UpdateLayout();

            using var exported = AnnotationController.FlattenToBitmap(root, scale)
                ?? throw new InvalidOperationException("压平导出返回 null");
            var outPng = Path.Combine(Path.GetTempPath(), "ta-selftest.png");
            exported.Save(outPng, System.Drawing.Imaging.ImageFormat.Png);
            lines.Add($"export: {exported.Width}x{exported.Height} -> {outPng}");
            lines.Add($"shapeCount: {controller.ShapeCount} (expect 3)");

            // 断言 1：蓝矩形描边出现在 (50,50)-(200,130) 区域
            bool blueFound = Scan(exported, 45, 45, 210, 135, p => p.B > 150 && p.R < 120 && p.G < 160);
            lines.Add($"blue-rect stroke found: {blueFound}");

            // 断言 2：马赛克语义验证——每个马赛克块的平均色 ≈ 底图对应区域的平均色，
            // 且整体区别于原始底图。（GDI 马赛克与 WPF 画刷的块边界插值不同，
            // 逐像素比对会低估匹配率，因此按块平均比对。）
            double blockMatchRatio = RegionBlockMatch(exported, crop, 220, 40, 360, 160, scale, blockSize: 14);
            bool differsFromBase = RegionDiffers(exported, crop, 220, 40, 360, 160, scale);
            lines.Add($"mosaic block-average match: {blockMatchRatio:P1} (expect >= 80%)");
            lines.Add($"mosaic differs from base: {differsFromBase} (expect True)");
            bool mosaicOk = blockMatchRatio >= 0.8 && differsFromBase;

            // 断言 3：放大镜圆形区域不透明（有放大内容）
            bool magnifierOpaque = Scan(exported, 75, 145, 145, 215, p => p.A > 200);
            lines.Add($"magnifier circle opaque: {magnifierOpaque}");

            lines.Add(blueFound && mosaicOk && magnifierOpaque && controller.ShapeCount == 3
                ? "SELFTEST PASS"
                : "SELFTEST FAIL");
            offscreen.Close();
        }
        catch (Exception ex)
        {
            lines.Add("SELFTEST ERROR: " + ex);
        }

        var resultPath = Path.Combine(Path.GetTempPath(), "ta-selftest-result.txt");
        File.WriteAllLines(resultPath, lines);
        Console.WriteLine(string.Join(Environment.NewLine, lines));
    }

    /// <summary>--translatetest：合成图 → 本地 OCR → 内置翻译（Google）全链路真实验证。</summary>
    public static void RunTranslate(AppConfig config)
    {
        string[] lines;
        try
        {
            lines = System.Threading.Tasks.Task.Run(() => RunTranslateCoreAsync(config)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            lines = new[] { "TRANSLATETEST FAIL: " + ex.Message };
        }
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "ta-translatetest-result.txt"), lines);
        Console.WriteLine(string.Join(Environment.NewLine, lines));
    }

    private static async Task<string[]> RunTranslateCoreAsync(AppConfig config)
    {
        var lines = new List<string>();
        using var baseImage = CreateSyntheticBase();
        var ocr = await new YingKe.Core.Ocr.WindowsOcrEngine().RecognizeAsync(baseImage);
        lines.Add($"ocr({ocr.EngineName}): {Truncate(ocr.Text, 80)}");
        if (string.IsNullOrWhiteSpace(ocr.Text))
            throw new InvalidOperationException("OCR 未识别到文字");

        var translated = await YingKe.Core.Translation.BuiltInTranslator.TranslateAsync(
            ocr.Text, config.Translation.TargetLanguage);
        lines.Add($"translated({translated.Length} chars): {Truncate(translated, 160)}");
        if (string.IsNullOrWhiteSpace(translated))
            throw new InvalidOperationException("翻译结果为空");
        lines.Add("TRANSLATETEST PASS");
        return lines.ToArray();
    }

    /// <summary>--rapidtest：合成图 → RapidOCR 离线识别全链路真实验证（首跑自动下载中文模型）。</summary>
    public static void RunRapid(AppConfig config)
    {
        string[] lines;
        try
        {
            lines = System.Threading.Tasks.Task.Run(() => RunRapidCoreAsync(config)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            lines = new[] { "RAPIDTEST FAIL: " + ex.Message };
        }
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "ta-rapidtest-result.txt"), lines);
        Console.WriteLine(string.Join(Environment.NewLine, lines));
    }

    private static async Task<string[]> RunRapidCoreAsync(AppConfig config)
    {
        var lines = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        using var baseImage = CreateSyntheticBase();
        var engine = new YingKe.Core.Ocr.RapidOcrEngine();
        var result = await engine.RecognizeAsync(baseImage);
        lines.Add($"engine: {result.EngineName} · {stopwatch.ElapsedMilliseconds}ms");
        lines.Add($"text({result.Text.Length} chars): {Truncate(result.Text, 160)}");
        if (string.IsNullOrWhiteSpace(result.Text))
            throw new InvalidOperationException("RapidOCR 识别结果为空");
        lines.Add("RAPIDTEST PASS");
        return lines.ToArray();
    }

    /// <summary>用真实配置走一次带图 Provider 调用（等价于 App 内 AI 识图的请求路径）。</summary>
    public static void RunApi(AppConfig config)
    {
        string[] lines;
        try
        {
            // 线程池上执行（内部无 UI 依赖），UI 线程阻塞等待不会死锁
            lines = System.Threading.Tasks.Task.Run(() => RunApiCoreAsync(config)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            lines = new[] { "APITEST FAIL: " + ex.Message };
        }
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "ta-apitest-result.txt"), lines);
        Console.WriteLine(string.Join(Environment.NewLine, lines));
    }

    private static async Task<string[]> RunApiCoreAsync(AppConfig config)
    {
        var lines = new List<string>
        {
            $"provider: {config.Ai.Provider} · {config.Ai.Model} · {config.Ai.BaseUrl}",
        };
        var provider = YingKe.Core.Recognition.ProviderFactory.FromConfig(config)
            ?? throw new InvalidOperationException("未配置 API Key（凭据管理器中无 Ta/ai.apikey）");
        lines.Add($"resolved: {provider.Name}");

        using var image = CreateSyntheticBase();
        var text = await provider.ChatAsync(YingKe.Core.Recognition.PromptTemplates.ExtractText, null, image);
        lines.Add($"response({text.Length} chars): {Truncate(text, 200)}");
        lines.Add("APITEST PASS");
        return lines.ToArray();
    }

    private static string Truncate(string text, int max)
        => string.IsNullOrEmpty(text) || text.Length <= max ? text : text[..max] + "…";

    private static bool Scan(Bitmap bmp, int x1, int y1, int x2, int y2, Func<System.Drawing.Color, bool> predicate)
    {
        for (int y = y1; y <= Math.Min(y2, bmp.Height - 1); y++)
            for (int x = x1; x <= Math.Min(x2, bmp.Width - 1); x++)
                if (predicate(bmp.GetPixel(x, y)))
                    return true;
        return false;
    }

    /// <summary>按块平均色比较：export 的每块平均色 vs base 对应物理区域的平均色。</summary>
    private static double RegionBlockMatch(Bitmap exported, Bitmap baseCrop,
        int x1, int y1, int x2, int y2, double scale, int blockSize)
    {
        int blocks = 0;
        int matched = 0;
        for (int by = y1; by < y2; by += blockSize)
        {
            for (int bx = x1; bx < x2; bx += blockSize)
            {
                int ex2 = Math.Min(bx + blockSize, x2);
                int ey2 = Math.Min(by + blockSize, y2);
                var exportAvg = AverageColor(exported, bx, by, ex2, ey2);
                int sx = (int)(bx * scale);
                int sy = (int)(by * scale);
                int sw = Math.Max(1, (int)((ex2 - bx) * scale));
                int sh = Math.Max(1, (int)((ey2 - by) * scale));
                var baseAvg = AverageColor(baseCrop, sx, sy, Math.Min(sx + sw, baseCrop.Width - 1), Math.Min(sy + sh, baseCrop.Height - 1));
                int diff = Math.Abs(exportAvg.R - baseAvg.R) + Math.Abs(exportAvg.G - baseAvg.G) + Math.Abs(exportAvg.B - baseAvg.B);
                blocks++;
                if (diff < 60) matched++; // 容忍量化/插值噪声
            }
        }
        return blocks == 0 ? 0 : (double)matched / blocks;
    }

    private static (int R, int G, int B) AverageColor(Bitmap bmp, int x1, int y1, int x2, int y2)
    {
        long r = 0, g = 0, b = 0;
        int n = 0;
        for (int y = y1; y <= y2; y += 2)
            for (int x = x1; x <= x2; x += 2)
            {
                var c = bmp.GetPixel(x, y);
                r += c.R; g += c.G; b += c.B;
                n++;
            }
        n = Math.Max(1, n);
        return ((int)(r / n), (int)(g / n), (int)(b / n));
    }

    private static double RegionMatchRatio(Bitmap exported, Bitmap expected, int x1, int y1, int x2, int y2)
    {
        int total = 0;
        int matched = 0;
        for (int y = y1; y <= Math.Min(y2, exported.Height - 1); y += 2)
            for (int x = x1; x <= Math.Min(x2, exported.Width - 1); x += 2)
            {
                total++;
                if (exported.GetPixel(x, y).ToArgb() == expected.GetPixel(x, y).ToArgb())
                    matched++;
            }
        return total == 0 ? 0 : (double)matched / total;
    }

    private static bool RegionDiffers(Bitmap exported, Bitmap baseCrop, int x1, int y1, int x2, int y2, double scale)
    {
        int sx1 = (int)(x1 * scale);
        int sy1 = (int)(y1 * scale);
        for (int y = y1; y <= Math.Min(y2, exported.Height - 1); y += 4)
            for (int x = x1; x <= Math.Min(x2, exported.Width - 1); x += 4)
            {
                int bx = Math.Min(sx1 + (x - x1), baseCrop.Width - 1);
                int by = Math.Min(sy1 + (y - y1), baseCrop.Height - 1);
                if (exported.GetPixel(x, y).ToArgb() != baseCrop.GetPixel(bx, by).ToArgb())
                    return true;
            }
        return false;
    }

    /// <summary>合成底图：白底 + 网格 + 文字，内容固定，保证断言确定性。</summary>
    private static Bitmap CreateSyntheticBase()
    {
        var bitmap = new Bitmap(400, 240);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.White);

        using (var gridPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(230, 230, 235)))
        {
            for (int x = 0; x < 400; x += 20) g.DrawLine(gridPen, x, 0, x, 240);
            for (int y = 0; y < 240; y += 20) g.DrawLine(gridPen, 0, y, 400, y);
        }

        using var font = new System.Drawing.Font("Microsoft YaHei UI", 22f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(System.Drawing.Color.FromArgb(40, 40, 45));
        g.DrawString("映刻 标注自检 123", font, brush, 16, 20);
        g.DrawString("Mosaic / Magnifier", font, brush, 16, 60);
        return bitmap;
    }
}
