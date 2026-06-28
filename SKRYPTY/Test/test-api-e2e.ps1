# SUSModder API E2E smoke tests - v2 + legacy + CDN assets
# Usage: .\SKRYPTY\Test\test-api-e2e.ps1 [-AuthToken "Bearer ..."] [-AdminSecret "..."]

param(
    [string]$V2Base = "https://api.susmodder-cdn.ovh/v2",
    [string]$LegacyBase = "https://susmodder.app",
    [string]$CdnBase = "https://susmodder-cdn.ovh",
    [string]$AuthToken = "",
    [string]$AdminSecret = "",
    [string]$UserHash = "a0516c62cae89f455520ec5f5355086854eef12ebec970a8634287d1849dd348"
)

$ErrorActionPreference = "Continue"
$results = [System.Collections.Generic.List[object]]::new()

function Add-Result {
    param(
        [string]$Group,
        [string]$Method,
        [string]$Url,
        [int]$Status,
        [string]$Verdict,   # OK | EXPECTED | FAIL | WARN
        [string]$Reason,
        [string]$BodyPreview = ""
    )
    $script:results.Add([pscustomobject]@{
        Group       = $Group
        Method      = $Method
        Url         = $Url
        Status      = $Status
        Verdict     = $Verdict
        Reason      = $Reason
        BodyPreview = $BodyPreview
    })
}

function Invoke-ApiTest {
    param(
        [string]$Group,
        [string]$Method = "GET",
        [string]$Path,
        [string]$Base = $V2Base,
        [hashtable]$Headers = @{},
        [string]$Body = $null,
        [scriptblock]$Evaluate
    )

    $url = if ($Path -match '^https?://') { $Path } else { "$($Base.TrimEnd('/'))/$($Path.TrimStart('/'))" }

    try {
        $params = @{
            Uri             = $url
            Method          = $Method
            Headers         = $Headers
            TimeoutSec      = 25
            UseBasicParsing = $true
        }
        if ($Body) {
            $params.Body = $Body
            $params.ContentType = "application/json"
        }

        $resp = Invoke-WebRequest @params
        $status = [int]$resp.StatusCode
        $text = $resp.Content
        if ($text.Length -gt 300) { $text = $text.Substring(0, 300) + "..." }

        if ($Evaluate) {
            & $Evaluate $status $resp.Content $resp.Headers
        } else {
            Add-Result $Group $Method $url $status "OK" "HTTP $status" $text
        }
    }
    catch {
        $status = 0
        $text = $_.Exception.Message
        if ($_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode.value__
            try {
                $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                $text = $reader.ReadToEnd()
                $reader.Close()
            } catch {}
        }
        $preview = if ($text.Length -gt 300) { $text.Substring(0, 300) + "..." } else { $text }

        if ($Evaluate) {
            & $Evaluate $status $text @{}
        } else {
            Add-Result $Group $Method $url $status "FAIL" $text $preview
        }
    }
}

function Test-DownloadRedirect {
    param([string]$Group, [int]$ModId, [string]$Version, [string]$Platform = "steam")

    $url = "$V2Base/downloads/mod/$ModId/$([uri]::EscapeDataString($Version))?platform=$Platform&arch=x86"
    try {
        $resp = Invoke-WebRequest -Uri $url -Method GET -MaximumRedirection 0 -ErrorAction SilentlyContinue -UseBasicParsing
        $status = [int]$resp.StatusCode
        $loc = $resp.Headers.Location
        Add-Result $Group "GET" $url $status "OK" "Bez redirectu" ($resp.Content.Substring(0, [Math]::Min(200, $resp.Content.Length)))
    }
    catch {
        $status = [int]$_.Exception.Response.StatusCode.value__
        $loc = $_.Exception.Response.Headers["Location"]
        $finalStatus = $null
        $finalReason = ""

        if ($loc) {
            try {
                $final = Invoke-WebRequest -Uri $loc -Method HEAD -UseBasicParsing -TimeoutSec 15
                $finalStatus = [int]$final.StatusCode
            }
            catch {
                if ($_.Exception.Response) { $finalStatus = [int]$_.Exception.Response.StatusCode.value__ }
            }
            if ($finalStatus -eq 200) {
                Add-Result $Group "GET-HEAD" "$url - $loc" $status "OK" "302 - CDN 200" ""
            } elseif ($finalStatus -eq 404) {
                Add-Result $Group "GET-HEAD" "$url - $loc" $status "FAIL" "API 302 ale plik na CDN = 404 (brak artefaktu)" ""
            } else {
                Add-Result $Group "GET-HEAD" "$url - $loc" $status "WARN" "302 - CDN HTTP $finalStatus" ""
            }
        } elseif ($status -eq 404) {
            Add-Result $Group "GET" $url $status "FAIL" "VARIANT_NOT_FOUND - brak wariantu w DB" ""
        } else {
            Add-Result $Group "GET" $url $status "FAIL" $_.Exception.Message ""
        }
    }
}

Write-Host "=== SUSModder API E2E ===" -ForegroundColor Cyan
Write-Host "V2: $V2Base | Legacy: $LegacyBase | CDN: $CdnBase"
Write-Host ""

# ─── Phase 1: Core MVP ───
Invoke-ApiTest "v2-core" "GET" "catalog?limit=5"
Invoke-ApiTest "v2-core" "GET" "catalog/1"
Invoke-ApiTest "v2-core" "GET" "catalog/1/versions"
Invoke-ApiTest "v2-core" "GET" "catalog-meta"
Invoke-ApiTest "v2-core" "GET" "versions"
Invoke-ApiTest "v2-core" "GET" "versions/2025-3-31"
Invoke-ApiTest "v2-core" "GET" "versions/9999-99-99" -Evaluate {
    param($s, $b)
    if ($s -eq 404) { Add-Result "v2-core" "GET" "$V2Base/versions/9999-99-99" $s "OK" "404 dla nieistniejacej wersji (oczekiwane)" "" }
    else { Add-Result "v2-core" "GET" "$V2Base/versions/9999-99-99" $s "WARN" "Nieoczekiwany status" $b }
}

# Catalog data quality
$catalogJson = $null
try {
    $catalogJson = (Invoke-WebRequest -Uri "$V2Base/catalog?limit=50" -UseBasicParsing).Content | ConvertFrom-Json
} catch {}

$modsWithVariants = @()
$modsWithoutVariants = @()
$modsWithoutIcon = @()
if ($catalogJson) {
    foreach ($m in $catalogJson.data) {
        if (-not $m.iconUrl) { $modsWithoutIcon += $m.id }
    }
}
try {
    foreach ($m in $catalogJson.data) {
        $detail = (Invoke-WebRequest -Uri "$V2Base/catalog/$($m.id)" -UseBasicParsing).Content | ConvertFrom-Json
        if ($detail.data.variants -and $detail.data.variants.Count -gt 0) {
            $modsWithVariants += [pscustomobject]@{ Id = $m.id; Name = $m.name; Version = $detail.data.currentVersion; Variant = $detail.data.variants[0] }
        } else {
            $modsWithoutVariants += [pscustomobject]@{ Id = $m.id; Name = $m.name; Version = $m.currentVersion }
        }
    }
} catch {}

Add-Result "v2-data-quality" "AUDIT" "catalog iconUrl" 0 $(if ($modsWithoutIcon.Count -eq $catalogJson.data.Count) { "FAIL" } else { "WARN" }) "Wszystkie $($modsWithoutIcon.Count)/$($catalogJson.data.Count) modow ma iconUrl=null (ikony tylko z legacy)" ""

# Downloads - test kilku modow
Test-DownloadRedirect "v2-downloads" 1 "5.3.1"
Test-DownloadRedirect "v2-downloads" 7 "7.2.0-pr3-fix"
if ($modsWithVariants.Count -gt 0) {
    $sample = $modsWithVariants | Select-Object -First 3
    foreach ($s in $sample) {
        if ($s.Id -ne 1) { Test-DownloadRedirect "v2-downloads" $s.Id $s.Version }
    }
}

# ─── Phase 2: Compatibility + Roles ───
Invoke-ApiTest "v2-compat" "GET" "compatibility?modId=1&amongVersion=2025-3-31"
Invoke-ApiTest "v2-compat" "GET" "compatibility/snapshot?amongVersion=2025-3-31"
Invoke-ApiTest "v2-compat" "GET" "roles"

# ─── Phase 3: Public ───
Invoke-ApiTest "v2-public" "GET" "releases?channel=release"
Invoke-ApiTest "v2-public" "GET" "releases?channel=beta"
Invoke-ApiTest "v2-public" "GET" "telemetry/stats"
Invoke-ApiTest "v2-public" "GET" "telemetry/health"
Invoke-ApiTest "v2-public" "GET" "discord/favs/public"
Invoke-ApiTest "v2-public" "GET" "discord/server-counts"
Invoke-ApiTest "v2-public" "GET" "online"
Invoke-ApiTest "v2-public" "GET" "virustotal/report"

# Telemetry heartbeat (write)
$hb = '{"userHash":"test-e2e-hash","appVersion":"3.0.0","platform":"steam","language":"pl","installedModIds":[1],"sessionTimeSeconds":1,"timestamp":"2026-06-06T12:00:00Z"}'
Invoke-ApiTest "v2-public" "POST" "telemetry/heartbeat" -Body $hb -Evaluate {
    param($s, $b)
    if ($s -in 200, 201, 204) { Add-Result "v2-public" "POST" "$V2Base/telemetry/heartbeat" $s "OK" "Heartbeat accepted" $b }
    elseif ($s -eq 429) { Add-Result "v2-public" "POST" "$V2Base/telemetry/heartbeat" $s "OK" "Rate limited (endpoint dziala)" $b }
    else { Add-Result "v2-public" "POST" "$V2Base/telemetry/heartbeat" $s "FAIL" "Heartbeat failed" $b }
}

# ─── Phase 4: Lobby + ModPacks + Sustats ───
$v2Headers = @{}
if ($AuthToken) { $v2Headers["Authorization"] = $AuthToken }
$v2Headers["X-User-Hash"] = $UserHash

Invoke-ApiTest "v2-lobby" "GET" "lobby?limit=5" -Headers $v2Headers -Evaluate {
    param($s, $b)
    if ($s -eq 200) { Add-Result "v2-lobby" "GET" "$V2Base/lobby" $s "OK" "Lista lobby" $b.Substring(0, [Math]::Min(200, $b.Length)) }
    elseif ($s -eq 401) { Add-Result "v2-lobby" "GET" "$V2Base/lobby" $s "FAIL" "401 Unauthorized - wymaga tokena admin/user (klient wysyla Authorization)" $b }
    else { Add-Result "v2-lobby" "GET" "$V2Base/lobby" $s "FAIL" "Nieoczekiwany status" $b }
}

Invoke-ApiTest "v2-modpacks" "GET" "modpacks/INVALID" -Evaluate {
    param($s, $b)
    if ($s -eq 404) { Add-Result "v2-modpacks" "GET" "$V2Base/modpacks/INVALID" $s "OK" "404 dla zlego kodu (route istnieje)" "" }
    else { Add-Result "v2-modpacks" "GET" "$V2Base/modpacks/INVALID" $s "WARN" "Status $s" $b }
}

Invoke-ApiTest "v2-sustats" "GET" "sustats/games" -Evaluate {
    param($s, $b)
    if ($s -eq 400) { Add-Result "v2-sustats" "GET" "$V2Base/sustats/games" $s "OK" "400 bez parametrow (route istnieje)" "" }
    else { Add-Result "v2-sustats" "GET" "$V2Base/sustats/games" $s "WARN" "Status $s" $b }
}

Invoke-ApiTest "v2-sustats" "GET" "sustats/drafts/stats" -Evaluate {
    param($s, $b)
    if ($s -eq 500) { Add-Result "v2-sustats" "GET" "$V2Base/sustats/drafts/stats" $s "FAIL" "500 - missing role_drafts table in prod DB" $b }
    elseif ($s -eq 200) { Add-Result "v2-sustats" "GET" "$V2Base/sustats/drafts/stats" $s "OK" "200" $b }
    else { Add-Result "v2-sustats" "GET" "$V2Base/sustats/drafts/stats" $s "WARN" "Status $s" $b }
}

# Admin sample (bez sekretow - oczekiwane 401/403)
$adminHeaders = @{}
if ($AuthToken) { $adminHeaders["Authorization"] = $AuthToken }
if ($AdminSecret) { $adminHeaders["X-Admin-API-Secret"] = $AdminSecret }

Invoke-ApiTest "v2-admin" "GET" "admin/mods?limit=1" -Headers $adminHeaders -Evaluate {
    param($s, $b)
    if ($AdminSecret -and $AuthToken -and $s -eq 200) { Add-Result "v2-admin" "GET" "$V2Base/admin/mods" $s "OK" "Admin auth OK" "" }
    elseif ($s -in 401, 403) { Add-Result "v2-admin" "GET" "$V2Base/admin/mods" $s "EXPECTED" "Wymaga admin auth (nie podano sekretow w tescie)" "" }
    else { Add-Result "v2-admin" "GET" "$V2Base/admin/mods" $s "WARN" "Status $s" $b }
}

# ─── Legacy susmodder.app ───
$legacyEndpoints = @(
    @{ G = "legacy"; P = "/api/susmodder-config" },
    @{ G = "legacy"; P = "/api/roles-modifiers" },
    @{ G = "legacy"; P = "/api/susmodder-discordfavs" },
    @{ G = "legacy"; P = "/api/public/discord-server-counts" },
    @{ G = "legacy"; P = "/api/among-tokens" },
    @{ G = "legacy"; P = "/api/mod-packs" },
    @{ G = "legacy"; P = "/api/lobby-board" },
    @{ G = "legacy"; P = "/api/mod-download/1/5.3.1?platform=steam" },
    @{ G = "legacy"; P = "/api/releases?channel=release" },
    @{ G = "legacy"; P = "/api/online-users" },
    @{ G = "legacy"; P = "/api/compatibility?modId=1" }
)

foreach ($ep in $legacyEndpoints) {
    Invoke-ApiTest $ep.G "GET" "$LegacyBase$($ep.P)" -Evaluate {
        param($s, $b, $h)
        $url = "$LegacyBase$($ep.P)"
        if ($s -eq 200) {
            Add-Result $ep.G "GET" $url $s "OK" "Dziala (v1)" ($b.Substring(0, [Math]::Min(150, $b.Length)))
        }
        elseif ($s -eq 404 -and $ep.P -match 'lobby-board|mod-download|releases') {
            Add-Result $ep.G "GET" $url $s "FAIL" "404 - endpoint v1 niedostepny / usuniety / nie wdrozony na susmodder.app" $b
        }
        elseif ($s -eq 401 -or $s -eq 403) {
            Add-Result $ep.G "GET" $url $s "EXPECTED" "Wymaga autoryzacji" ""
        }
        else {
            Add-Result $ep.G "GET" $url $s "WARN" "HTTP $s" ($b.Substring(0, [Math]::Min(150, $b.Length)))
        }
    }
}

# ─── Static assets ───
$iconTests = @("10.png", "tou.png", "syzyf-beta.png", "ModNeut1.png")
foreach ($icon in $iconTests) {
    Invoke-ApiTest "assets" "HEAD" "$LegacyBase/icons/$icon" -Evaluate {
        param($s, $b)
        if ($s -eq 200) { Add-Result "assets" "HEAD" "$LegacyBase/icons/$icon" $s "OK" "Ikona na susmodder.app" "" }
        else { Add-Result "assets" "HEAD" "$LegacyBase/icons/$icon" $s $(if ($s -eq 404) { "FAIL" } else { "WARN" }) "HTTP $s" "" }
    }
    Invoke-ApiTest "assets" "HEAD" "$CdnBase/icons/$icon" -Evaluate {
        param($s, $b)
        if ($s -eq 200) { Add-Result "assets" "HEAD" "$CdnBase/icons/$icon" $s "OK" "Ikona na CDN" "" }
        else { Add-Result "assets" "HEAD" "$CdnBase/icons/$icon" $s "FAIL" "404 - ikony NIE sa na CDN (tylko susmodder.app)" "" }
    }
}

# CDN mod file direct
Invoke-ApiTest "assets" "HEAD" "$CdnBase/mods/1/5.3.1/ToU.v5.3.1.zip" -Evaluate {
    param($s, $b)
    if ($s -eq 200) { Add-Result "assets" "HEAD" "$CdnBase/mods/1/5.3.1/ToU.v5.3.1.zip" $s "OK" "Plik moda na CDN" "" }
    else { Add-Result "assets" "HEAD" "$CdnBase/mods/1/5.3.1/ToU.v5.3.1.zip" $s "FAIL" "404 - plik moda nie zostal wrzucony na CDN mimo metadanych w API" "" }
}

# Clair (external)
Invoke-ApiTest "external" "GET" "https://clairbot.app/api/susmodder/config" -Evaluate {
    param($s, $b)
    if ($s -eq 200) { Add-Result "external" "GET" "clairbot.app/api/susmodder/config" $s "OK" "Clair OAuth config" "" }
    else { Add-Result "external" "GET" "clairbot.app/api/susmodder/config" $s "WARN" "HTTP $s" $b }
}

# ─── Summary ───
Write-Host ""
Write-Host "=== PODSUMOWANIE ===" -ForegroundColor Cyan
$grouped = $results | Group-Object Verdict
foreach ($g in $grouped) {
    Write-Host "$($g.Name): $($g.Count)" -ForegroundColor $(switch ($g.Name) { "OK" { "Green" } "EXPECTED" { "DarkGray" } "WARN" { "Yellow" } "FAIL" { "Red" } default { "White" } })
}

Write-Host ""
Write-Host "=== FAIL / WARN ===" -ForegroundColor Red
$results | Where-Object { $_.Verdict -in "FAIL", "WARN" } | Sort-Object Group, Url | Format-Table Group, Method, Status, Verdict, Reason, Url -AutoSize -Wrap

# Mods without variants
if ($modsWithoutVariants.Count -gt 0) {
    Write-Host ""
    Write-Host "=== FULL MODS WITHOUT VARIANTS (not downloadable) ===" -ForegroundColor Yellow
    $modsWithoutVariants | Format-Table Id, Name, Version -AutoSize
}

$outFile = Join-Path $PSScriptRoot "api-e2e-results-$(Get-Date -Format 'yyyy-MM-dd-HHmm').json"
$results | ConvertTo-Json -Depth 4 | Set-Content $outFile -Encoding UTF8
Write-Host ""
Write-Host "Full results: $outFile" -ForegroundColor DarkGray

