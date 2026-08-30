param([int]$X = 1226, [int]$Y = 480)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Namespace Win32 -Name Mouse -MemberDefinition '[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);'
[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point($X, $Y)
Start-Sleep -Milliseconds 250
[Win32.Mouse]::mouse_event(0x02, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 80
[Win32.Mouse]::mouse_event(0x04, 0, 0, 0, [UIntPtr]::Zero)
Write-Output "clicked $X,$Y"
