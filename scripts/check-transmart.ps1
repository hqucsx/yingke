$url = "https://transmart.qq.com/api/imt"
$headers = @{
  "User-Agent" = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/110.0.0.0 Safari/537.36"
  "Referer" = "https://yi.qq.com/zh-CN/index"
  "Content-Type" = "application/json"
}
$body = @'
{
  "header": { "fn": "auto_translation_block", "client_key": "browser-chrome-110.0.0-Mac OS-df4bd4c5-a65d-44b2-a40f-42f34f3535f2-1677486696487" },
  "type": "plain",
  "model_category": "normal",
  "source": { "lang": "en", "text_block": "The quick brown fox jumps over the lazy dog. Ta is an AI native screenshot tool." },
  "target": { "lang": "zh" }
}
'@
try {
  $resp = Invoke-WebRequest -Uri $url -Method Post -Headers $headers -Body $body -UseBasicParsing -TimeoutSec 20
  Write-Output "HTTP $($resp.StatusCode): $($resp.Content)"
} catch {
  Write-Output "FAIL: $($_.Exception.Message)"
}
