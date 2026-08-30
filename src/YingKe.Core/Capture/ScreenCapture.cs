using YingKe.Core.Geometry;
using YingKe.Core.Native;

namespace YingKe.Core.Capture;

/// <summary>
/// M1 里程碑使用 GDI 捕获（CopyFromScreen），全平台可用、无需授权。
/// Windows.Graphics.Capture（窗口捕获/排除自身窗口）按 PRD 规划在后续里程碑接入，
/// 届时本类保持接口不变，仅替换实现。
/// </summary>
public static class ScreenCapture
{
    public static FrozenScreen CaptureVirtualScreen()
    {
        var bounds = GetVirtualScreenBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("无法获取虚拟屏幕尺寸。");

        var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0,
                new System.Drawing.Size(bounds.Width, bounds.Height),
                System.Drawing.CopyPixelOperation.SourceCopy);
        }

        return new FrozenScreen(bitmap, bounds, NativeMethods.GetDpiForSystem());
    }

    public static PixelRect GetVirtualScreenBounds()
        => new(
            NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));
}
