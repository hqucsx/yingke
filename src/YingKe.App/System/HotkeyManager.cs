using YingKe.Core.Hotkeys;

namespace YingKe.App;

/// <summary>
/// 截图快捷键的注册管理：换绑时先注销旧键，注册失败返回 false，由调用方决定回滚。
/// </summary>
public sealed class HotkeyManager
{
    private readonly IntPtr _hwnd;

    public HotkeyManager(IntPtr hwnd) => _hwnd = hwnd;

    /// <summary>当前生效的修饰键与虚拟键（未注册成功时仅代表最近一次尝试值）。</summary>
    public uint Modifiers { get; private set; }
    public uint VirtualKey { get; private set; }
    public bool IsRegistered { get; private set; }

    public bool TrySet(uint modifiers, uint virtualKey)
    {
        Unregister();
        if (HotkeyApi.TryRegister(_hwnd, HotkeyWindow.IdCapture, modifiers, virtualKey))
        {
            Modifiers = modifiers;
            VirtualKey = virtualKey;
            IsRegistered = true;
            return true;
        }
        IsRegistered = false;
        return false;
    }

    public void Unregister()
    {
        HotkeyApi.Unregister(_hwnd, HotkeyWindow.IdCapture);
        IsRegistered = false;
    }
}
