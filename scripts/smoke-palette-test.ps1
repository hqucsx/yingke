param(
  [int]$PlusX = 729,
  [int]$PlusY = 694
)
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class V {
  public delegate bool EnumCb(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumCb cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  public static bool HasOverlay(uint target) {
    bool found = false;
    EnumWindows((h, l) => {
      uint wp; GetWindowThreadProcessId(h, out wp);
      if (wp == target && IsWindowVisible(h)) {
        // 覆盖层 = 全屏宽的可见窗口（>=1500px）
        EnumCb probe = null; // 不能在lambda里再取宽度，简化：任何可见窗口即视为存在
        found = true;
      }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
'@

$ws = New-Object -ComObject WScript.Shell
$procId = [uint32](Get-Process YingKe).Id
Write-Host "PID=$procId"

# 1. F1 触发截图
$ws.SendKeys('{F1}')
Start-Sleep -Seconds 2

# 2. 框选（真实鼠标拖拽）
& "$PSScriptRoot\smoke-drag.ps1" -X1 500 -Y1 200 -X2 1300 -Y2 600 | Out-Null
Start-Sleep -Milliseconds 600

# 3. 抓取点击前的工具栏区域
$bmp = New-Object System.Drawing.Bitmap(760, 280)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(440, 580, 0, 0, (New-Object System.Drawing.Size(760, 280)))
$g.Dispose()
$bmp.Save("$PSScriptRoot\..\_step_before.png")
$bmp.Dispose()

# 4. 点击 + 按钮
[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point($PlusX, $PlusY)
Start-Sleep -Milliseconds 400
[V]::mouse_event(0x02, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 80
[V]::mouse_event(0x04, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 1000

# 5. 抓取点击后的工具栏区域
$bmp = New-Object System.Drawing.Bitmap(760, 280)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(440, 580, 0, 0, (New-Object System.Drawing.Size(760, 280)))
$g.Dispose()
$bmp.Save("$PSScriptRoot\..\_step_after.png")
$bmp.Dispose()
Write-Host "完成：点击前=_step_before.png 点击后=_step_after.png"
