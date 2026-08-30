using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace YingKe.Core.Media;

/// <summary>
/// 标注用的位图效果（PRD F-29）：马赛克/模糊供标注笔刷采样，反色/灰度供钉图滤镜。
/// 全部返回新位图，不改源图。
/// </summary>
public static class ImageEffects
{
    /// <summary>马赛克：分块取平均色（最近邻降采样后再放大）。</summary>
    public static Bitmap Mosaic(Bitmap source, int blockSize = 14)
    {
        ArgumentNullException.ThrowIfNull(source);
        int smallW = Math.Max(1, source.Width / blockSize);
        int smallH = Math.Max(1, source.Height / blockSize);

        using var small = new Bitmap(smallW, smallH);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(source, new Rectangle(0, 0, smallW, smallH),
                new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
        }

        return Resize(small, source.Width, source.Height, InterpolationMode.NearestNeighbor);
    }

    /// <summary>模糊：高质量降采样再放大（盒式近似，速度快、效果自然）。</summary>
    public static Bitmap Blur(Bitmap source, int strength = 12)
    {
        ArgumentNullException.ThrowIfNull(source);
        int smallW = Math.Max(1, source.Width / strength);
        int smallH = Math.Max(1, source.Height / strength);

        using var small = new Bitmap(smallW, smallH);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, new Rectangle(0, 0, smallW, smallH),
                new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
        }

        return Resize(small, source.Width, source.Height, InterpolationMode.HighQualityBicubic);
    }

    /// <summary>反色（钉图滤镜）。</summary>
    public static Bitmap Invert(Bitmap source)
    {
        // R'G'B' = 255 - RGB
        var matrix = new ColorMatrix(new[]
        {
            new[] { -1f, 0f, 0f, 0f, 0f },
            new[] { 0f, -1f, 0f, 0f, 0f },
            new[] { 0f, 0f, -1f, 0f, 0f },
            new[] { 0f, 0f, 0f, 1f, 0f },
            new[] { 1f, 1f, 1f, 0f, 1f },
        });
        return ApplyColorMatrix(source, matrix);
    }

    /// <summary>灰度（钉图滤镜，Rec.601 亮度加权）。</summary>
    public static Bitmap ToGrayscale(Bitmap source)
    {
        var matrix = new ColorMatrix(new[]
        {
            new[] { 0.299f, 0.299f, 0.299f, 0f, 0f },
            new[] { 0.587f, 0.587f, 0.587f, 0f, 0f },
            new[] { 0.114f, 0.114f, 0.114f, 0f, 0f },
            new[] { 0f, 0f, 0f, 1f, 0f },
            new[] { 0f, 0f, 0f, 0f, 1f },
        });
        return ApplyColorMatrix(source, matrix);
    }

    private static Bitmap ApplyColorMatrix(Bitmap source, ColorMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new Bitmap(source.Width, source.Height);
        using var g = Graphics.FromImage(result);
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix);
        g.DrawImage(source,
            new Rectangle(0, 0, source.Width, source.Height),
            0, 0, source.Width, source.Height,
            GraphicsUnit.Pixel, attributes);
        return result;
    }

    private static Bitmap Resize(Bitmap source, int width, int height, InterpolationMode mode)
    {
        var result = new Bitmap(width, height);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = mode;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(source, new Rectangle(0, 0, width, height),
            new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
        return result;
    }
}
