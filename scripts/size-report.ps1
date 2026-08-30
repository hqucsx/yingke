$dir = "D:\workspace\ZCodeProjects\Ta-Windows\src\YingKe.App\bin\Debug\net8.0-windows10.0.19041.0"
Get-ChildItem $dir -File -ErrorAction SilentlyContinue |
  Sort-Object Length -Descending | Select-Object -First 12 Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}} |
  Format-Table -AutoSize
