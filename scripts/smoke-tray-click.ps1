Add-Type -AssemblyName System.Windows.Forms
Add-Type -Namespace Win32 -Name Mouse -MemberDefinition '[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);'

[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(1902, 1058)
Start-Sleep -Milliseconds 400
[Win32.Mouse]::mouse_event(0x02, 0, 0, 0, [UIntPtr]::Zero)  # left down
Start-Sleep -Milliseconds 80
[Win32.Mouse]::mouse_event(0x04, 0, 0, 0, [UIntPtr]::Zero)  # left up
Write-Output "clicked tray at 1902,1058"
