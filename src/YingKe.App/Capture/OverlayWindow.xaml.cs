using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using YingKe.App.Media;
using YingKe.Core.Capture;
using YingKe.Core.Configuration;
using YingKe.Core.Files;
using YingKe.Core.Geometry;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace YingKe.App.Capture;

public enum Manipulation
{
    None,
    Moving,
    Resizing,
}

[Flags]
public enum EdgeFlags
{
    None = 0,
    L = 1,
    R = 2,
    T = 4,
    B = 8,
}

/// <summary>
/// 全屏框选遮罩：展示冻结画面 + 暗色遮罩 + 选区 + 工具栏 + 取色放大镜 + 原位标注层。
/// 选区支持拖拽移动与八向调整大小（PRD 用户反馈）；标注随选区移动、缩放时锚定屏幕内容。
/// 所有屏幕内容取自冻结位图，遮罩层本身永不进入截图结果（PRD F-08）。
/// </summary>
public partial class OverlayWindow : Window, System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private void SetProp(ref string field, string value,
        [System.Runtime.CompilerServices.CallerMemberName] string? propName = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propName!));
    }

    private const double MagnifierSize = 142;
    private const double MagnifierHeight = 168;
    private const double BorderTolerance = 8;   // 边缘命中判定距离（DIP）
    private const double MinSelectionDip = 16;  // 调整大小后的最小选区（DIP）

    // 标注偏好：跨选区重建保持（AnnotationController 每次重建都会重置为默认值）
    private Color _strokeColor = Color.FromRgb(0xFF, 0x3B, 0x30);
    private double _textFontSize = 16;

    private readonly FrozenScreen _frozen;
    private readonly BitmapSource _frozenSource;
    private readonly AppConfig _config;

    private bool _isSelecting;
    private bool _hasSelection;
    private Point _startPoint;
    private Rect _selectionDip;

    private AnnotationController? _annotation;
    private AnnotationTool _activeTool = AnnotationTool.None;
    private bool _drawingAnnotation;
    private Dictionary<Key, Action> _actionKeys = new();

    private string _ocrKeyText = " Q";
    public string OcrKeyText { get => _ocrKeyText; set => SetProp(ref _ocrKeyText, value); }
    private string _aiKeyText = " I";
    public string AiKeyText { get => _aiKeyText; set => SetProp(ref _aiKeyText, value); }
    private string _translateKeyText = " Y";
    public string TranslateKeyText { get => _translateKeyText; set => SetProp(ref _translateKeyText, value); }
    private string _pinKeyText = " P";
    public string PinKeyText { get => _pinKeyText; set => SetProp(ref _pinKeyText, value); }
    private string _copyKeyText = " C";
    public string CopyKeyText { get => _copyKeyText; set => SetProp(ref _copyKeyText, value); }
    private string _saveKeyText = " S";
    public string SaveKeyText { get => _saveKeyText; set => SetProp(ref _saveKeyText, value); }

    private Manipulation _manipulation = Manipulation.None;
    private EdgeFlags _resizeEdges = EdgeFlags.None;
    private Vector _moveAnchor;
    // 鼠标移动按帧合并处理：高频 MouseMove 事件只记录位置，渲染帧统一应用，
    // 避免每次事件都触发全窗口（3840 宽、软件渲染）重绘造成卡顿
    private Point? _pendingPos;

    /// <summary>取字：把冻结图与选区交给 App 跑 OCR（所有权移交，Closed 时不释放）。</summary>
    public event Action<FrozenScreen, PixelRect>? ExtractTextRequested;

    /// <summary>AI 识图：视觉模型描述/解释选区内容（M3）。templateName 为空时用默认模板。</summary>
    public event Action<FrozenScreen, PixelRect, string?>? AiVisionRequested;

    /// <summary>翻译：本地 OCR + AI 翻译到目标语言（M3）。</summary>
    public event Action<FrozenScreen, PixelRect>? TranslateRequested;

    /// <summary>钉图：把压平后的标注图交给 App 创建钉图窗口（位图所有权移交）。</summary>
    public event Action<System.Drawing.Bitmap, Point>? PinRequested;

    /// <summary>App 在 ExtractTextRequested 处理器里置 true，Closed 时据此跳过释放。</summary>
    public bool HandoffCaptureOwnership { get; set; }

    public OverlayWindow(FrozenScreen frozen, AppConfig config)
    {
        InitializeComponent();
        _frozen = frozen;
        _config = config;
        _frozenSource = BitmapConversion.ToBitmapSource(frozen.Bitmap);

        var scale = frozen.Scale;
        FrozenImage.Source = _frozenSource;
        FrozenImage.Width = frozen.Bitmap.Width / scale;
        FrozenImage.Height = frozen.Bitmap.Height / scale;
        BuildColorPalette();

        Left = frozen.VirtualBounds.X / scale;
        Top = frozen.VirtualBounds.Y / scale;
        Width = frozen.VirtualBounds.Width / scale;
        Height = frozen.VirtualBounds.Height / scale;

        PreviewKeyDown += OnPreviewKeyDown;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseRightButtonDown += (_, _) => Close();
        // 注意：CompositionTarget.Rendering 是静态事件，匿名 lambda 会把整个覆盖层
        // （含全屏位图与渲染表面）钉在内存里——每次截图泄漏一份。必须具名并在关闭时退订。
        CompositionTarget.Rendering += OnRenderingFrame;
        Loaded += (_, _) =>
        {
            ShowInitialMask();
            // 窗口必须持有键盘焦点，否则 PreviewKeyDown 不会触发（无焦点目标时
            // 隧道路由不发生），框选后的 Esc/Enter/工具快捷键全部失灵
            Focusable = true;
            Keyboard.Focus(this);
            ForceForeground();
            // 剥离输入法（WPF 官方 API）：中文 IME 会把 Q/I/Y 吃成组合输入，快捷键全部失效
            System.Windows.Input.InputMethod.SetIsInputMethodEnabled(this, false);
            BuildActionKeys();
            RefreshToolbarKeyHints();
        };
    }

    /// <summary>选区内可配置动作键（PRD 用户反馈：取字/识图/翻译/钉图/保存可改键）。</summary>
    private void BuildActionKeys()
    {
        _actionKeys = new Dictionary<Key, Action>
        {
            [ParseKeyName(_config.Hotkeys.OcrKey, Key.Q)] = ExtractText,
            [ParseKeyName(_config.Hotkeys.AiVisionKey, Key.I)] = () => AiVision(),
            [ParseKeyName(_config.Hotkeys.TranslateKey, Key.Y)] = Translate,
            [ParseKeyName(_config.Hotkeys.PinKey, Key.P)] = PinSelection,
            [ParseKeyName(_config.Hotkeys.SaveKey, Key.S)] = SaveSelectionWithDialog,
            [Key.C] = CopySelectionToClipboard,
            [Key.S] = SaveSelectionWithDialog,
        };
    }

    private static Key ParseKeyName(string name, Key fallback)
        => Enum.TryParse<Key>(name, ignoreCase: true, out var key) ? key : fallback;

    /// <summary>工具栏按键提示跟随配置刷新（设置改键后，新遮罩显示新键）。</summary>
    private void RefreshToolbarKeyHints()
    {
        OcrKeyText = " " + _config.Hotkeys.OcrKey;
        AiKeyText = " " + _config.Hotkeys.AiVisionKey;
        TranslateKeyText = " " + _config.Hotkeys.TranslateKey;
        PinKeyText = " " + _config.Hotkeys.PinKey;
        SaveKeyText = " " + _config.Hotkeys.SaveKey;
    }

    /// <summary>
    /// 热键唤出时当前进程未持有前台权限，直接 Activate 可能被前台锁拒绝，
    /// 按键会泄漏到原前台应用（表现为"快捷键变成打字"）。模拟一次 Alt 按放绕过前台锁。
    /// </summary>
    private void ForceForeground()
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Activate();
            YingKe.Core.Native.NativeMethods.keybd_event(
                YingKe.Core.Native.NativeMethods.VK_MENU, 0, 0, IntPtr.Zero);
            YingKe.Core.Native.NativeMethods.keybd_event(
                YingKe.Core.Native.NativeMethods.VK_MENU, 0, YingKe.Core.Native.NativeMethods.KEYEVENTF_KEYUP, IntPtr.Zero);
            YingKe.Core.Native.NativeMethods.SetForegroundWindow(hwnd);
        }
        catch
        {
            // 个别受限环境（如 elevated 前台应用）仍会失败，鼠标点击后即可恢复
        }
    }

    /// <summary>
    /// 遮罩窗口默认剥离 IME（快捷键不被输入法拦截）；
    /// 文字标注的输入框需要中文时由控制器通知挂回。
    /// </summary>
    private IntPtr _imeOriginalContext;

    internal void SetImeForOverlay(bool enable)
    {
        try
        {
            System.Windows.Input.InputMethod.SetIsInputMethodEnabled(this, enable);
        }
        catch
        {
            // 失败不影响主流程
        }
    }

    private double Scale => _frozen.Scale;

    // ---- 选区交互 ----

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 标注模式：选区内按下即开始绘制
        if (_activeTool != AnnotationTool.None && _hasSelection)
        {
            var ap = e.GetPosition(AnnotationHost);
            if (ap.X >= 0 && ap.Y >= 0 && ap.X <= AnnotationHost.Width && ap.Y <= AnnotationHost.Height)
            {
                _drawingAnnotation = true;
                CaptureMouse();
                _annotation?.Begin(ap);
            }
            return;
        }

        if (Toolbar.IsMouseOver)
            return; // 工具栏区域（含空白处）的按下不参与框选

        var pos = e.GetPosition(this);

        // 选区内双击 = 复制（含标注）
        if (_hasSelection && e.ClickCount == 2 && _selectionDip.Contains(pos))
        {
            CopySelectionToClipboard();
            return;
        }

        // 边缘/四角优先判定（±8px 感应带，选框内外两侧都能抓住）；
        // 命中即调整大小；选区内非边缘 = 移动；选区外 = 框新选区
        if (_hasSelection)
        {
            var edges = HitEdges(pos);
            if (edges != EdgeFlags.None)
            {
                _manipulation = Manipulation.Resizing;
                _resizeEdges = edges;
                AnnotationBaseImage.Visibility = Visibility.Collapsed;
                CaptureMouse();
                return;
            }
            if (_selectionDip.Contains(pos))
            {
                _manipulation = Manipulation.Moving;
                _moveAnchor = new Vector(pos.X - _selectionDip.X, pos.Y - _selectionDip.Y);
                // 拖动期间隐藏选区底图：全屏冻结图从后方透出（内容随位置自然变化），
                // 避免每帧位图重裁/换源导致的闪烁与卡顿
                AnnotationBaseImage.Visibility = Visibility.Collapsed;
                CaptureMouse();
                return;
            }
        }

        _isSelecting = true;
        _hasSelection = false;
        _startPoint = pos;
        Toolbar.Visibility = Visibility.Collapsed;
        ResetAnnotation();
        CaptureMouse();
        UpdateSelectionVisuals(new Rect(pos, pos));
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        // 底图被释放（取字/翻译等流程完成后的竞态）：僵尸遮罩自愈关闭，终止异常风暴
        if (_frozen.IsDisposed)
        {
            Close();
            return;
        }

        if (_drawingAnnotation)
        {
            _annotation?.Move(e.GetPosition(AnnotationHost));
            return;
        }

        _pendingPos = e.GetPosition(this); // 只记录，渲染帧统一应用（把高频事件合并到每帧一次）
    }

    /// <summary>把最新鼠标位置应用到当前状态（选框绘制/移动/缩放/放大镜/光标）。</summary>
    private readonly System.Diagnostics.Stopwatch _frameDiag = System.Diagnostics.Stopwatch.StartNew();
    private long _slowFramesLogged;

    /// <summary>Rendering 帧回调（具名以便关闭时从静态事件退订，见 OnClosed）。</summary>
    private void OnRenderingFrame(object? sender, EventArgs e) => ApplyPendingFrame();

    protected override void OnClosed(EventArgs e)
    {
        CompositionTarget.Rendering -= OnRenderingFrame;
        base.OnClosed(e);
    }

    private void ApplyPendingFrame()
    {
        var frameStart = _frameDiag.ElapsedMilliseconds;
        var visStart = _frameDiag.ElapsedMilliseconds;

        if (_pendingPos is not { } pos)
            return;
        _pendingPos = null;

        if (_frozen.IsDisposed)
        {
            Close();
            return;
        }

        switch (_manipulation)
        {
            case Manipulation.Moving:
            {
                double nx = Math.Clamp(pos.X - _moveAnchor.X, 0, Math.Max(0, Scene.ActualWidth - _selectionDip.Width));
                double ny = Math.Clamp(pos.Y - _moveAnchor.Y, 0, Math.Max(0, Scene.ActualHeight - _selectionDip.Height));
                double dx = _selectionDip.X - nx;
                double dy = _selectionDip.Y - ny;
                _selectionDip = new Rect(nx, ny, _selectionDip.Width, _selectionDip.Height);
                UpdateSelectionVisuals(_selectionDip);
                PlaceAnnotationHost(_selectionDip);
                // 拖动中零位图操作：底图隐藏，全屏冻结图自然透出（就是"在新位置开窗"）；
                // 松手后 EndManipulation 做一次最终重裁
                _annotation?.Translate(dx, dy);
                ShowToolbar(_selectionDip);
                return;
            }
            case Manipulation.Resizing:
                ApplyResize(pos);
                return;
        }

        var preVis = _frameDiag.ElapsedMilliseconds;
        UpdateCursor(pos);
        UpdateMagnifier(pos);
        if (_isSelecting)
            UpdateSelectionVisuals(new Rect(_startPoint, pos));
        var frameMs = _frameDiag.ElapsedMilliseconds - frameStart;
        if (frameMs > 25 && _slowFramesLogged < 20)
        {
            _slowFramesLogged++;
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "yingke-error.log"),
                    "[diag] slow frame " + frameMs + "ms state=" + _manipulation + "/" + _activeTool + "\r\n");
            }
            catch { }
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_manipulation != Manipulation.None)
        {
            EndManipulation();
            return;
        }

        if (_drawingAnnotation)
        {
            _drawingAnnotation = false;
            ReleaseMouseCapture();
            _annotation?.End(e.GetPosition(AnnotationHost));
            return;
        }

        if (!_isSelecting) return;
        _isSelecting = false;
        ReleaseMouseCapture();

        var rect = new Rect(_startPoint, e.GetPosition(this));
        if (!SelectionMath.MeetsMinimum(ToPhysical(rect)))
        {
            UpdateSelectionVisuals(Rect.Empty);
            return;
        }

        _selectionDip = rect;
        _hasSelection = true;
        UpdateSelectionVisuals(rect);
        SetupAnnotation();
        ShowToolbar(rect);
        Keyboard.Focus(this); // 鼠标交互后把键盘焦点收回窗口，保证快捷键持续可用
    }

    /// <summary>结束移动/缩放：缩放需按新选区重裁底图并重建标注宿主（保留已画图形）。</summary>
    private void EndManipulation()
    {
        bool wasResizing = _manipulation == Manipulation.Resizing;
        _manipulation = Manipulation.None;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (wasResizing)
            RebuildAnnotationAfterResize(); // 重裁最终区域 + 采纳图形 + 刷新像素刷
        else
            RefreshBaseImage(); // 移动结束：底图换成最终位置的内容并恢复显示
        ShowToolbar(_selectionDip);
        Keyboard.Focus(this);
    }

    /// <summary>按当前选区重裁底图并恢复显示（拖动结束后调用一次）。</summary>
    private void RefreshBaseImage()
    {
        using var crop = _frozen.Crop(SelectionPhysicalRect);
        AnnotationBaseImage.Source = BitmapConversion.ToBitmapSource(crop);
        AnnotationBaseImage.Width = _selectionDip.Width;
        AnnotationBaseImage.Height = _selectionDip.Height;
        AnnotationBaseImage.Visibility = Visibility.Visible;
    }

    private void ApplyResize(Point pos)
    {
        double x = Math.Clamp(pos.X, 0, Scene.ActualWidth);
        double y = Math.Clamp(pos.Y, 0, Scene.ActualHeight);
        double left = _selectionDip.Left;
        double top = _selectionDip.Top;
        double right = _selectionDip.Right;
        double bottom = _selectionDip.Bottom;

        if (_resizeEdges.HasFlag(EdgeFlags.L)) left = Math.Min(x, right - MinSelectionDip);
        if (_resizeEdges.HasFlag(EdgeFlags.R)) right = Math.Max(x, left + MinSelectionDip);
        if (_resizeEdges.HasFlag(EdgeFlags.T)) top = Math.Min(y, bottom - MinSelectionDip);
        if (_resizeEdges.HasFlag(EdgeFlags.B)) bottom = Math.Max(y, top + MinSelectionDip);

        var newRect = new Rect(left, top, right - left, bottom - top);
        double dx = _selectionDip.X - newRect.X;
        double dy = _selectionDip.Y - newRect.Y;
        _selectionDip = newRect;

        UpdateSelectionVisuals(newRect);
        PlaceAnnotationHost(newRect);
        if (dx != 0 || dy != 0)
            _annotation?.Translate(dx, dy); // 标注锚定屏幕内容；松手后重裁底图统一修正
    }

    /// <summary>缩放结束后：按新选区重裁底图，重建标注宿主并采纳已画图形（撤销栈延续）。</summary>
    private void RebuildAnnotationAfterResize()
    {
        if (_frozen.IsDisposed) { Close(); return; }
        var children = ShapesCanvas.Children.Cast<UIElement>().ToList();
        ShapesCanvas.Children.Clear();
        _annotation = null;

        var crop = _frozen.Crop(SelectionPhysicalRect);
        PlaceAnnotationHost(_selectionDip);
        AnnotationBaseImage.Source = BitmapConversion.ToBitmapSource(crop);
        AnnotationBaseImage.Width = _selectionDip.Width;
        AnnotationBaseImage.Height = _selectionDip.Height;
        AnnotationBaseImage.Visibility = Visibility.Visible;
        _annotation = new AnnotationController(ShapesCanvas, crop, Scale, _selectionDip.Width, _selectionDip.Height, children);
        _annotation.ImeSwitchRequested += SetImeForOverlay;
        // 换底图后像素采样类图形（马赛克/模糊/放大镜）必须按新坐标系重算取材区域
        _annotation.RefreshPixelBrushes();
        ApplyAnnotationPrefs();
    }

    private EdgeFlags HitEdges(Point pos)
    {
        var r = _selectionDip;
        var edges = EdgeFlags.None;
        if (Math.Abs(pos.X - r.Left) <= BorderTolerance) edges |= EdgeFlags.L;
        if (Math.Abs(pos.X - r.Right) <= BorderTolerance) edges |= EdgeFlags.R;
        if (Math.Abs(pos.Y - r.Top) <= BorderTolerance) edges |= EdgeFlags.T;
        if (Math.Abs(pos.Y - r.Bottom) <= BorderTolerance) edges |= EdgeFlags.B;

        bool inYBand = pos.Y >= r.Top - BorderTolerance && pos.Y <= r.Bottom + BorderTolerance;
        bool inXBand = pos.X >= r.Left - BorderTolerance && pos.X <= r.Right + BorderTolerance;
        if ((edges & (EdgeFlags.L | EdgeFlags.R)) != 0 && !inYBand) edges &= ~(EdgeFlags.L | EdgeFlags.R);
        if ((edges & (EdgeFlags.T | EdgeFlags.B)) != 0 && !inXBand) edges &= ~(EdgeFlags.T | EdgeFlags.B);
        return edges;
    }

    private void UpdateCursor(Point pos)
    {
        if (!_hasSelection)
        {
            Cursor = Cursors.Cross;
            return;
        }

        var edges = HitEdges(pos);
        if (edges == EdgeFlags.None)
        {
            Cursor = _selectionDip.Contains(pos) ? Cursors.SizeAll : Cursors.Cross;
            return;
        }

        bool horizontal = (edges & (EdgeFlags.L | EdgeFlags.R)) != 0;
        bool vertical = (edges & (EdgeFlags.T | EdgeFlags.B)) != 0;
        Cursor = horizontal && vertical
            ? (edges == (EdgeFlags.L | EdgeFlags.T) || edges == (EdgeFlags.R | EdgeFlags.B) ? Cursors.SizeNWSE : Cursors.SizeNESW)
            : horizontal ? Cursors.SizeWE
            : Cursors.SizeNS;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "yingke-error.log"),
                "[diag] key=" + e.Key + "\r\n");
        }
        catch { }

        // 文字标注编辑中：按键交给输入框（Enter 提交、Esc 取消）
        if (_annotation is { IsEditingText: true })
            return;

        // Ctrl+C / Ctrl+S 与无修饰键单键等价
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.C when _hasSelection:
                    CopySelectionToClipboard();
                    e.Handled = true;
                    return;
                case Key.S when _hasSelection:
                    SaveSelectionWithDialog();
                    e.Handled = true;
                    return;
            }
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None)
            return;

        // 标注工具单键（PRD 用户反馈：工具栏操作加快捷键）
        if (TryMapToolKey(e.Key, out var tool) && _hasSelection)
        {
            ActivateTool(tool);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                if (_manipulation != Manipulation.None)
                    EndManipulation(); // 结束移动/缩放
                else if (_activeTool != AnnotationTool.None)
                    DeactivateTool(); // 先退出标注工具，再按一次才退出截图
                else
                    Close();
                e.Handled = true;
                break;
            case Key.Enter when _hasSelection:
                CopySelectionToClipboard();
                e.Handled = true;
                break;
            case Key.Z when _hasSelection:
                _annotation?.Undo();
                e.Handled = true;
                break;
            case Key.X when _hasSelection:
                _annotation?.Clear();
                e.Handled = true;
                break;
            default:
                if (_hasSelection && _actionKeys.TryGetValue(e.Key, out var action))
                {
                    try
                    {
                        File.AppendAllText(Path.Combine(Path.GetTempPath(), "yingke-error.log"),
                            "[diag] key=" + e.Key + " hit=True\r\n");
                    }
                    catch { }
                    action();
                    e.Handled = true;
                }
                else
                {
                    try
                    {
                        File.AppendAllText(Path.Combine(Path.GetTempPath(), "yingke-error.log"),
                            "[diag] key=" + e.Key + " hit=False\r\n");
                    }
                    catch { }
                }
                break;
        }
    }

    private static bool TryMapToolKey(Key key, out AnnotationTool tool)
    {
        tool = key switch
        {
            Key.R => AnnotationTool.Rectangle,
            Key.O => AnnotationTool.Ellipse,
            Key.A => AnnotationTool.Arrow,
            Key.D => AnnotationTool.Pen,
            Key.T => AnnotationTool.Text,
            Key.N => AnnotationTool.Number,
            Key.M => AnnotationTool.Mosaic,
            Key.B => AnnotationTool.Blur,
            Key.U => AnnotationTool.Magnifier,
            _ => AnnotationTool.None,
        };
        return tool != AnnotationTool.None;
    }

    // ---- 标注（PRD F-29/F-30） ----

    private void SetupAnnotation()
    {
        if (_frozen.IsDisposed) { Close(); return; }
        var crop = _frozen.Crop(SelectionPhysicalRect);
        PlaceAnnotationHost(_selectionDip);
        AnnotationBaseImage.Source = BitmapConversion.ToBitmapSource(crop);
        AnnotationBaseImage.Width = _selectionDip.Width;
        AnnotationBaseImage.Height = _selectionDip.Height;
        ShapesCanvas.Children.Clear();
        _annotation = new AnnotationController(ShapesCanvas, crop, Scale, _selectionDip.Width, _selectionDip.Height);
        _annotation.ImeSwitchRequested += SetImeForOverlay;
        ApplyAnnotationPrefs();
        AnnotationHost.Visibility = Visibility.Visible;
    }

    private void PlaceAnnotationHost(Rect sel)
    {
        Canvas.SetLeft(AnnotationHost, sel.X);
        Canvas.SetTop(AnnotationHost, sel.Y);
        AnnotationHost.Width = sel.Width;
        AnnotationHost.Height = sel.Height;
        AnnotationRenderRoot.Width = sel.Width;
        AnnotationRenderRoot.Height = sel.Height;
    }

    private void ResetAnnotation()
    {
        _annotation = null;
        _activeTool = AnnotationTool.None;
        _drawingAnnotation = false;
        ShapesCanvas.Children.Clear();
        AnnotationBaseImage.Source = null;
        AnnotationHost.Visibility = Visibility.Hidden;
        SetToolButtonVisual();
    }

    private void OnToolClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        ActivateTool(Enum.Parse<AnnotationTool>(tag));
    }

    private void ActivateTool(AnnotationTool tool)
    {
        _activeTool = _activeTool == tool ? AnnotationTool.None : tool;
        _annotation?.SetTool(_activeTool);
        SetToolButtonVisual();
    }

    /// <summary>标注控制器重建后恢复用户的颜色/字号偏好（否则会静默重置回默认值）。</summary>
    private void ApplyAnnotationPrefs()
    {
        _annotation?.SetStrokeColor(_strokeColor);
        _annotation?.SetTextFontSize(_textFontSize);
    }

    /// <summary>把当前描边色应用到标注控制器（供色块与自定义颜色共用）。</summary>
    private void ApplySwatchSelection(Border? selected)
    {
        if (selected?.Parent is StackPanel panel)
        {
            foreach (var child in panel.Children)
                if (child is Border { Tag: string } other)
                    other.BorderBrush = ReferenceEquals(other, selected) ? Brushes.White : Brushes.Transparent;
        }
    }

    private void OnSwatchClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // 防止冒泡到窗口级框选逻辑，把当前选区重置掉
        if (sender is not Border { Tag: string hex } swatch) return;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            _strokeColor = color;
            _annotation?.SetStrokeColor(color);

            // 白描边标记当前选中色
            ApplySwatchSelection(swatch);
        }
        catch (FormatException)
        {
            // 忽略非法颜色标签
        }
    }

    /// <summary>填充扩展调色板（24 色，点击直接应用）。</summary>
    private void BuildColorPalette()
    {
        string[] colors =
        {
            "#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#1E9FFF", "#AF52DE",
            "#FF6482", "#FFB84C", "#C7E66A", "#4CD9C2", "#5AC8FA", "#7B8DFF",
            "#FF2DB4", "#D4A5FF", "#FFFFFF", "#C8C8C8", "#969696", "#646464",
            "#141414", "#7A4A2B", "#2B5D7A", "#2B7A3B", "#7A2B5D", "#7A6A2B",
        };
        foreach (var hex in colors)
        {
            var dot = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 6, 6),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Tag = hex,
                ToolTip = hex,
            };
            dot.MouseLeftButtonDown += OnPaletteColorClicked;
            PalettePanel.Children.Add(dot);
        }
    }

    /// <summary>
    /// 展开/收起自定义调色板。注意：不要在这里弹原生模态对话框（Win32 ChooseColor）——
    /// 全屏置顶覆盖层之上的模态对话框会出现 z 序异常导致整个截图界面冻结（已实测踩坑）。
    /// </summary>
    private void OnCustomColorClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        PalettePanel.Visibility = PalettePanel.Visibility == Visibility.Collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>从调色板中选色：应用描边色并同步到自定义色块外观。</summary>
    private void OnPaletteColorClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is Border { Tag: string hex } swatch)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            _strokeColor = color;
            _annotation?.SetStrokeColor(color);
            CustomColorSwatch.Background = new SolidColorBrush(color);
            ApplySwatchSelection(CustomColorSwatch);
            CustomColorSwatch.BorderBrush = Brushes.White;
        }
        PalettePanel.Visibility = Visibility.Collapsed;
    }

    private void OnFontSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontSizeCombo.SelectedItem is not ComboBoxItem item) return;
        if (double.TryParse(item.Content as string, out var size))
        {
            _textFontSize = size;
            _annotation?.SetTextFontSize(size);
        }
    }

    private void OnUndoClicked(object sender, RoutedEventArgs e) => _annotation?.Undo();

    private void OnClearClicked(object sender, RoutedEventArgs e) => _annotation?.Clear();

    private void DeactivateTool()
    {
        _activeTool = AnnotationTool.None;
        _annotation?.SetTool(AnnotationTool.None);
        SetToolButtonVisual();
    }

    private void SetToolButtonVisual()
    {
        var activeBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x9F, 0xFF));
        foreach (var child in ToolRow.Children)
        {
            if (child is Button { Tag: string tag } button && Enum.TryParse<AnnotationTool>(tag, out var tool))
                button.Background = tool == _activeTool ? activeBrush : Brushes.Transparent;
        }
    }

    // ---- 工具栏动作 ----

    private void OnExtractTextClicked(object sender, RoutedEventArgs e) => ExtractText();

    private void OnAiVisionClicked(object sender, RoutedEventArgs e) => ShowAiVisionMenu();

    private void OnTranslateClicked(object sender, RoutedEventArgs e) => Translate();

    private void ExtractText()
    {
        if (!_hasSelection) return;
        ExtractTextRequested?.Invoke(_frozen, SelectionPhysicalRect);
    }

    private void AiVision(string? templateName = null)
    {
        if (!_hasSelection) return;
        AiVisionRequested?.Invoke(_frozen, SelectionPhysicalRect, templateName ?? _config.Ai.VisionTemplate);
    }

    /// <summary>AI 识图模板选择菜单（PRD F-15）：内置任务模板 + 自定义模板，选中即识别。</summary>
    private void ShowAiVisionMenu()
    {
        if (!_hasSelection) return;
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };

        void AddItem(string label, string templateName)
        {
            var item = new MenuItem
            {
                Header = templateName == _config.Ai.VisionTemplate ? label + " ✓" : label,
                FontWeight = templateName == _config.Ai.VisionTemplate ? FontWeights.Bold : FontWeights.Normal,
            };
            item.Click += (_, _) =>
            {
                _config.Ai.VisionTemplate = templateName;
                _config.Save();
                AiVision(templateName);
            };
            menu.Items.Add(item);
        }

        AddItem("AI 识图（描述内容）", "AI 识图");
        AddItem("精确取字", "精确取字");
        AddItem("代码解释", "代码解释");
        AddItem("转 Markdown", "转 Markdown");
        AddItem("转 CSV", "转 CSV");
        AddItem("转 LaTeX", "转 LaTeX");
        foreach (var kv in _config.Ai.CustomPrompts)
            AddItem(kv.Key, kv.Key);
        menu.Items.Add(new Separator());

        menu.IsOpen = true;
    }

    private void Translate()
    {
        if (!_hasSelection) return;
        TranslateRequested?.Invoke(_frozen, SelectionPhysicalRect);
    }

    private void OnPinClicked(object sender, RoutedEventArgs e) => PinSelection();

    private void PinSelection()
    {
        var bitmap = ExportFlattenedSelection();
        if (bitmap == null) return;
        // 位图所有权移交给钉图窗口（PinWindow 关闭时释放）
        PinRequested?.Invoke(bitmap, new Point(
            (_frozen.VirtualBounds.X + SelectionPhysicalRect.X) / Scale,
            (_frozen.VirtualBounds.Y + SelectionPhysicalRect.Y) / Scale));
        Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => Close();

    private void OnCopyClicked(object sender, RoutedEventArgs e) => CopySelectionToClipboard();

    private void OnSaveClicked(object sender, RoutedEventArgs e) => SaveSelectionWithDialog();

    private void CopySelectionToClipboard()
    {
        try
        {
            using var bitmap = ExportFlattenedSelection();
            if (bitmap == null) return;
            ClipboardHelper.SetImage(BitmapConversion.ToBitmapSource(bitmap));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"复制到剪贴板失败：{ex.Message}", "映刻",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Close();
    }

    private void SaveSelectionWithDialog()
    {
        var configuredDir = _config.General.SaveDirectory;
        var initialDirectory = string.IsNullOrWhiteSpace(configuredDir)
            ? SavePathGenerator.DefaultDirectory()
            : configuredDir;

        var dialog = new SaveFileDialog
        {
            FileName = SavePathGenerator.DefaultFileName(DateTime.Now),
            InitialDirectory = initialDirectory,
            Filter = "PNG 图片|*.png|JPEG 图片|*.jpg",
            DefaultExt = "png",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            using var bitmap = ExportFlattenedSelection();
            if (bitmap == null) return;
            if (dialog.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                dialog.FileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                bitmap.Save(dialog.FileName, ImageFormat.Jpeg);
            else
                bitmap.Save(dialog.FileName, ImageFormat.Png);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存失败：{ex.Message}", "映刻",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Close();
    }

    /// <summary>
    /// 导出选区（含标注）为物理像素位图：无标注直接裁剪；
    /// 有标注用渲染根压平（共享 AnnotationController.FlattenToBitmap）。
    /// </summary>
    private System.Drawing.Bitmap? ExportFlattenedSelection()
    {
        if (!_hasSelection) return null;
        if (_annotation == null || _annotation.ShapeCount == 0)
            return _frozen.Crop(SelectionPhysicalRect);

        var bitmap = AnnotationController.FlattenToBitmap(AnnotationRenderRoot, Scale);
        return bitmap ?? _frozen.Crop(SelectionPhysicalRect);
    }

    // ---- 视觉更新 ----

    /// <summary>打开遮罩即全屏变暗（对齐主流截图工具的肌肉记忆），开始拖拽后由选区挖孔。</summary>
    private void ShowInitialMask()
    {
        foreach (var mask in new[] { MaskTop, MaskLeft, MaskRight, MaskBottom })
            mask.Visibility = Visibility.Visible;
        SetRect(MaskTop, 0, 0, Scene.ActualWidth, Scene.ActualHeight);
        SetRect(MaskLeft, 0, 0, 0, 0);
        SetRect(MaskRight, 0, 0, 0, 0);
        SetRect(MaskBottom, 0, 0, 0, 0);
        SelectionBorder.Visibility = Visibility.Hidden;
        SizeBadge.Visibility = Visibility.Hidden;
    }

    private PixelRect SelectionPhysicalRect => ToPhysical(_selectionDip);

    private PixelRect ToPhysical(Rect dipRect)
    {
        int x = (int)Math.Round(dipRect.X * Scale);
        int y = (int)Math.Round(dipRect.Y * Scale);
        int w = (int)Math.Round(dipRect.Width * Scale);
        int h = (int)Math.Round(dipRect.Height * Scale);
        return new PixelRect(x, y, w, h);
    }

    private void UpdateSelectionVisuals(Rect rect)
    {
        bool empty = rect.IsEmpty || rect.Width < 0 || rect.Height < 0;
        double sceneW = Scene.ActualWidth;
        double sceneH = Scene.ActualHeight;

        if (empty)
        {
            foreach (var mask in new[] { MaskTop, MaskLeft, MaskRight, MaskBottom })
                mask.Visibility = Visibility.Hidden;
            SelectionBorder.Visibility = Visibility.Hidden;
            SizeBadge.Visibility = Visibility.Hidden;
            HandleLayer.Visibility = Visibility.Hidden;
            return;
        }

        foreach (var mask in new[] { MaskTop, MaskLeft, MaskRight, MaskBottom })
            mask.Visibility = Visibility.Visible;

        SetRect(MaskTop, 0, 0, sceneW, rect.Top);
        SetRect(MaskBottom, 0, rect.Bottom, sceneW, sceneH - rect.Bottom);
        SetRect(MaskLeft, 0, rect.Top, rect.Left, rect.Height);
        SetRect(MaskRight, rect.Right, rect.Top, sceneW - rect.Right, rect.Height);

        Canvas.SetLeft(SelectionBorder, rect.X);
        Canvas.SetTop(SelectionBorder, rect.Y);
        SelectionBorder.Width = rect.Width;
        SelectionBorder.Height = rect.Height;
        SelectionBorder.Visibility = Visibility.Visible;

        // 八向调整手柄
        SetHandle(HandleNW, rect.Left - 4, rect.Top - 4);
        SetHandle(HandleN, rect.Left + rect.Width / 2 - 4, rect.Top - 4);
        SetHandle(HandleNE, rect.Right - 4, rect.Top - 4);
        SetHandle(HandleW, rect.Left - 4, rect.Top + rect.Height / 2 - 4);
        SetHandle(HandleE, rect.Right - 4, rect.Top + rect.Height / 2 - 4);
        SetHandle(HandleSW, rect.Left - 4, rect.Bottom - 4);
        SetHandle(HandleS, rect.Left + rect.Width / 2 - 4, rect.Bottom - 4);
        SetHandle(HandleSE, rect.Right - 4, rect.Bottom - 4);
        HandleLayer.Visibility = Visibility.Visible;

        var phys = ToPhysical(rect);
        var badgeText = $"{phys.Width} × {phys.Height}";
        if (SizeBadgeText.Text != badgeText)
        {
            SizeBadgeText.Text = badgeText;
            SizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }
        double badgeY = rect.Y - SizeBadge.DesiredSize.Height - 4;
        if (badgeY < 0) badgeY = rect.Y + 4;
        Canvas.SetLeft(SizeBadge, Math.Max(0, rect.X));
        Canvas.SetTop(SizeBadge, badgeY);
        SizeBadge.Visibility = Visibility.Visible;
    }

    private static void SetRect(System.Windows.Shapes.Rectangle shape,
        double x, double y, double width, double height)
    {
        Canvas.SetLeft(shape, x);
        Canvas.SetTop(shape, y);
        shape.Width = Math.Max(0, width);
        shape.Height = Math.Max(0, height);
    }

    private static void SetHandle(UIElement handle, double x, double y)
    {
        Canvas.SetLeft(handle, x);
        Canvas.SetTop(handle, y);
    }

    private Size? _toolbarSize;

    private void ShowToolbar(Rect selection)
    {
        Toolbar.Visibility = Visibility.Visible;
        if (_toolbarSize is not { } size)
        {
            Toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            size = Toolbar.DesiredSize;
            _toolbarSize = size;
        }
        double w = size.Width;
        double h = size.Height;

        double x = selection.X;
        double y = selection.Bottom + 10;
        if (y + h > Scene.ActualHeight - 4)
            y = selection.Y - h - 10;
        x = Math.Clamp(x, 4, Math.Max(4, Scene.ActualWidth - w - 4));
        y = Math.Max(4, y);

        Canvas.SetLeft(Toolbar, x);
        Canvas.SetTop(Toolbar, y);
    }

    private void UpdateMagnifier(Point dipPos)
    {
        if (_frozen.IsDisposed)
        {
            Magnifier.Visibility = Visibility.Hidden;
            return;
        }

        var bounds = new PixelRect(0, 0, _frozen.Bitmap.Width, _frozen.Bitmap.Height);
        int px = (int)(dipPos.X * Scale);
        int py = (int)(dipPos.Y * Scale);
        if (!bounds.Contains(px, py))
        {
            Magnifier.Visibility = Visibility.Hidden;
            return;
        }

        const int span = 17;
        const int radius = span / 2;
        var cropRect = SelectionMath.Clamp(new PixelRect(px - radius, py - radius, span, span), bounds);
        var cropped = new CroppedBitmap(_frozenSource,
            new System.Windows.Int32Rect(cropRect.X, cropRect.Y, cropRect.Width, cropRect.Height));
        MagnifierImage.Source = cropped;

        var color = _frozen.GetPixel(px, py);
        MagnifierInfo.Text = $"{px},{py}   #{color.R:X2}{color.G:X2}{color.B:X2}";

        double x = dipPos.X + 24;
        double y = dipPos.Y + 24;
        if (x + MagnifierSize > Scene.ActualWidth) x = dipPos.X - MagnifierSize - 16;
        if (y + MagnifierHeight > Scene.ActualHeight) y = dipPos.Y - MagnifierHeight - 16;
        Canvas.SetLeft(Magnifier, x);
        Canvas.SetTop(Magnifier, y);
        Magnifier.Visibility = Visibility.Visible;
    }
}
