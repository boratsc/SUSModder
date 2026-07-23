param([string]$V2Base = "https://api.susmodder-cdn.ovh/v2")

$token = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(
    "YTBlMjNiZWFmMTBlZjQxZGM1ZjRiN2NhNGYxOWZmNDkyNDI4MjEyNWMzNzQ5Mzk5MjIwYzA5MDMzNGU3NGI4Mw=="))
$sha = [Security.Cryptography.SHA256]::Create()
try {
    $bytes = [Text.Encoding]::UTF8.GetBytes([guid]::NewGuid().ToString())
    $hash = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-", "").ToLowerInvariant()
}
finally { $sha.Dispose() }

$headers = @{
    Authorization = $token
    "X-User-Hash" = $hash
    "User-Agent"  = "SUSModder/3.0"
}

function Try-Create([string]$Name, [string]$Json) {
    Write-Host ""
    Write-Host ("== {0} ==" -f $Name) -ForegroundColor Cyan
    Write-Host $Json
    try {
        $r = Invoke-WebRequest -Uri ("{0}/modpacks" -f $V2Base.TrimEnd('/')) -Method POST -Headers $headers -Body $Json -ContentType "application/json" -UseBasicParsing -TimeoutSec 30
        $snippet = $r.Content.Substring(0, [Math]::Min(220, $r.Content.Length))
        Write-Host ("HTTP {0} {1}" -f [int]$r.StatusCode, $snippet) -ForegroundColor Green
    }
    catch {
        $resp = $_.Exception.Response
        if ($resp) {
            $code = [int]$resp.StatusCode
            $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
            $body = $reader.ReadToEnd()
            $reader.Close()
            Write-Host ("HTTP {0} body='{1}'" -f $code, $body) -ForegroundColor Red
        }
        else {
            Write-Host $_.Exception.Message -ForegroundColor Red
        }
    }
}

Try-Create "omit fullModId" (@{
    creatorHash = $hash
    creatorName = "t"
    fullModVersion = "custom"
    modName = "t1"
    ttlDays = 7
    dllMods = @()
    metadata = @{ amongVersion = "2024-6-18" }
} | ConvertTo-Json -Depth 6 -Compress)

Try-Create "fullModId null JSON" ("{`"creatorHash`":`"$hash`",`"creatorName`":`"t`",`"fullModId`":null,`"fullModVersion`":`"custom`",`"modName`":`"t2`",`"ttlDays`":7,`"dllMods`":[],`"metadata`":{`"amongVersion`":`"2024-6-18`"}}")

Try-Create "fullModId 0" ("{`"creatorHash`":`"$hash`",`"creatorName`":`"t`",`"fullModId`":0,`"fullModVersion`":`"custom`",`"modName`":`"t3`",`"ttlDays`":7,`"dllMods`":[],`"metadata`":{`"amongVersion`":`"2024-6-18`"}}")

Try-Create "hasCustomFullMod true" (@{
    creatorHash = $hash
    creatorName = "t"
    fullModVersion = "custom"
    modName = "t4"
    ttlDays = 7
    dllMods = @()
    hasCustomFullMod = $true
    amongVersion = "2024-6-18"
    metadata = @{ amongVersion = "2024-6-18" }
} | ConvertTo-Json -Depth 6 -Compress)
