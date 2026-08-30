using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using YingKe.Core.Native;

namespace YingKe.App;

/// <summary>
/// 托盘图标（Shell_NotifyIcon 原生实现，零第三方依赖）。
/// 左键 = 发起截图；右键 = 弹出上下文菜单；支持气泡提示。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const int WM_TRAY_CALLBACK = 0x8000 + 1; // WM_APP + 1
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;

    private const uint NIM_ADD = 0x0000;
    private const uint NIM_MODIFY = 0x0001;
    private const uint NIM_DELETE = 0x0002;
    private const uint NIF_MESSAGE = 0x0001;
    private const uint NIF_ICON = 0x0002;
    private const uint NIF_TIP = 0x0004;
    private const uint NIF_INFO = 0x0010;

    private NOTIFYICONDATAW _data;
    private readonly System.Drawing.Icon _icon; // 持有引用，防止 GC 终结器销毁图标句柄
    private bool _added;

    public event Action? LeftClicked;
    public event Action? MenuRequested;

    public TrayIconService(IntPtr ownerHwnd, System.Drawing.Icon icon, string tooltip)
    {
        _icon = icon;
        _data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = ownerHwnd, // 回调消息的接收窗口；缺省为 IntPtr.Zero 会导致图标可见但点击无响应
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAY_CALLBACK,
            hIcon = icon.Handle,
        };
        _data.szTip = tooltip ?? string.Empty;
        _added = Shell_NotifyIcon(NIM_ADD, ref _data);
    }

    /// <summary>接入宿主窗口过程；返回 true 表示消息已消费。</summary>
    public bool HandleWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != WM_TRAY_CALLBACK || !_added)
            return false;

        int mouse = lParam.ToInt32();
        if (mouse == WM_LBUTTONUP)
            LeftClicked?.Invoke();
        else if (mouse == WM_RBUTTONUP)
            MenuRequested?.Invoke();
        return true;
    }

    /// <summary>在光标处弹出托盘菜单（SetForegroundWindow 是菜单外部点击可关闭的关键）。</summary>
    public void ShowContextMenu(IntPtr ownerHwnd, Func<ContextMenu> menuFactory)
    {
        NativeMethods.GetCursorPos(out var pt);
        var menu = menuFactory();
        menu.Placement = PlacementMode.AbsolutePoint;
        menu.HorizontalOffset = pt.X;
        menu.VerticalOffset = pt.Y;
        SetForegroundWindowFor(ownerHwnd);
        menu.IsOpen = true;
    }

    private static void SetForegroundWindowFor(IntPtr hwnd) => NativeMethods.SetForegroundWindow(hwnd);

    public void ShowBalloonTip(string title, string text)
    {
        if (!_added) return;
        // Win11 会把托盘气泡转成 toast，且 app 名/图标按历史路径哈希解析，
        // 换品牌后会残留旧身份；改为直接发 WinRT Toast 并显式携带 AUMID，
        // 头部渲染的 app 名与图标即来自安装器注册的同名快捷方式（映刻/蓝logo）。
        if (ShowToast(title, text)) return;

        // 兜底：WinRT 通知不可用时退回传统气泡
        var data = _data;
        data.uFlags = NIF_INFO;
        data.szInfoTitle = title;
        data.szInfo = text;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private static bool ShowToast(string title, string text)
    {
        try
        {
            const string appId = "YingKe.App"; // 与 App.OnStartup 设置及安装器快捷方式一致
            var xml = new Windows.Data.Xml.Dom.XmlDocument();
            xml.LoadXml(
                "<toast activationType=\"system\"><visual><binding template=\"ToastGeneric\">" +
                $"<text>{Escape(title)}</text><text>{Escape(text)}</text>" +
                "</binding></visual></toast>");
            var toast = new Windows.UI.Notifications.ToastNotification(xml);
            Windows.UI.Notifications.ToastNotificationManager
                .CreateToastNotifier(appId).Show(toast);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Escape(string s) =>
        System.Security.SecurityElement.Escape(s ?? string.Empty) ?? string.Empty;

    public void Dispose()
    {
        if (_added)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _data);
            _added = false;
        }
        _icon.Dispose();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
    }
}
