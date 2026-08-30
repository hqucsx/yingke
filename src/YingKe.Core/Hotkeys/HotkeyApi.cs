using YingKe.Core.Native;

namespace YingKe.Core.Hotkeys;

/// <summary>
/// 全局热键注册的薄封装（RegisterHotKey）。注册失败即快捷键被其他程序占用（PRD F-02）。
/// WM_HOTKEY 消息由调用方窗口过程接收。
/// </summary>
public static class HotkeyApi
{
    public static bool TryRegister(IntPtr hwnd, int id, uint fsModifiers, uint virtualKey)
        => NativeMethods.RegisterHotKey(hwnd, id, fsModifiers | NativeMethods.MOD_NOREPEAT, virtualKey);

    public static void Unregister(IntPtr hwnd, int id)
        => NativeMethods.UnregisterHotKey(hwnd, id);
}
