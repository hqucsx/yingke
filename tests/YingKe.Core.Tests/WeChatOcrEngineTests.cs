using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using YingKe.Core.Ocr;
using Xunit;

namespace YingKe.Core.Tests;

public class WeChatOcrEngineTests
{
    /// <summary>
    /// 真实微信 OCR 集成测试（本机需安装微信 PC 版且已下载 OCR 组件；否则静默跳过）。
    /// </summary>
    [Fact]
    public async Task RecognizeAsync_ReadsRenderedChineseText_WhenWeChatAvailable()
    {
        if (!WeChatOcrEngine.IsAvailable())
            return; // 本机未满足微信 OCR 条件（未安装微信或 XPlugin 组件未下载）

        using var bitmap = RenderText("微信 OCR 测试 Ta2026", 420, 120);
        var engine = new WeChatOcrEngine();
        var result = await engine.RecognizeAsync(bitmap);

        Assert.False(string.IsNullOrWhiteSpace(result.Text));
        Assert.Contains("Ta", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("微信 OCR", result.EngineName);
    }

    private static Bitmap RenderText(string text, int width, int height)
    {
        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.White);

        using var font = new Font("Microsoft YaHei UI", 36f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.Black);
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, brush, (width - size.Width) / 2f, (height - size.Height) / 2f);
        return bitmap;
    }
}
