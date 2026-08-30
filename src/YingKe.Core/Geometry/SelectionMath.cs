namespace YingKe.Core.Geometry;

/// <summary>
/// 选区计算的纯函数集合，便于单元测试。
/// </summary>
public static class SelectionMath
{
    /// <summary>框选有效判定：宽高均不小于该物理像素值。</summary>
    public const int MinimumSizePx = 4;

    public static bool MeetsMinimum(PixelRect rect, int minimum = MinimumSizePx)
        => rect.Width >= minimum && rect.Height >= minimum;

    /// <summary>把矩形完全约束在 bounds 内；尺寸超出时收缩为 bounds，位置越界时贴边。</summary>
    public static PixelRect Clamp(PixelRect rect, PixelRect bounds)
    {
        int width = Math.Min(rect.Width, bounds.Width);
        int height = Math.Min(rect.Height, bounds.Height);
        int x = Math.Clamp(rect.X, bounds.X, bounds.Right - width);
        int y = Math.Clamp(rect.Y, bounds.Y, bounds.Bottom - height);
        return new PixelRect(x, y, width, height);
    }
}
