using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using YingKe.App.Media;
using YingKe.Core.Media;

namespace YingKe.App.Capture;

public enum AnnotationTool
{
    None,
    Rectangle,
    Ellipse,
    Arrow,
    Pen,
    Text,
    Number,
    Mosaic,
    Blur,
    Magnifier,
}

/// <summary>
/// 原位标注控制器（PRD F-29/F-30）：在选区上叠加图形，坐标原点 = 选区左上角（DIP）。
/// 马赛克/模糊/放大镜用 ImageBrush 绝对坐标采样预处理位图实现；
/// 导出由 OverlayWindow 用 RenderTargetBitmap 把 底图+图形 压平为物理像素。
/// </summary>
public sealed class AnnotationController
{
    private const double StrokeWidth = 3;
    private const double MagnifierDiameter = 84;
    private const double MagnifierZoom = 2.5;

    private static readonly Brush WhiteStroke = new SolidColorBrush(Colors.White);

    private readonly Canvas _canvas;
    private readonly double _scale;
    private readonly double _width;   // 画布 DIP 尺寸
    private readonly double _height;
    private readonly BitmapSource _baseCrop;
    private readonly BitmapSource _mosaicCrop;
    private readonly BitmapSource _blurCrop;
    private readonly List<UIElement> _shapes = new();

    private SolidColorBrush _strokeBrush = new(Color.FromRgb(0xFF, 0x3B, 0x30));
    private UIElement? _draft;
    private Point _start;
    private TextBox? _textEditor;
    private BitmapSource? _pixelSource; // 马赛克/模糊草稿的采样源
    private int _nextNumber = 1;

    public AnnotationController(Canvas canvas, System.Drawing.Bitmap baseCropPhysical, double scale, double widthDip, double heightDip, IEnumerable<UIElement>? adoptedShapes = null)
    {
        _canvas = canvas;
        _scale = scale;
        _width = widthDip;
        _height = heightDip;
        _baseCrop = BitmapConversion.ToBitmapSource(baseCropPhysical);
        _mosaicCrop = BitmapConversion.ToBitmapSource(ImageEffects.Mosaic(baseCropPhysical));
        _blurCrop = BitmapConversion.ToBitmapSource(ImageEffects.Blur(baseCropPhysical));
        if (adoptedShapes != null)
            Adopt(adoptedShapes);
    }

    public AnnotationTool Tool { get; private set; } = AnnotationTool.None;
    public int ShapeCount => _shapes.Count;
    public bool IsEditingText => _textEditor != null;
    public Color StrokeColor { get; private set; } = Color.FromRgb(0xFF, 0x3B, 0x30);
    public double TextFontSize { get; private set; } = 16;

    /// <summary>文字输入框开关输入法时回调（true=挂回 IME 支持中文，false=剥离）。</summary>
    public event Action<bool>? ImeSwitchRequested;

    /// <summary>设置后续图形/文字的颜色（PRD 用户反馈：颜色可调）。</summary>
    public void SetStrokeColor(Color color)
    {
        StrokeColor = color;
        _strokeBrush = new SolidColorBrush(color);
    }

    /// <summary>设置后续文字的字号。</summary>
    public void SetTextFontSize(double size) => TextFontSize = size;

    /// <summary>平移所有已提交图形（选区移动/缩放时保持标注锚定屏幕内容）。</summary>
    public void Translate(double dx, double dy)
    {
        foreach (var shape in _shapes)
        {
            double x = Canvas.GetLeft(shape);
            double y = Canvas.GetTop(shape);
            if (double.IsNaN(x)) x = 0;
            if (double.IsNaN(y)) y = 0;
            Canvas.SetLeft(shape, x + dx);
            Canvas.SetTop(shape, y + dy);
        }
        // 像素采样类图形（马赛克/模糊/放大镜）的采样区跟随新位置刷新
        RefreshPixelBrushes();
    }

    /// <summary>
    /// 缩放/平移后刷新像素采样类图形的取材区域：
    /// 马赛克/模糊按形状当前画布矩形重新采样，放大镜按当前中心重新取放大区域。
    /// </summary>
    public void RefreshPixelBrushes()
    {
        foreach (var shape in _shapes.OfType<System.Windows.Shapes.Shape>())
        {
            if (shape.Tag as string is not string kind) continue;
            double x = double.IsNaN(Canvas.GetLeft(shape)) ? 0 : Canvas.GetLeft(shape);
            double y = double.IsNaN(Canvas.GetTop(shape)) ? 0 : Canvas.GetTop(shape);

            switch (kind)
            {
                case "pixel:mosaic":
                    SetPixelRegion(shape, _mosaicCrop, new Rect(x, y, shape.Width, shape.Height));
                    break;
                case "pixel:blur":
                    SetPixelRegion(shape, _blurCrop, new Rect(x, y, shape.Width, shape.Height));
                    break;
                case "pixel:magnifier":
                    SetMagnifierFill(shape, x + shape.Width / 2, y + shape.Height / 2);
                    break;
            }
        }
    }

    /// <summary>接管既有图形（选区缩放后重建控制器时沿用已画内容，撤销栈随之延续）。</summary>
    public void Adopt(IEnumerable<UIElement> shapes)
    {
        foreach (var shape in shapes)
        {
            _canvas.Children.Add(shape);
            _shapes.Add(shape);
        }
    }

    /// <summary>
    /// 把渲染根（底图+标注图形）压平为物理像素位图。
    /// 静态工具方法：OverlayWindow 的导出与 --selftest 共用。
    /// 必须传"零偏移"的渲染根——RenderTargetBitmap.Render 会带上 VisualOffset。
    /// </summary>
    public static System.Drawing.Bitmap? FlattenToBitmap(Canvas renderRoot, double scale)
    {
        int pixelW = (int)Math.Round(renderRoot.Width * scale);
        int pixelH = (int)Math.Round(renderRoot.Height * scale);
        if (pixelW <= 0 || pixelH <= 0) return null;

        var rtb = new RenderTargetBitmap(pixelW, pixelH, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(renderRoot);

        int stride = pixelW * 4;
        var pixels = new byte[stride * pixelH];
        rtb.CopyPixels(pixels, stride, 0);

        var bitmap = new System.Drawing.Bitmap(pixelW, pixelH, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, pixelW, pixelH),
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            for (int y = 0; y < pixelH; y++)
                Marshal.Copy(pixels, y * stride, data.Scan0 + y * data.Stride, stride);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    // ---- 工具切换 ----

    public void SetTool(AnnotationTool tool)
    {
        CommitTextEditor();
        CancelDraft();
        Tool = tool; // 工具切换/取消的开关逻辑由 OverlayWindow 负责
    }

    // ---- 交互（坐标为画布内 DIP） ----

    public void Begin(Point p)
    {
        CommitTextEditor();
        p = Clamp(p);

        switch (Tool)
        {
            case AnnotationTool.Rectangle:
                _draft = NewShape(new Rectangle { Stroke = _strokeBrush, StrokeThickness = StrokeWidth });
                break;
            case AnnotationTool.Ellipse:
                _draft = NewShape(new Ellipse { Stroke = _strokeBrush, StrokeThickness = StrokeWidth });
                break;
            case AnnotationTool.Arrow:
                _draft = NewShape(new Path
                {
                    Stroke = _strokeBrush,
                    StrokeThickness = StrokeWidth,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                });
                break;
            case AnnotationTool.Pen:
                var pen = new Polyline
                {
                    Stroke = _strokeBrush,
                    StrokeThickness = StrokeWidth,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                };
                pen.Points.Add(p);
                _draft = NewShape(pen);
                break;
            case AnnotationTool.Text:
                BeginTextEditor(p);
                break;
            case AnnotationTool.Number:
                PlaceNumberBadge(p);
                break;
            case AnnotationTool.Mosaic:
                _pixelSource = _mosaicCrop;
                _draft = NewShape(new Rectangle { StrokeThickness = 0, Tag = "pixel:mosaic" });
                break;
            case AnnotationTool.Blur:
                _pixelSource = _blurCrop;
                _draft = NewShape(new Rectangle { StrokeThickness = 0, Tag = "pixel:blur" });
                break;
            case AnnotationTool.Magnifier:
                PlaceMagnifier(p);
                break;
        }
        _start = p;
    }

    public void Move(Point p)
    {
        p = Clamp(p);
        switch (_draft)
        {
            case System.Windows.Shapes.Rectangle rect when _pixelSource != null:
                SetBox(rect, CurrentBox(_start, p));
                var box = CurrentBox(_start, p);
                if (box.Width >= 2 && box.Height >= 2)
                    SetPixelRegion(rect, _pixelSource, box);
                break;
            case System.Windows.Shapes.Rectangle rect2:
                SetBox(rect2, CurrentBox(_start, p));
                break;
            case Ellipse ellipse:
                SetBox(ellipse, CurrentBox(_start, p));
                break;
            case Path arrow:
                arrow.Data = BuildArrowGeometry(_start, p);
                break;
            case Polyline pen:
                pen.Points.Add(p);
                break;
        }
    }

    public void End(Point p)
    {
        p = Clamp(p);
        if (_draft != null)
        {
            bool keep = _draft switch
            {
                System.Windows.Shapes.Rectangle or Ellipse => CurrentBox(_start, p) is { Width: >= 2, Height: >= 2 },
                _ => true,
            };
            if (keep)
                _shapes.Add(_draft);
            else
                _canvas.Children.Remove(_draft); // 过小的误触图形直接丢弃
            _draft = null;
        }
        _pixelSource = null;
    }

    public void Undo()
    {
        CommitTextEditor();
        if (_shapes.Count == 0) return;
        var last = _shapes[^1];
        _shapes.RemoveAt(_shapes.Count - 1);
        _canvas.Children.Remove(last);
    }

    public void Clear()
    {
        CommitTextEditor();
        foreach (var shape in _shapes)
            _canvas.Children.Remove(shape);
        _shapes.Clear();
        _nextNumber = 1;
    }

    // ---- 工具实现 ----

    private UIElement NewShape(Shape shape)
    {
        _canvas.Children.Add(shape);
        return shape;
    }

    /// <summary>
    /// 马赛克/模糊填充：采样"形状覆盖的图像区域"，铺满形状自身。
    /// 注意：TileBrush 的 Absolute Viewport 相对形状本地坐标（0,0 = 形状左上角），
    /// 传画布坐标会让采样区落到形状外，表现为"看不到效果"。
    /// </summary>
    private void SetPixelRegion(Shape shape, BitmapSource source, Rect canvasBox)
    {
        shape.Fill = new ImageBrush(source)
        {
            TileMode = TileMode.None,
            Stretch = Stretch.Fill,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(canvasBox.X * _scale, canvasBox.Y * _scale,
                canvasBox.Width * _scale, canvasBox.Height * _scale),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, canvasBox.Width, canvasBox.Height),
        };
    }

    private static void SetBox(Shape shape, Rect box)
    {
        Canvas.SetLeft(shape, box.X);
        Canvas.SetTop(shape, box.Y);
        shape.Width = box.Width;
        shape.Height = box.Height;
    }

    private static Rect CurrentBox(Point from, Point to)
        => new(Math.Min(from.X, to.X), Math.Min(from.Y, to.Y),
               Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));

    private Geometry BuildArrowGeometry(Point from, Point to)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(from, false, false);
            ctx.LineTo(to, true, false);

            var vx = to.X - from.X;
            var vy = to.Y - from.Y;
            var len = Math.Sqrt(vx * vx + vy * vy);
            if (len > 8)
            {
                vx /= len; vy /= len;
                const double headLen = 14;
                const double angle = Math.PI / 7; // ~25.7°
                double cos = Math.Cos(angle), sin = Math.Sin(angle);
                var head1 = new Point(to.X - (vx * cos - vy * sin) * headLen, to.Y - (vx * sin + vy * cos) * headLen);
                var head2 = new Point(to.X - (vx * cos + vy * sin) * headLen, to.Y - (-vx * sin + vy * cos) * headLen);
                ctx.BeginFigure(to, false, false);
                ctx.LineTo(head1, true, false);
                ctx.BeginFigure(to, false, false);
                ctx.LineTo(head2, true, false);
            }
        }
        geo.Freeze();
        return geo;
    }

    private void BeginTextEditor(Point p)
    {
        var editor = new TextBox
        {
            MinWidth = 130,
            FontSize = 15,
            Padding = new Thickness(5, 2, 5, 2),
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x10, 0x10, 0x10)),
            Foreground = WhiteStroke,
            BorderBrush = _strokeBrush,
            BorderThickness = new Thickness(1),
        };
        Canvas.SetLeft(editor, p.X);
        Canvas.SetTop(editor, p.Y);
        _canvas.Children.Add(editor);
        _textEditor = editor;
        ImeSwitchRequested?.Invoke(true); // 输入框需要中文输入
        editor.Focus();
        editor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitTextEditor();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                DiscardTextEditor();
                e.Handled = true;
            }
        };
        editor.LostFocus += (_, _) => CommitTextEditor();
    }

    private void CommitTextEditor()
    {
        if (_textEditor == null) return;
        var editor = _textEditor;
        _textEditor = null;
        var text = editor.Text.Trim();
        var left = Canvas.GetLeft(editor);
        var top = Canvas.GetTop(editor);
        _canvas.Children.Remove(editor);
        ImeSwitchRequested?.Invoke(false); // 输入结束，恢复快捷键模式

        if (text.Length == 0) return;
        var label = new TextBlock
        {
            Text = text,
            Foreground = _strokeBrush,
            FontSize = TextFontSize,
            FontWeight = FontWeights.SemiBold,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 2, ShadowDepth = 1, Opacity = 0.7 },
        };
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        _canvas.Children.Add(label);
        _shapes.Add(label);
    }

    private void DiscardTextEditor()
    {
        if (_textEditor == null) return;
        var editor = _textEditor;
        _textEditor = null;
        _canvas.Children.Remove(editor);
        ImeSwitchRequested?.Invoke(false);
    }

    private void PlaceNumberBadge(Point p)
    {
        var badge = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = _strokeBrush,
            Child = new TextBlock
            {
                Text = (_nextNumber++).ToString(),
                Foreground = WhiteStroke,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Canvas.SetLeft(badge, p.X - 11);
        Canvas.SetTop(badge, p.Y - 11);
        _canvas.Children.Add(badge);
        _shapes.Add(badge);
    }

    private void PlaceMagnifier(Point p)
    {
        double r = MagnifierDiameter / 2;
        double cx = Math.Clamp(p.X, r, Math.Max(r, _width - r));
        double cy = Math.Clamp(p.Y, r, Math.Max(r, _height - r));

        var ellipse = new Ellipse
        {
            Width = MagnifierDiameter,
            Height = MagnifierDiameter,
            Stroke = WhiteStroke,
            StrokeThickness = 2,
            Tag = "pixel:magnifier",
        };
        SetMagnifierFill(ellipse, cx, cy);
        Canvas.SetLeft(ellipse, cx - r);
        Canvas.SetTop(ellipse, cy - r);
        _canvas.Children.Add(ellipse);
        _shapes.Add(ellipse);
    }

    private void SetMagnifierFill(System.Windows.Shapes.Shape ellipse, double centerX, double centerY)
    {
        double regionDip = MagnifierDiameter / MagnifierZoom;
        double regionPx = regionDip * _scale;
        ellipse.Fill = new ImageBrush(_baseCrop)
        {
            TileMode = TileMode.None,
            Stretch = Stretch.Fill,
            ViewboxUnits = BrushMappingMode.Absolute,
            // 采样光标处放大区域，铺满椭圆本地坐标（Absolute 视口相对形状本地，非画布）
            Viewbox = new Rect(centerX * _scale - regionPx / 2, centerY * _scale - regionPx / 2, regionPx, regionPx),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, MagnifierDiameter, MagnifierDiameter),
        };
    }

    private void CancelDraft()
    {
        if (_draft == null) return;
        _canvas.Children.Remove(_draft);
        _draft = null;
        _pixelSource = null;
    }

    private Point Clamp(Point p)
        => new(Math.Clamp(p.X, 0, _width), Math.Clamp(p.Y, 0, _height));
}
