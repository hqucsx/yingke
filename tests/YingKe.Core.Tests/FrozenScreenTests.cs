using YingKe.Core.Capture;
using YingKe.Core.Geometry;
using Xunit;

namespace YingKe.Core.Tests;

public class FrozenScreenTests
{
    private static FrozenScreen CreateSample(int width = 100, int height = 80)
    {
        var bitmap = new System.Drawing.Bitmap(width, height);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
            g.Clear(System.Drawing.Color.FromArgb(0xAB, 0xCD, 0xEF));

        // 在 (30,40)-(59,59) 画一块红色标记，用于验证裁剪位置正确
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Red))
            g.FillRectangle(brush, 30, 40, 30, 20);

        return new FrozenScreen(bitmap, new PixelRect(0, 0, width, height), systemDpi: 96);
    }

    [Fact]
    public void Crop_ReturnsPixelsAtRequestedPhysicalCoordinates()
    {
        using var screen = CreateSample();
        using var cropped = screen.Crop(new PixelRect(30, 40, 30, 20));

        Assert.Equal(30, cropped.Width);
        Assert.Equal(20, cropped.Height);
        // GDI+ 命名色与位图读回的 ARGB 值需按数值比较
        Assert.Equal(System.Drawing.Color.Red.ToArgb(), cropped.GetPixel(0, 0).ToArgb());
        Assert.Equal(System.Drawing.Color.Red.ToArgb(), cropped.GetPixel(29, 19).ToArgb());
    }

    [Fact]
    public void Crop_ClampsRectInsideBitmap()
    {
        using var screen = CreateSample();

        // 越出右下角：位置贴边、尺寸收缩，不抛异常
        using var cropped = screen.Crop(new PixelRect(90, 70, 50, 50));
        Assert.Equal(new System.Drawing.Rectangle(0, 0, 10, 10), new System.Drawing.Rectangle(0, 0, cropped.Width, cropped.Height));
    }

    [Fact]
    public void Crop_ThrowsWhenRectDisjointFromBitmap()
    {
        using var screen = CreateSample();
        Assert.Throws<ArgumentOutOfRangeException>(() => screen.Crop(new PixelRect(500, 500, 10, 10)));
    }

    [Fact]
    public void Scale_ConvertsDpiCorrectly()
    {
        using var screen = new FrozenScreen(new System.Drawing.Bitmap(10, 10),
            new PixelRect(0, 0, 10, 10), systemDpi: 120);
        Assert.Equal(1.25, screen.Scale, precision: 6);
    }

    [Fact]
    public void GetPixel_ReturnsEmptyOutsideBitmap()
    {
        using var screen = CreateSample();
        Assert.Equal(System.Drawing.Color.Empty, screen.GetPixel(-1, 0));
        Assert.Equal(System.Drawing.Color.Empty, screen.GetPixel(0, 999));
        Assert.Equal(System.Drawing.Color.FromArgb(0xAB, 0xCD, 0xEF), screen.GetPixel(0, 0));
    }
}
