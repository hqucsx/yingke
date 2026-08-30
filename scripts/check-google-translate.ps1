$text = [Uri]::EscapeDataString("The quick brown fox jumps over the lazy dog")
$variants = @(
  @{ name = "gtx via sysproxy"; url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=zh-CN&dt=t&q=$text"; noproxy = $false },
  @{ name = "clients5 via sysproxy"; url = "https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl=auto&tl=zh-CN&q=$text"; noproxy = $false },
  @{ name = "gtx direct"; url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=zh-CN&dt=t&q=$text"; noproxy = $true }
)

foreach ($v in $variants) {
  Write-Output "=== $($v.name) ==="
  try {
    if ($v.noproxy) { [System.Net.WebRequest]::DefaultWebProxy = $null }
    else { [System.Net.WebRequest]::DefaultWebProxy = [System.Net.WebRequest]::GetSystemWebProxy() }
    $resp = Invoke-WebRequest -Uri $v.url -UseBasicParsing -TimeoutSec 15
    $content = $resp.Content
    if ($content.Length -gt 200) { $content = $content.Substring(0, 200) }
    Write-Output "OK HTTP $($resp.StatusCode): $content"
    break
  } catch {
    Write-Output "FAIL: $($_.Exception.Message)"
  }
}
