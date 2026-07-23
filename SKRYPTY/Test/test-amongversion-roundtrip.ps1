# Verify amongVersion round-trip for custom github_full modpacks (API v2).
param(
    [string]$V2Base = "https://api.susmodder-cdn.ovh/v2",
    [string]$AmongVersion = "2024-6-18",
    [string]$GitHubFullUrl = "https://github.com/NuclearPowered/BepInEx/releases/download/v6.0.0-pre.1/BepInEx_UnityIL_x64_6.0.0-pre.1.zip"
)

$ErrorActionPreference = "Stop"

$token = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(
    "YTBlMjNiZWFmMTBlZjQxZGM1ZjRiN2NhNGYxOWZmNDkyNDI4MjEyNWMzNzQ5Mzk5MjIwYzA5MDMzNGU3NGI4Mw=="))

$sha = [Security.Cryptography.SHA256]::Create()
try {
    $bytes = [Text.Encoding]::UTF8.GetBytes(("susmodder-among-verify-{0}" -f [guid]::NewGuid()))
    $hash = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-", "").ToLowerInvariant()
}
finally {
    $sha.Dispose()
}

$headers = @{
    Authorization = $token
    "X-User-Hash" = $hash
    "User-Agent"  = "SUSModder/3.0"
}

$results = New-Object System.Collections.Generic.List[object]

function Add-Result([string]$Test, [string]$Verdict, [string]$Detail) {
    $script:results.Add([pscustomobject]@{ Test = $Test; Verdict = $Verdict; Detail = $Detail })
    $color = switch ($Verdict) { "OK" { "Green" } "FAIL" { "Red" } default { "Cyan" } }
    Write-Host ("{0,-4} {1} - {2}" -f $Verdict, $Test, $Detail) -ForegroundColor $color
}

function Invoke-Api([string]$Method, [string]$Path, $Body = $null) {
    $uri = "{0}/{1}" -f $V2Base.TrimEnd('/'), $Path.TrimStart('/')
    $params = @{
        Uri             = $uri
        Method          = $Method
        Headers         = $headers
        TimeoutSec      = 60
        UseBasicParsing = $true
    }
    if ($null -ne $Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 12)
        $params.ContentType = "application/json"
    }

    try {
        $response = Invoke-WebRequest @params
        return @{
            Code = [int]$response.StatusCode
            Body = $response.Content
            Json = ($response.Content | ConvertFrom-Json)
        }
    }
    catch {
        $code = 0
        $body = $_.Exception.Message
        if ($_.Exception.Response) {
            $code = [int]$_.Exception.Response.StatusCode.value__
            try {
                $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                $body = $reader.ReadToEnd()
                $reader.Close()
            }
            catch {}
        }

        $json = $null
        try { $json = $body | ConvertFrom-Json } catch {}
        return @{ Code = $code; Body = $body; Json = $json }
    }
}

function Get-Among([object]$obj) {
    if ($null -eq $obj) { return $null }
    if ($obj.amongVersion) { return [string]$obj.amongVersion }
    if ($obj.among_version) { return [string]$obj.among_version }
    return $null
}

Write-Host "=== amongVersion round-trip ===" -ForegroundColor Cyan
Write-Host ("creatorHash={0}" -f $hash)

# --- Probe create modes ---
$createCandidates = @(
    @{
        Name = "fullModId=0 + metadata"
        Body = @{
            creatorHash           = $hash
            creatorName           = "AmongVersion Verify"
            fullModId             = 0
            fullModVersion        = "custom"
            modName               = ("AmongVerify-{0}" -f (Get-Date -Format "HHmmss"))
            includeIntegrationDll = $false
            ttlDays               = 7
            dllMods               = @()
            metadata              = @{ amongVersion = $AmongVersion }
        }
    },
    @{
        Name = "fullModId=1 + metadata"
        Body = @{
            creatorHash           = $hash
            creatorName           = "AmongVersion Verify"
            fullModId             = 1
            fullModVersion        = "latest"
            modName               = ("AmongVerifyCat-{0}" -f (Get-Date -Format "HHmmss"))
            includeIntegrationDll = $false
            ttlDays               = 7
            dllMods               = @()
            metadata              = @{ amongVersion = $AmongVersion }
        }
    }
)

$packCode = $null
$createMode = $null
foreach ($candidate in $createCandidates) {
    $created = Invoke-Api -Method "POST" -Path "modpacks" -Body $candidate.Body
    if ($created.Code -in 200, 201) {
        $packCode = $created.Json.data.packCode
        if ([string]::IsNullOrWhiteSpace($packCode)) { $packCode = $created.Json.packCode }
        $createMode = $candidate.Name
        Add-Result ("POST /modpacks ({0})" -f $candidate.Name) "OK" ("HTTP {0} packCode={1}" -f $created.Code, $packCode)
        break
    }

    $snippet = if ($created.Body) { $created.Body.Substring(0, [Math]::Min(240, $created.Body.Length)) } else { "(empty)" }
    Add-Result ("POST /modpacks ({0})" -f $candidate.Name) "INFO" ("HTTP {0}: {1}" -f $created.Code, $snippet)
}

if ([string]::IsNullOrWhiteSpace($packCode)) {
    Add-Result "create" "FAIL" "Nie udało się utworzyć paczki żadnym wariantem"
    $results | Format-Table -AutoSize
    exit 1
}

# --- GET metadata ---
$get1 = Invoke-Api -Method "GET" -Path ("modpacks/{0}" -f $packCode)
if ($get1.Code -eq 200) {
    $metaAmong = Get-Among $get1.Json.data.metadata
    if ($metaAmong -eq $AmongVersion) {
        Add-Result "GET metadata.amongVersion" "OK" ("amongVersion={0}" -f $metaAmong)
    }
    else {
        $raw = $get1.Body.Substring(0, [Math]::Min(500, $get1.Body.Length))
        Add-Result "GET metadata.amongVersion" "FAIL" ("expected={0} got='{1}' raw={2}" -f $AmongVersion, $metaAmong, $raw)
    }
}
else {
    Add-Result "GET /modpacks/:code" "FAIL" ("HTTP {0}: {1}" -f $get1.Code, $get1.Body)
}

# --- declare without amongVersion ---
$noAmong = Invoke-Api -Method "POST" -Path ("modpacks/{0}/custom-github-mods" -f $packCode) -Body @{
    creatorHash = $hash
    sourceKind  = "github_full"
    modType     = "full"
    displayName = "Missing Among"
    version     = "e2e"
    githubUrl   = $GitHubFullUrl
}
if ($noAmong.Code -ge 400) {
    $snippet = if ($noAmong.Body) { $noAmong.Body.Substring(0, [Math]::Min(200, $noAmong.Body.Length)) } else { "" }
    Add-Result "declare github_full bez amongVersion → reject" "OK" ("HTTP {0} {1}" -f $noAmong.Code, $snippet)
}
elseif ($noAmong.Code -in 200, 201, 202) {
    Add-Result "declare github_full bez amongVersion → reject" "FAIL" ("BE zaakceptował bez amongVersion HTTP {0}" -f $noAmong.Code)
}
else {
    Add-Result "declare github_full bez amongVersion" "INFO" ("HTTP {0}: {1}" -f $noAmong.Code, $noAmong.Body)
}

# --- declare with amongVersion ---
$withAmong = Invoke-Api -Method "POST" -Path ("modpacks/{0}/custom-github-mods" -f $packCode) -Body @{
    creatorHash   = $hash
    sourceKind    = "github_full"
    modType       = "full"
    displayName   = "With Among"
    version       = "e2e"
    amongVersion  = $AmongVersion
    githubUrl     = $GitHubFullUrl
}

if ($withAmong.Code -in 200, 201, 202) {
    $art = $withAmong.Json.data.customArtifact
    if ($null -eq $art) { $art = $withAmong.Json.data }
    $returnedAmong = Get-Among $art
    if ($returnedAmong -eq $AmongVersion) {
        Add-Result "declare github_full z amongVersion" "OK" ("HTTP {0} artifactId={1} amongVersion={2} status={3}" -f $withAmong.Code, $art.artifactId, $returnedAmong, $art.status)
    }
    else {
        $snippet = $withAmong.Body.Substring(0, [Math]::Min(400, $withAmong.Body.Length))
        Add-Result "declare github_full z amongVersion (echo)" "FAIL" ("returned='{0}' body={1}" -f $returnedAmong, $snippet)
    }
}
else {
    Add-Result "declare github_full z amongVersion" "FAIL" ("HTTP {0}: {1}" -f $withAmong.Code, $withAmong.Body)
}

# --- GET artifact amongVersion ---
$get2 = Invoke-Api -Method "GET" -Path ("modpacks/{0}" -f $packCode)
if ($get2.Code -eq 200) {
    $arts = @($get2.Json.data.customArtifacts)
    $full = $arts | Where-Object { $_.sourceKind -eq "github_full" -or $_.modType -eq "full" } | Select-Object -First 1
    if ($null -eq $full) {
        Add-Result "GET customArtifacts github_full" "FAIL" ("brak artefaktu full count={0}" -f $arts.Count)
    }
    else {
        $a = Get-Among $full
        if ($a -eq $AmongVersion) {
            Add-Result "GET customArtifacts[].amongVersion" "OK" ("amongVersion={0} status={1}" -f $a, $full.status)
        }
        else {
            Add-Result "GET customArtifacts[].amongVersion" "FAIL" ("expected={0} got='{1}'" -f $AmongVersion, $a)
        }
    }
}
else {
    Add-Result "GET after declare" "FAIL" ("HTTP {0}: {1}" -f $get2.Code, $get2.Body)
}

# Client-side resolve parity (metadata fallback already covered by unit tests)
Add-Result "create mode used" "INFO" $createMode

$del = Invoke-Api -Method "DELETE" -Path ("modpacks/{0}" -f $packCode)
Add-Result "cleanup DELETE" "INFO" ("HTTP {0}" -f $del.Code)

Write-Host ""
$results | Format-Table -AutoSize
$failed = @($results | Where-Object { $_.Verdict -eq "FAIL" }).Count
if ($failed -gt 0) {
    Write-Host "FAILED CHECKS: $failed" -ForegroundColor Red
    exit 1
}

Write-Host "ALL CHECKS PASSED" -ForegroundColor Green
exit 0
