param(
  [string]$Keys = 'r',
  [long]$Hwnd = 30090270
)
Add-Type -Namespace Win32 -Name FW -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);'
[Win32.FW]::SetForegroundWindow([IntPtr]$Hwnd) | Out-Null
Start-Sleep -Milliseconds 400
$shell = New-Object -ComObject WScript.Shell
$shell.SendKeys($Keys)
Write-Output "sent '$Keys' to hwnd $Hwnd (foreground: $([Win32.FW]::GetForegroundWindow()))"
