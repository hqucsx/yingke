using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;

namespace YingKe.App.Media;

/// <summary>
/// 剪贴板写入（参考 STranslate/TextCopy 的 Win32 原生方案）：
/// 文本不走 WPF/OleSetClipboard（易被剪贴板管理器卡住），而是
/// OpenClipboard(重试) → EmptyClipboard → GlobalAlloc + SetClipboardData(CF_UNICODETEXT) → CloseClipboard。
/// 图像仍走 WPF SetImage + 重试。
/// </summary>
public static class ClipboardHelper
{
    /// <summary>全局剪贴板闸门：串行化应用内所有写入（前台 + 后台重试），避免自我竞争。</summary>
    public static readonly SemaphoreSlim Gate = new(1, 1);

    private const uint CF_UNICODETEXT = 13;
    private const int OpenRetryCount = 10;
    private const int OpenRetryDelayMs = 100;

    // ---- 文本：Win32 原生直写 ----

    public static void SetText(string text)
    {
        text ??= string.Empty;
        for (int round = 0; ; round++)
        {
            try
            {
                SetTextNative(text);
                return;
            }
            catch (Exception ex) when (round < 2)
            {
                Thread.Sleep(200); // 原生打开已内置 10×100ms 重试，外层再兜底两轮
                if (round == 1 && ex is not Win32Exception) throw;
            }
        }
    }

    private static void SetTextNative(string text)
    {
        if (!TryOpenClipboard())
            throw new Win32Exception("打开剪贴板失败：被其他程序持续占用。");

        IntPtr hGlobal = IntPtr.Zero;
        try
        {
            if (!EmptyClipboard())
                throw new Win32Exception(Marshal.GetLastWin32Error(), "EmptyClipboard 失败。");

            var bytes = Encoding.Unicode.GetByteCount(text) + 2; // UTF-16 + '\0'
            hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
            if (hGlobal == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GlobalAlloc 失败。");

            var target = GlobalLock(hGlobal);
            if (target == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GlobalLock 失败。");

            try
            {
                var textBytes = Encoding.Unicode.GetBytes(text + '\0');
                Marshal.Copy(textBytes, 0, target, textBytes.Length);
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetClipboardData 失败。");

            // 数据所有权已交给系统
            hGlobal = IntPtr.Zero;
        }
        finally
        {
            if (hGlobal != IntPtr.Zero)
                GlobalFree(hGlobal);
            CloseClipboard();
        }
    }

    private static bool TryOpenClipboard()
    {
        for (int attempt = 0; ; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
                return true;
            if (attempt >= OpenRetryCount)
                return false;
            Thread.Sleep(OpenRetryDelayMs);
        }
    }

    // ---- 图像：WPF 路径 + 重试 ----

    public static void SetImage(BitmapSource image, int retries = 10)
    {
        COMException? last = null;
        for (int attempt = 0; attempt <= retries; attempt++)
        {
            try
            {
                Clipboard.SetImage(image);
                return;
            }
            catch (COMException ex) when (ex.ErrorCode == unchecked((int)0x800401D0))
            {
                last = ex;
                if (attempt < retries) Thread.Sleep(150);
            }
        }
        throw new InvalidOperationException(
            "写入剪贴板失败：剪贴板被其他程序持续占用（如微信/输入法/安全软件），请稍后重试。", last);
    }

    // ---- Win32 P/Invoke ----

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    private const uint GMEM_MOVEABLE = 0x0002;
}
