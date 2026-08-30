using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using YingKe.Core.Ocr;
using Xunit;

namespace YingKe.Core.Tests;

/// <summary>
/// 真实 Windows.Media.Ocr 集成测试：渲染文字位图 → 识别 → 断言。
/// 机器未装任何 OCR 语言包时静默通过（CI/裸机环境保护）。
/// </summary>
public class WindowsOcrEngineTests
{
    [Fact]
    public async Task RecognizeAsync_ReadsRenderedLatinText()
    {
        if (!WindowsOcrEngine.IsAvailable())
            return; // 无 OCR 语言包的环境直接通过

        using var bitmap = RenderText("Ta OCR 2026", width: 420, height: 100);
        var engine = new WindowsOcrEngine();
        var result = await engine.RecognizeAsync(bitmap);

        Assert.False(string.IsNullOrWhiteSpace(result.Text));
        Assert.Contains("Ta", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Windows 内置 OCR", result.EngineName);
        Assert.NotEmpty(result.Lines);
    }

    [Fact]
    public async Task RecognizeAsync_HandlesLargeImageByScaling()
    {
        if (!WindowsOcrEngine.IsAvailable())
            return;

        // 超大图（含边框空白）应被等比缩小后仍能识别
        using var bitmap = RenderText("HELLO", width: 9000, height: 2000);
        var engine = new WindowsOcrEngine();
        var result = await engine.RecognizeAsync(bitmap);
        Assert.Contains("HELLO", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    private static Bitmap RenderText(string text, int width, int height)
    {
        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.White);

        float fontSize = Math.Min(height * 0.6f, width / (text.Length * 0.62f));
        using var font = new Font("Microsoft YaHei UI", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.Black);
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, brush, (width - size.Width) / 2f, (height - size.Height) / 2f);
        return bitmap;
    }
}
