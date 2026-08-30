param(
  [int]$Vk = 0x51,        # 触发的功能键：Q=取字
  [int]$SampleMs = 16000  # 采样时长
)
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class MemWinCheck {
  public delegate bool EnumCb(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumCb cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
  public static int CountVisible(uint target) {
    int n = 0;
    EnumWindows((h, l) => {
      uint wp; GetWindowThreadProcessId(h, out wp);
      if (wp == target && IsWindowVisible(h)) n++;
      return true;
    }, IntPtr.Zero);
    return n;
  }
}
'@

$ws = New-Object -ComObject WScript.Shell
$procId = [uint32](Get-Process YingKe).Id
Write-Host "目标 PID=$procId 功能键=$([char]$Vk)"

$ws.SendKeys('{F1}')
Start-Sleep -Seconds 2

& "$PSScriptRoot\smoke-drag.ps1" -X1 620 -Y1 300 -X2 1400 -Y2 500 | Out-Null
Start-Sleep -Milliseconds 500
& "$PSScriptRoot\smoke-postkey.ps1" -Vk $Vk -ProcId $procId | Out-Null

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$last = -1
while ($sw.ElapsedMilliseconds -lt $SampleMs) {
  $n = [MemWinCheck]::CountVisible($procId)
  if ($n -ne $last) {
    Write-Host ("T+" + [math]::Round($sw.ElapsedMilliseconds / 1000.0, 1) + "s 可见窗口=" + $n)
    $last = $n
  }
  Start-Sleep -Milliseconds 150
}
$clip = Get-Clipboard -Raw -ErrorAction SilentlyContinue
if ($clip) { Write-Host ("剪贴板前40字: " + $clip.Substring(0, [Math]::Min(40, $clip.Length)).Replace("`r", "").Replace("`n", " ")) }
else { Write-Host "剪贴板空" }
