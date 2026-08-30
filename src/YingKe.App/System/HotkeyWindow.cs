using System.Windows;
using System.Windows.Interop;
using YingKe.Core.Hotkeys;

namespace YingKe.App;

/// <summary>
/// 0×0 隐藏窗口：承载 RegisterHotKey 的目标句柄与托盘回调消息。
/// WPF 窗口 Hide 后句柄仍存活并继续处理消息。
/// </summary>
public sealed class HotkeyWindow : Window
{
    public const int IdCapture = 1;

    /// <summary>托盘等外部组件的消息过滤器：(msg, wParam, lParam) → 是否已处理。</summary>
    public Func<IntPtr, int, IntPtr, IntPtr, bool>? ExternalMessageHandler { get; set; }

    public event Action<int>? HotkeyPressed;

    public IntPtr Handle { get; private set; }

    public HotkeyWindow()
    {
        Width = 0;
        Height = 0;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        ShowInTaskbar = false;
        Title = "Ta";
        Visibility = Visibility.Hidden;
    }

    /// <summary>显示一次以创建句柄，随后隐藏；句柄继续存活。</summary>
    public void Start()
    {
        Show();
        Hide();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(Handle)!;
        source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == YingKe.Core.Native.NativeMethods.WM_HOTKEY)
        {
            HotkeyPressed?.Invoke(wParam.ToInt32());
            handled = true;
            return IntPtr.Zero;
        }

        if (ExternalMessageHandler?.Invoke(hwnd, msg, wParam, lParam) == true)
            handled = true;

        return IntPtr.Zero;
    }
}
