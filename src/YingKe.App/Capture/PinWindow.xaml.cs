using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using YingKe.App.Media;
using YingKe.Core.Files;
using YingKe.Core.Media;
using YingKe.Core.Native;

namespace YingKe.App.Capture;

/// <summary>
/// 钉图窗口（PRD F-31/32/33）：
/// 置顶无边框；滚轮缩放（光标锚点）、Alt+滚轮透明度、拖动移动、双击关闭；
/// 右键菜单：复制/保存/旋转/滤镜/鼠标穿透/关闭。
/// 穿透后窗口不再接收鼠标，由托盘菜单统一管理（取消穿透/关闭全部）。
/// </summary>
public partial class PinWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x00000020;
    private const long WS_EX_LAYERED = 0x00080000;
    private const long WS_EX_TOOLWINDOW = 0x00000080;

    private const double BorderPadding = 10;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 5.0;

    // 钉图只保留一份像素（WPF BitmapSource）。
    // 之前同时持有 GDI 原图 + WPF 副本两份解码位图；滤镜/落盘改为按需临时物化 GDI 位图，
    // 常驻内存减半。WebP 等压缩格式只影响文件体积，解码后的内存不变。
    private BitmapSource _source;
    private double _zoom = 1.0;
    private int _rotation; // 0/90/180/270
    private string _filterName = "原图";
    private bool _clickThrough;
    private bool _dragging;
    private Point _dragStartPosition;

    public PinWindow(System.Drawing.Bitmap image, double screenXDip, double screenYDip)
    {
        InitializeComponent();
        _source = BitmapConversion.ToBitmapSource(image);
        image.Dispose(); // 像素已复制进 WPF，GDI 副本立即释放
        PinImage.Source = _source;
        ApplySize();
        Left = screenXDip - BorderPadding;
        Top = screenYDip - BorderPadding;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
        Closed += (_, _) => _source = null;
    }

    private double ImageWidthDip => (_rotation % 180 == 0 ? _source.PixelWidth : _source.PixelHeight) * _zoom;
    private double ImageHeightDip => (_rotation % 180 == 0 ? _source.PixelHeight : _source.PixelWidth) * _zoom;

    private void ApplySize()
    {
        PinImage.LayoutTransform = _rotation == 0 ? Transform.Identity : new RotateTransform(_rotation);
        Width = ImageWidthDip + BorderPadding * 2;
        Height = ImageHeightDip + BorderPadding * 2;
    }

    // ---- 交互 ----

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Close();
            return;
        }
        _dragging = true;
        _dragStartPosition = e.GetPosition(this);
        CaptureMouse();
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging && e.LeftButton == MouseButtonState.Pressed)
        {
            var p = e.GetPosition(this);
            Left += p.X - _dragStartPosition.X;
            Top += p.Y - _dragStartPosition.Y;
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _dragging = false;
        ReleaseMouseCapture();
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            Opacity = Math.Clamp(Opacity + Math.Sign(e.Delta) * 0.1, 0.05, 1.0);
        }
        else
        {
            var oldZoom = _zoom;
            _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), MinZoom, MaxZoom);
            if (Math.Abs(_zoom - oldZoom) > 0.0001)
            {
                // 以光标为锚点缩放：光标下的图像点保持不动
                var cursor = e.GetPosition(this);
                double ratio = _zoom / oldZoom;
                Left += cursor.X * (1 - ratio);
                Top += cursor.Y * (1 - ratio);
                ApplySize();
            }
        }
        e.Handled = true;
        base.OnMouseWheel(e);
    }

    // ---- 右键菜单 ----

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };

        Add(menu, "复制图片", (_, _) => CopyToClipboard());
        Add(menu, "保存…", (_, _) => SaveToFile());
        menu.Items.Add(new Separator());
        Add(menu, "旋转 90°", (_, _) => { _rotation = (_rotation + 90) % 360; ApplySize(); });
        menu.Items.Add(new Separator());
        foreach (var (name, apply) in new (string, Func<System.Drawing.Bitmap>)[]
                 {
                     ("原图", () => Materialize()), // SetFilter 内部转换后即释放
                     ("灰度", () => { using var g = Materialize(); return ImageEffects.ToGrayscale(g); }),
                     ("反色", () => { using var g = Materialize(); return ImageEffects.Invert(g); }),
                 })
        {
            Add(menu, (_filterName == name ? "● " : "○ ") + name, (_, _) => apply());
        }
        menu.Items.Add(new Separator());
        Add(menu, _clickThrough ? "取消鼠标穿透" : "鼠标穿透", (_, _) => SetClickThrough(!_clickThrough));
        Add(menu, "关闭钉图", (_, _) => Close());

        ContextMenu = menu;
    }

    private static MenuItem Add(ContextMenu menu, string header, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header };
        item.Click += handler;
        menu.Items.Add(item);
        return item;
    }

    /// <summary>把当前显示位图按需物化为 GDI 位图（调用方负责 Dispose）。</summary>
    private System.Drawing.Bitmap Materialize() => BitmapConversion.ToBitmap(_source);

    private void SetFilter(string name, System.Drawing.Bitmap bitmap)
    {
        using (bitmap) // 滤镜结果先接管再转 WPF，之后只保留 WPF 副本
        {
            _source = BitmapConversion.ToBitmapSource(bitmap);
        }
        _filterName = name;
        PinImage.Source = _source;
        ApplySize();
    }

    private void CopyToClipboard()
    {
        try
        {
            ClipboardHelper.Gate.Wait();
            try { ClipboardHelper.SetImage(_source); }
            finally { ClipboardHelper.Gate.Release(); }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"复制失败：{ex.Message}", "映刻 钉图", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveToFile()
    {
        var dialog = new SaveFileDialog
        {
            FileName = SavePathGenerator.DefaultFileName(DateTime.Now),
            InitialDirectory = SavePathGenerator.DefaultDirectory(),
            Filter = "PNG 图片|*.png|JPEG 图片|*.jpg",
            DefaultExt = "png",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            using var gdi = Materialize();
            if (dialog.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                dialog.FileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                gdi.Save(dialog.FileName, ImageFormat.Jpeg);
            else
                gdi.Save(dialog.FileName, ImageFormat.Png);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存失败：{ex.Message}", "映刻 钉图", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---- 鼠标穿透（PRD F-32） ----

    public void SetClickThrough(bool enabled)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        if (enabled)
            style |= (int)(WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
        else
            style &= ~(int)(WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
        SetWindowLong(hwnd, GWL_EXSTYLE, style);
        _clickThrough = enabled;
    }

    public bool IsClickThrough => _clickThrough;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

    private static int GetWindowLong(IntPtr hwnd, int index) => GetWindowLong32(hwnd, index);

    private static void SetWindowLong(IntPtr hwnd, int index, int value) => SetWindowLong32(hwnd, index, value);
}
