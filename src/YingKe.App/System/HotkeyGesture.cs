using System.Text;
using System.Windows.Input;

namespace YingKe.App;

/// <summary>快捷键的展示与 WPF/Win32 转换。</summary>
public static class HotkeyGesture
{
    public static string Describe(uint modifiers, uint virtualKey)
    {
        var sb = new StringBuilder();
        if ((modifiers & YingKe.Core.Native.NativeMethods.MOD_CONTROL) != 0) sb.Append("Ctrl+");
        if ((modifiers & YingKe.Core.Native.NativeMethods.MOD_SHIFT) != 0) sb.Append("Shift+");
        if ((modifiers & YingKe.Core.Native.NativeMethods.MOD_ALT) != 0) sb.Append("Alt+");
        if ((modifiers & YingKe.Core.Native.NativeMethods.MOD_WIN) != 0) sb.Append("Win+");
        sb.Append(KeyName(virtualKey));
        return sb.ToString();
    }

    public static uint ToWin32Modifiers(ModifierKeys modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= YingKe.Core.Native.NativeMethods.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= YingKe.Core.Native.NativeMethods.MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= YingKe.Core.Native.NativeMethods.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= YingKe.Core.Native.NativeMethods.MOD_WIN;
        return result;
    }

    private static string KeyName(uint virtualKey)
    {
        // 数字与字母的虚拟键码即 ASCII
        if (virtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
            return ((char)virtualKey).ToString();

        try
        {
            var key = KeyInterop.KeyFromVirtualKey((int)virtualKey);
            var name = key.ToString();
            if (name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1]))
                return name[1..];
            return name;
        }
        catch
        {
            return $"0x{virtualKey:X2}";
        }
    }
}
