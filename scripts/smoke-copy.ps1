(New-Object -ComObject WScript.Shell).SendKeys('~')
Start-Sleep -Milliseconds 800
Add-Type -AssemblyName System.Windows.Forms
$img = [System.Windows.Forms.Clipboard]::GetImage()
if ($img) { Write-Output "clipboard image: $($img.Width) x $($img.Height)" }
else { Write-Output "clipboard: no image" }
