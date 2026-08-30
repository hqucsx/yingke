param(
  [int]$X1 = 1192,
  [int]$Y1 = 180,
  [int]$X2 = 1905,
  [int]$Y2 = 450
)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Namespace Win32 -Name Mouse -MemberDefinition '[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);'

[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point($X1, $Y1)
Start-Sleep -Milliseconds 300
[Win32.Mouse]::mouse_event(0x02, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 150

$steps = 10
for ($i = 1; $i -le $steps; $i++) {
  $x = $X1 + [int](($X2 - $X1) * $i / $steps)
  $y = $Y1 + [int](($Y2 - $Y1) * $i / $steps)
  [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point($x, $y)
  Start-Sleep -Milliseconds 35
}
Start-Sleep -Milliseconds 250
[Win32.Mouse]::mouse_event(0x04, 0, 0, 0, [UIntPtr]::Zero)
Write-Output "drag done to $x,$y"
