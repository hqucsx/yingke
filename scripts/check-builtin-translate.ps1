$ErrorActionPreference = "Continue"
$variants = @(
  @{ name = "direct+UA";   proxy = $false; ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0" },
  @{ name = "direct+noUA"; proxy = $false; ua = $null },
  @{ name = "sysproxy+UA"; proxy = $true;  ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0" }
)

foreach ($v in $variants) {
  Write-Output "=== $($v.name) ==="
  try {
    if (-not $v.proxy) { [System.Net.WebRequest]::DefaultWebProxy = $null }
    else { [System.Net.WebRequest]::DefaultWebProxy = [System.Net.WebRequest]::GetSystemWebProxy() }
    $headers = @{ }
    if ($v.ua) { $headers["User-Agent"] = $v.ua }
    $auth = Invoke-WebRequest -Uri "https://edge.microsoft.com/translate/auth" -Headers $headers -UseBasicParsing -TimeoutSec 15
    $token = $auth.Content.Trim()
    Write-Output "auth OK, token $($token.Length) chars"
    $headers2 = @{ "Authorization" = "Bearer $token" }
    if ($v.ua) { $headers2["User-Agent"] = $v.ua }
    $body = '[{"Text":"The quick brown fox jumps over the lazy dog"}]'
    $resp = Invoke-WebRequest -Uri "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&to=zh-Hans" `
      -Method Post -Headers $headers2 -Body $body -ContentType "application/json" -UseBasicParsing -TimeoutSec 15
    Write-Output "translate OK: $($resp.Content)"
    break
  } catch {
    Write-Output "FAIL: $($_.Exception.Message)"
  }
}
