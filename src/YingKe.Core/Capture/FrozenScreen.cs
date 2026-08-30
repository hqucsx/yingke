using YingKe.Core.Geometry;
using YingKe.Core.Native;

namespace YingKe.Core.Capture;

/// <summary>
/// 一次"冻结"的全屏捕获。热键触发时先于任何 UI 拍下整块虚拟屏幕，
/// 之后的框选、取色、裁剪全部发生在这份拷贝上，
/// 因此遮罩与工具栏永远不会污染截图结果（对应 PRD F-08）。
/// </summary>
public sealed class FrozenScreen : IDisposable
{
    private bool _disposed;

    public FrozenScreen(System.Drawing.Bitmap bitmap, PixelRect virtualBounds, uint systemDpi)
    {
        Bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        VirtualBounds = virtualBounds;
        SystemDpi = systemDpi == 0 ? 96 : systemDpi;
    }

    /// <summary>虚拟屏幕在物理像素坐标系下的范围（多显示器时 X/Y 可能为负）。</summary>
    public PixelRect VirtualBounds { get; }

    /// <summary>系统 DPI（主显示器），用于物理像素与 WPF DIP 的换算。</summary>
    public uint SystemDpi { get; }

    /// <summary>物理像素与 DIP 的换算比，例如 125% 时为 1.25。</summary>
    public double Scale => SystemDpi / 96.0;

    public System.Drawing.Bitmap Bitmap { get; }

    /// <summary>底图是否已释放（Dispose 后遮罩层应自愈关闭，避免鼠标路径访问已释放位图）。</summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// 按物理像素裁剪，坐标相对虚拟屏幕原点；与位图不相交时抛异常，部分越界时取交集。
    /// </summary>
    public System.Drawing.Bitmap Crop(PixelRect physicalRect)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var clipped = physicalRect.Intersect(new PixelRect(0, 0, Bitmap.Width, Bitmap.Height));
        if (clipped.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(physicalRect), "裁剪区域与位图不相交。");

        var result = new System.Drawing.Bitmap(clipped.Width, clipped.Height);
        using var g = System.Drawing.Graphics.FromImage(result);
        g.DrawImage(Bitmap, new System.Drawing.Rectangle(0, 0, clipped.Width, clipped.Height),
            new System.Drawing.Rectangle(clipped.X, clipped.Y, clipped.Width, clipped.Height),
            System.Drawing.GraphicsUnit.Pixel);
        return result;
    }

    private int[]? _pixelCache;

    /// <summary>O(1) 像素读取：GDI GetPixel 逐点调用极慢（放大镜每次移动都会调）。</summary>
    public System.Drawing.Color GetPixel(int physicalX, int physicalY)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (physicalX < 0 || physicalY < 0 || physicalX >= Bitmap.Width || physicalY >= Bitmap.Height)
            return System.Drawing.Color.Empty;

        if (_pixelCache == null || _pixelCache.Length != Bitmap.Width * Bitmap.Height)
        {
            var rect = new System.Drawing.Rectangle(0, 0, Bitmap.Width, Bitmap.Height);
            var data = Bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                _pixelCache = new int[Bitmap.Width * Bitmap.Height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, _pixelCache, 0, _pixelCache.Length);
            }
            finally
            {
                Bitmap.UnlockBits(data);
            }
        }

        var v = _pixelCache[physicalY * Bitmap.Width + physicalX];
        return System.Drawing.Color.FromArgb((v >> 24) & 0xFF, (v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
    }

    public void SavePng(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Bitmap.Dispose();
    }
}
