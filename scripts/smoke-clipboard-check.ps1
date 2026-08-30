Add-Type -AssemblyName System.Windows.Forms
$img = [System.Windows.Forms.Clipboard]::GetImage()
if ($img) {
  $out = "D:\workspace\ZCodeProjects\Ta-Windows\scripts\clipboard-test.png"
  $img.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
  Write-Output "saved $($img.Width) x $($img.Height)"

  $minX = 999999; $minY = 999999; $maxX = -1; $maxY = -1
  for ($y = 0; $y -lt $img.Height; $y += 2) {
    for ($x = 0; $x -lt $img.Width; $x += 2) {
      $p = $img.GetPixel($x, $y)
      if ($p.R -gt 200 -and $p.G -lt 100 -and $p.B -lt 100) {
        if ($x -lt $minX) { $minX = $x }
        if ($y -lt $minY) { $minY = $y }
        if ($x -gt $maxX) { $maxX = $x }
        if ($y -gt $maxY) { $maxY = $y }
      }
    }
  }
  Write-Output "red bbox: ($minX,$minY) - ($maxX,$maxY)  expect (100,100)-(500,250)"
} else {
  Write-Output "clipboard: no image"
}
