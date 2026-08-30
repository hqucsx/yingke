param([int]$Vk = 0x52, [int]$ProcId = 0)
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class TaWin {
  public delegate bool EnumCb(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L; public int T; public int R; public int B; }
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumCb cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
  public static IntPtr FindLargestVisible(int pid) {
    IntPtr best = IntPtr.Zero; int bestW = 0;
    EnumWindows((h, l) => {
      uint wp; GetWindowThreadProcessId(h, out wp);
      if (wp == (uint)pid && IsWindowVisible(h)) {
        RECT r; GetWindowRect(h, out r);
        if (r.R - r.L > bestW) { bestW = r.R - r.L; best = h; }
      }
      return true;
    }, IntPtr.Zero);
    return best;
  }
  public static void PostKey(IntPtr h, int vk) {
    PostMessage(h, 0x0100, (IntPtr)vk, (IntPtr)1);
    PostMessage(h, 0x0102, (IntPtr)vk, (IntPtr)1);
    PostMessage(h, 0x0101, (IntPtr)vk, (IntPtr)0xC0000001);
  }
}
"@
if ($ProcId -eq 0) { Write-Output 'need -ProcId'; exit 1 }
$best = [TaWin]::FindLargestVisible($ProcId)
if ($best -eq [IntPtr]::Zero) { Write-Output "no visible window for pid $ProcId"; exit 1 }
[TaWin]::PostKey($best, $Vk)
Write-Output "posted VK 0x$($Vk.ToString('X2')) to hwnd=$best pid=$ProcId"
