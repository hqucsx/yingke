using YingKe.Core.Geometry;
using Xunit;

namespace YingKe.Core.Tests;

public class SelectionMathTests
{
    [Theory]
    [InlineData(10, 20, 110, 120, 10, 20, 100, 100)]   // 左上 → 右下
    [InlineData(110, 120, 10, 20, 10, 20, 100, 100)]   // 右下 → 左上
    [InlineData(110, 20, 10, 120, 10, 20, 100, 100)]   // 右上 → 左下
    [InlineData(10, 120, 110, 20, 10, 20, 100, 100)]   // 左下 → 右上
    [InlineData(50, 50, 50, 150, 50, 50, 0, 100)]      // 垂直线
    public void FromPoints_NormalizesAnyDragDirection(
        int x1, int y1, int x2, int y2,
        int ex, int ey, int ew, int eh)
    {
        var rect = PixelRect.FromPoints(x1, y1, x2, y2);
        Assert.Equal(new PixelRect(ex, ey, ew, eh), rect);
    }

    [Fact]
    public void MeetsMinimum_RejectsTinySelections()
    {
        Assert.True(SelectionMath.MeetsMinimum(new PixelRect(0, 0, 4, 4)));
        Assert.False(SelectionMath.MeetsMinimum(new PixelRect(0, 0, 3, 4)));
        Assert.False(SelectionMath.MeetsMinimum(new PixelRect(0, 0, 4, 0)));
    }

    [Fact]
    public void Clamp_KeepsRectInsideBounds()
    {
        var bounds = new PixelRect(0, 0, 1920, 1080);

        // 越出右下边界 → 贴边
        var overshoot = SelectionMath.Clamp(new PixelRect(1900, 1070, 100, 100), bounds);
        Assert.Equal(new PixelRect(1820, 980, 100, 100), overshoot);

        // 负坐标 → 收敛到 0
        var negative = SelectionMath.Clamp(new PixelRect(-50, -50, 200, 200), bounds);
        Assert.Equal(new PixelRect(0, 0, 200, 200), negative);

        // 尺寸超过 bounds → 收缩为 bounds
        var tooBig = SelectionMath.Clamp(new PixelRect(-10, -10, 4000, 4000), bounds);
        Assert.Equal(bounds, tooBig);
    }

    [Fact]
    public void Intersect_ReturnsOverlappingRegion()
    {
        var a = new PixelRect(0, 0, 100, 100);
        var b = new PixelRect(50, 50, 100, 100);
        Assert.Equal(new PixelRect(50, 50, 50, 50), a.Intersect(b));

        var disjoint = new PixelRect(200, 200, 10, 10);
        Assert.True(a.Intersect(disjoint).IsEmpty);
    }
}
