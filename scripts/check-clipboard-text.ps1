Add-Type -AssemblyName System.Windows.Forms
try {
  $t = [System.Windows.Forms.Clipboard]::GetText()
  if ($t) {
    $preview = $t -replace "`r`n", " / "
    if ($preview.Length -gt 100) { $preview = $preview.Substring(0, 100) + "..." }
    Write-Output "clipboard text ($($t.Length) chars): $preview"
  } else {
    Write-Output "clipboard: no text"
  }
} catch {
  Write-Output "clipboard read error: $($_.Exception.Message)"
}
Write-Output "=== last error log entries ==="
$log = "$env:TEMP\ta-error.log"
if (Test-Path $log) { Get-Content $log -Tail 6 | Where-Object { $_ -match '^\[' } } else { Write-Output "no error log" }
