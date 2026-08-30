param([switch]$CallApi)
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class TaCred {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
  public struct CREDENTIAL {
    public int Flags; public int Type; public string TargetName; public string Comment;
    public long LastWritten; public int BlobSize; public IntPtr Blob; public int Persist;
    public int AttrCount; public IntPtr Attrs; public string TargetAlias; public string UserName;
  }
  [DllImport("advapi32.dll", CharSet=CharSet.Unicode)] public static extern bool CredRead(string target, int type, int flags, out IntPtr ptr);
  [DllImport("advapi32.dll")] public static extern void CredFree(IntPtr ptr);
  public static string Read(string target) {
    IntPtr ptr;
    if (!CredRead(target, 1, 0, out ptr)) return null;
    CREDENTIAL c = (CREDENTIAL)Marshal.PtrToStructure(ptr, typeof(CREDENTIAL));
    byte[] blob = new byte[c.BlobSize];
    Marshal.Copy(c.Blob, blob, 0, c.BlobSize);
    CredFree(ptr);
    return Encoding.Unicode.GetString(blob);
  }
}
"@

$key = [TaCred]::Read("Ta/ai.apikey")
if (-not $key) { Write-Output "api key: NOT SET"; exit 0 }
Write-Output "api key: present (len=$($key.Length), tail=$($key.Substring([Math]::Max(0,$key.Length-4))))"

if ($CallApi) {
  $headers = @{
    "x-api-key" = $key
    "anthropic-version" = "2023-06-01"
    "content-type" = "application/json"
  }
  $body = @{
    model = "glm-5.3-flash"
    max_tokens = 16
    messages = @(@{ role = "user"; content = "回复 pong 两个字母即可" })
  } | ConvertTo-Json -Depth 5
  try {
    $resp = Invoke-WebRequest -Uri "https://open.bigmodel.cn/api/anthropic/v1/messages" -Method Post -Headers $headers -Body $body -UseBasicParsing -TimeoutSec 30
    Write-Output "HTTP $($resp.StatusCode)"
    Write-Output ($resp.Content.Substring(0, [Math]::Min(300, $resp.Content.Length)))
  } catch {
    Write-Output "HTTP ERROR: $($_.Exception.Message)"
    if ($_.Exception.Response) {
      $stream = $_.Exception.Response.GetResponseStream()
      $reader = New-Object System.IO.StreamReader($stream)
      Write-Output ($reader.ReadToEnd().Substring(0, 400))
    }
  }
}
