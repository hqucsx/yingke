param([long]$Hwnd = 0, [string]$Keys = '')
Add-Type -Namespace Win32 -Name FW2 -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
'@

$fg = [Win32.FW2]::GetForegroundWindow()
$sb = New-Object System.Text.StringBuilder 256
[Win32.FW2]::GetWindowText($fg, $sb, 256) | Out-Null
Write-Output "foreground before: '$($sb.ToString())' (hwnd=$fg)"

if ($Hwnd -ne 0) {
  $ok = [Win32.FW2]::SetForegroundWindow([IntPtr]$Hwnd)
  Start-Sleep -Milliseconds 400
  $fg2 = [Win32.FW2]::GetForegroundWindow()
  $sb2 = New-Object System.Text.StringBuilder 256
  [Win32.FW2]::GetWindowText($fg2, $sb2, 256) | Out-Null
  Write-Output "SetForegroundWindow($Hwnd) => $ok; foreground after: '$($sb2.ToString())' (hwnd=$fg2)"
}

if ($Keys -ne '') {
  (New-Object -ComObject WScript.Shell).SendKeys($Keys)
  Write-Output "sent '$Keys'"
}
