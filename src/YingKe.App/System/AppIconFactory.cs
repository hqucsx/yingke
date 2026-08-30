using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace YingKe.App;

/// <summary>
/// 运行时绘制托盘图标：蓝色渐变圆角方块 + 白色"映"字，与 exe/安装包图标同款配色。
/// </summary>
public static class AppIconFactory
{
    public static Icon Create()
    {
        using var bitmap = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using var background = new GraphicsPath();
            background.AddArc(2, 2, 20, 20, 180, 90);
            background.AddArc(42, 2, 20, 20, 270, 90);
            background.AddArc(42, 42, 20, 20, 0, 90);
            background.AddArc(2, 42, 20, 20, 90, 90);
            background.CloseFigure();

            using var gradient = new LinearGradientBrush(
                new Rectangle(0, 0, 64, 64),
                Color.FromArgb(0x5B, 0x91, 0xFF),
                Color.FromArgb(0x2B, 0x5B, 0xE0),
                LinearGradientMode.Vertical);
            g.FillPath(gradient, background);

            using var font = new Font("Microsoft YaHei UI", 30f, FontStyle.Bold, GraphicsUnit.Pixel);
            var size = g.MeasureString("映", font);
            using var textBrush = new SolidBrush(Color.White);
            g.DrawString("映", font, textBrush, (64 - size.Width) / 2f, (64 - size.Height) / 2f + 1);
        }

        var hIcon = bitmap.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        var clone = (Icon)icon.Clone();
        DestroyIcon(hIcon);
        return clone;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
