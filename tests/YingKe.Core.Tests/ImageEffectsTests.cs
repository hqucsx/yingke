using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using YingKe.Core.Media;
using Xunit;

namespace YingKe.Core.Tests;

public class ImageEffectsTests
{
    private static Bitmap CreateSample()
    {
        var bitmap = new Bitmap(80, 60);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.White);
        using var font = new Font("Microsoft YaHei UI", 24f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.Black);
        g.DrawString("Ta 测试", font, brush, 8, 14);
        return bitmap;
    }

    [Theory]
    [InlineData("Mosaic")]
    [InlineData("Blur")]
    public void Effects_PreserveDimensionsAndChangePixels(string effect)
    {
        using var source = CreateSample();
        using var result = effect == "Mosaic" ? ImageEffects.Mosaic(source) : ImageEffects.Blur(source);

        Assert.Equal(source.Width, result.Width);
        Assert.Equal(source.Height, result.Height);
        Assert.NotEqual(0, CountChangedPixels(source, result));
    }

    [Fact]
    public void Invert_TurnsWhiteToBlack()
    {
        using var source = new Bitmap(10, 10);
        using (var g = Graphics.FromImage(source))
            g.Clear(Color.White);

        using var result = ImageEffects.Invert(source);
        Assert.Equal(Color.Black.ToArgb(), result.GetPixel(5, 5).ToArgb());
    }

    [Fact]
    public void Grayscale_MakesChannelsEqual()
    {
        using var source = new Bitmap(10, 10);
        using (var g = Graphics.FromImage(source))
            g.Clear(Color.FromArgb(255, 200, 30, 90));

        using var result = ImageEffects.ToGrayscale(source);
        var pixel = result.GetPixel(5, 5);
        Assert.Equal(pixel.R, pixel.G);
        Assert.Equal(pixel.G, pixel.B);
    }

    private static int CountChangedPixels(Bitmap a, Bitmap b)
    {
        int changed = 0;
        for (int y = 0; y < a.Height; y += 3)
            for (int x = 0; x < a.Width; x += 3)
                if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb())
                    changed++;
        return changed;
    }
}
