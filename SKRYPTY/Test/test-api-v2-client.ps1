# SUSModder API v2 - client-aligned E2E verification (post-remediation)
param(
    [string]$V2Base = "https://api.susmodder-cdn.ovh/v2",
    [string]$AuthToken = ""
)

$results = [System.Collections.Generic.List[object]]::new()

function Add-R {
    param([string]$Area, [string]$Test, [string]$Verdict, [string]$Detail)
    $script:results.Add([pscustomobject]@{ Area=$Area; Test=$Test; Verdict=$Verdict; Detail=$Detail })
}

function Get-Status {
    param([string]$Url, [string]$Method = "GET", [hashtable]$Headers = @{}, [string]$Body = $null)
    try {
        $p = @{ Uri=$Url; Method=$Method; Headers=$Headers; TimeoutSec=30; UseBasicParsing=$true }
        if ($Body) { $p.Body=$Body; $p.ContentType="application/json" }
        $r = Invoke-WebRequest @p
        return @{ Code=[int]$r.StatusCode; Body=$r.Content }
    } catch {
        $code = 0; $body = $_.Exception.Message
        if ($_.Exception.Response) {
            $code = [int]$_.Exception.Response.StatusCode.value__
            try {
                $sr = New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())
                $body = $sr.ReadToEnd(); $sr.Close()
            } catch {}
        }
        return @{ Code=$code; Body=$body }
    }
}

function Get-FinalDownloadCode {
    param([string]$Url)
    try {
        $r = Invoke-WebRequest -Uri $Url -Method Head -MaximumRedirection 5 -UseBasicParsing
        return [int]$r.StatusCode
    } catch {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode.value__ }
        return 0
    }
}

Write-Host "=== SUSModder API v2 (client-aligned) ===" -ForegroundColor Cyan

# --- Catalog ---
$c = Get-Status "$V2Base/catalog?limit=50"
if ($c.Code -eq 200) {
    Add-R "catalog" "GET /catalog" "OK" "200, $($((($c.Body | ConvertFrom-Json).data).Count)) mods"
    $catalog = ($c.Body | ConvertFrom-Json).data
    $nullIcons = @($catalog | Where-Object { -not $_.iconUrl }).Count
    if ($nullIcons -eq 0) { Add-R "catalog" "iconUrl present" "OK" "20/20" }
    else { Add-R "catalog" "iconUrl present" "FAIL" "$nullIcons/20 null" }
} else { Add-R "catalog" "GET /catalog" "FAIL" "HTTP $($c.Code)" }

foreach ($ep in @("catalog/1", "catalog/1/versions", "catalog-meta")) {
    $r = Get-Status "$V2Base/$ep"
    Add-R "catalog" "GET /$ep" $(if ($r.Code -eq 200){"OK"}else{"FAIL"}) "HTTP $($r.Code)"
}

# --- Versions ---
foreach ($ep in @("versions", "versions/2025-3-31", "versions/2026-3-31/steam", "versions/2026-3-31/epic")) {
    $r = Get-Status "$V2Base/$ep"
    Add-R "versions" "GET /$ep" $(if ($r.Code -eq 200){"OK"}else{"FAIL"}) "HTTP $($r.Code)"
}

# --- Downloads (all mods) ---
$failDl = @(); $okDl = 0
foreach ($m in $catalog) {
    $d = Get-Status "$V2Base/catalog/$($m.id)"
    if ($d.Code -ne 200) { $failDl += "mod $($m.id) detail $($d.Code)"; continue }
    $detail = ($d.Body | ConvertFrom-Json).data
    if (-not $detail.variants -or $detail.variants.Count -eq 0) {
        $failDl += "mod $($m.id) no variants"; continue
    }
    $ver = [uri]::EscapeDataString($detail.currentVersion)
    $url = "$V2Base/downloads/mod/$($m.id)/$ver`?platform=steam&arch=x86"
    $fc = Get-FinalDownloadCode $url
    if ($fc -eq 200) { $okDl++ } else { $failDl += "mod $($m.id) $($m.name) -> $fc" }
}
if ($failDl.Count -eq 0) { Add-R "downloads" "all mods steam x86" "OK" "$okDl/$($catalog.Count) -> 200" }
else { Add-R "downloads" "all mods steam x86" "FAIL" ($failDl -join "; ") }

# --- Compatibility ---
$r1 = Get-Status "$V2Base/compatibility?fullModId=1&dllModId=10"
Add-R "compat" "GET /compatibility?fullModId&dllModId" $(if ($r1.Code -eq 200){"OK"}else{"FAIL"}) "HTTP $($r1.Code)"
$r2 = Get-Status "$V2Base/compatibility/snapshot"
Add-R "compat" "GET /compatibility/snapshot" $(if ($r2.Code -eq 200){"OK"}else{"FAIL"}) "HTTP $($r2.Code)"

# --- Roles, Discord, Public ---
foreach ($item in @(
    @{ A="roles"; T="GET /roles"; U="roles" },
    @{ A="discord"; T="GET /discord/favs/public"; U="discord/favs/public" },
    @{ A="discord"; T="GET /discord/server-counts"; U="discord/server-counts" },
    @{ A="public"; T="GET /releases?channel=release"; U="releases?channel=release" },
    @{ A="public"; T="GET /releases?channel=beta"; U="releases?channel=beta" },
    @{ A="public"; T="GET /telemetry/stats"; U="telemetry/stats" },
    @{ A="public"; T="GET /telemetry/health"; U="telemetry/health" },
    @{ A="public"; T="GET /online"; U="online" },
    @{ A="public"; T="GET /virustotal/report"; U="virustotal/report" }
)) {
    $r = Get-Status "$V2Base/$($item.U)"
    Add-R $item.A $item.T $(if ($r.Code -eq 200){"OK"}else{"FAIL"}) "HTTP $($r.Code)"
}

# --- Icons v2 route ---
$icon = Get-Status "$V2Base/icons/10.png"
if ($icon.Code -in 200,302) { Add-R "icons" "GET /icons/10.png" "OK" "HTTP $($icon.Code)" }
else { Add-R "icons" "GET /icons/10.png" "FAIL" "HTTP $($icon.Code)" }

# --- Telemetry heartbeat ---
$hbPath = Join-Path $PSScriptRoot "_hb-payload.json"
if (-not (Test-Path $hbPath)) {
    @{ userHash="a0516c62cae89f455520ec5f5355086854eef12ebec970a8634287d1849dd348"; appVersion="1.0.0"; platform="steam"; language="pl"; installedModIds=@(1,13); sessionTimeSeconds=60; timestamp=(Get-Date).ToUniversalTime().ToString("o") } | ConvertTo-Json -Compress | Set-Content $hbPath -Encoding UTF8
}
$hb = Get-Content $hbPath -Raw
$hbR = Get-Status "$V2Base/telemetry/heartbeat" -Method POST -Body $hb
$hbVerdict = if ($hbR.Code -in 200,201,204) { "OK" } elseif ($hbR.Code -eq 429) { "OK" } else { "FAIL" }
$hbDetail = if ($hbR.Code -eq 429) { "HTTP 429 (rate limit - endpoint dziala)" } else { "HTTP $($hbR.Code)" }
Add-R "telemetry" "POST /telemetry/heartbeat" $hbVerdict $hbDetail

# --- Lobby ---
$lh = @{}
if ($AuthToken) { $lh["Authorization"] = $AuthToken }
$lobby = Get-Status "$V2Base/lobby?limit=5" -Headers $lh
if ($AuthToken -and $lobby.Code -eq 200) { Add-R "lobby" "GET /lobby (auth)" "OK" "HTTP 200" }
elseif (-not $AuthToken -and $lobby.Code -eq 401) { Add-R "lobby" "GET /lobby (no auth)" "EXPECTED" "401 - wymaga Bearer token" }
elseif ($AuthToken) { Add-R "lobby" "GET /lobby (auth)" "FAIL" "HTTP $($lobby.Code)" }
else { Add-R "lobby" "GET /lobby" "FAIL" "HTTP $($lobby.Code)" }

# --- Modpacks route exists ---
$mp = Get-Status "$V2Base/modpacks/INVALID-CODE"
Add-R "modpacks" "GET /modpacks/:code" $(if ($mp.Code -in 400,404){"OK"}else{"WARN"}) "HTTP $($mp.Code) (route exists)"

# --- Summary ---
Write-Host ""
$ok = @($results | Where-Object Verdict -eq "OK").Count
$fail = @($results | Where-Object Verdict -eq "FAIL").Count
$exp = @($results | Where-Object Verdict -eq "EXPECTED").Count
Write-Host "OK: $ok | FAIL: $fail | EXPECTED: $exp" -ForegroundColor $(if ($fail -eq 0){"Green"}else{"Red"})

if ($fail -gt 0) {
    Write-Host "`nFAIL:" -ForegroundColor Red
    $results | Where-Object Verdict -eq "FAIL" | Format-Table Area, Test, Detail -AutoSize
}

$out = Join-Path $PSScriptRoot "api-v2-client-results-$(Get-Date -Format 'yyyy-MM-dd-HHmm').json"
$results | ConvertTo-Json | Set-Content $out -Encoding UTF8
Write-Host "Saved: $out"
