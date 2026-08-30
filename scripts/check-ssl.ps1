param([int]$Times = 3)
$exe = "D:\workspace\ZCodeProjects\Ta-Windows\src\YingKe.App\bin\Debug\net8.0-windows10.0.19041.0\YingKe.exe"
for ($i = 1; $i -le $Times; $i++) {
  $f = "$env:TEMP\ta-apitest-result.txt"
  Remove-Item $f -ErrorAction SilentlyContinue
  Start-Process -FilePath $exe -ArgumentList "--apitest" -Wait
  Write-Output "--- run $i ---"
  Get-Content $f | Select-Object -First 4
}
Write-Output "=== system proxy ==="
netsh winhttp show proxy
$reg = Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings"
Write-Output "ProxyEnable: $($reg.ProxyEnable)  ProxyServer: $($reg.ProxyServer)"
