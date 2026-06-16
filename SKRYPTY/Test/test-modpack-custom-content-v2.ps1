# SUSModder API v2 - modpack custom content smoke test
# Verifies create -> DLL upload -> status -> optional GitHub declaration -> finalize -> preview.
param(
    [string]$V2Base = "https://api.susmodder-cdn.ovh/v2",
    [Parameter(Mandatory = $false)]
    [string]$AuthToken = "",
    [Parameter(Mandatory = $false)]
    [string]$DllPath = "",
    [Parameter(Mandatory = $false)]
    [string]$GitHubDllUrl = "",
    [string]$CreatorName = "SUSModder E2E",
    [int]$FullModId = 1,
    [string]$FullModVersion = "latest",
    [int]$TtlDays = 7,
    [int]$MaxPollAttempts = 40,
    [int]$PollDelaySeconds = 3,
    [switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"
$results = [System.Collections.Generic.List[object]]::new()

function Add-Result {
    param([string]$Area, [string]$Test, [string]$Verdict, [string]$Detail)
    $script:results.Add([pscustomobject]@{ Area = $Area; Test = $Test; Verdict = $Verdict; Detail = $Detail })
}

function New-CreatorHash {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes("susmodder-e2e-$([guid]::NewGuid())")
    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
    return ([Convert]::ToHexString($hash)).ToLowerInvariant()
}

function New-Headers {
    param([string]$CreatorHash)
    $h = @{
        "X-User-Hash" = $CreatorHash
    }
    if (-not [string]::IsNullOrWhiteSpace($AuthToken)) {
        $h["Authorization"] = "Bearer $AuthToken"
    }
    return $h
}

function Invoke-Json {
    param(
        [string]$Method,
        [string]$Path,
        [hashtable]$Headers,
        [object]$Body = $null
    )
    $uri = "$($V2Base.TrimEnd('/'))/$($Path.TrimStart('/'))"
    try {
        $params = @{
            Uri = $uri
            Method = $Method
            Headers = $Headers
            TimeoutSec = 60
            UseBasicParsing = $true
        }
        if ($null -ne $Body) {
            $params.Body = ($Body | ConvertTo-Json -Depth 20)
            $params.ContentType = "application/json"
        }
        $response = Invoke-WebRequest @params
        return @{ Code = [int]$response.StatusCode; Body = $response.Content; Json = ($response.Content | ConvertFrom-Json) }
    }
    catch {
        $code = 0
        $body = $_.Exception.Message
        if ($_.Exception.Response) {
            $code = [int]$_.Exception.Response.StatusCode.value__
            try {
                $reader = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
                $body = $reader.ReadToEnd()
                $reader.Close()
            } catch {}
        }
        return @{ Code = $code; Body = $body; Json = $null }
    }
}

function Wait-ArtifactStatus {
    param(
        [string]$PackCode,
        [string]$StatusPath,
        [hashtable]$Headers,
        [string]$Area
    )

    for ($i = 1; $i -le $MaxPollAttempts; $i++) {
        $status = Invoke-Json -Method "GET" -Path $StatusPath -Headers $Headers
        if ($status.Code -ne 200) {
            Add-Result $Area "GET status" "FAIL" "HTTP $($status.Code): $($status.Body)"
            return $false
        }

        $value = $status.Json.data.status
        Add-Result $Area "poll #$i" "INFO" "status=$value"

        if ($value -eq "clean") { return $true }
        if ($value -in @("suspicious", "rejected", "expired")) { return $false }
        Start-Sleep -Seconds $PollDelaySeconds
    }

    Add-Result $Area "poll timeout" "FAIL" "No clean status after $MaxPollAttempts attempts"
    return $false
}

if ($ValidateOnly) {
    Write-Host "Script parsed OK. Use -AuthToken and -DllPath to run the smoke test." -ForegroundColor Green
    return
}

if ([string]::IsNullOrWhiteSpace($AuthToken)) {
    throw "AuthToken is required for modpack create/upload endpoints."
}

if ([string]::IsNullOrWhiteSpace($DllPath) -or -not (Test-Path -LiteralPath $DllPath)) {
    throw "DllPath must point to an existing .dll file."
}

if ([System.IO.Path]::GetExtension($DllPath) -ne ".dll") {
    throw "DllPath must be a .dll file."
}

Write-Host "=== SUSModder API v2 custom content smoke ===" -ForegroundColor Cyan
$creatorHash = New-CreatorHash
$headers = New-Headers -CreatorHash $creatorHash

$createBody = @{
    creatorHash = $creatorHash
    creatorName = $CreatorName
    fullModId = $FullModId
    fullModVersion = $FullModVersion
    modName = "Custom content E2E $((Get-Date).ToString('yyyyMMdd-HHmmss'))"
    includeIntegrationDll = $false
    ttlDays = $TtlDays
    dllMods = @()
}

$created = Invoke-Json -Method "POST" -Path "modpacks" -Headers $headers -Body $createBody
if ($created.Code -ne 200 -and $created.Code -ne 201) {
    Add-Result "create" "POST /modpacks" "FAIL" "HTTP $($created.Code): $($created.Body)"
    $results | Format-Table -AutoSize
    exit 1
}

$packCode = $created.Json.data.packCode
if ([string]::IsNullOrWhiteSpace($packCode)) { $packCode = $created.Json.packCode }
if ([string]::IsNullOrWhiteSpace($packCode)) {
    Add-Result "create" "packCode" "FAIL" "Response did not include packCode"
    $results | Format-Table -AutoSize
    exit 1
}
Add-Result "create" "POST /modpacks" "OK" "packCode=$packCode"

$uploadUri = "$($V2Base.TrimEnd('/'))/modpacks/$packCode/dlls"
try {
    $upload = Invoke-WebRequest -Uri $uploadUri -Method POST -Headers $headers -Form @{ file = Get-Item -LiteralPath $DllPath; creatorHash = $creatorHash } -TimeoutSec 120 -UseBasicParsing
    $uploadJson = $upload.Content | ConvertFrom-Json
    Add-Result "uploaded_dll" "POST /dlls" "OK" "HTTP $([int]$upload.StatusCode)"
}
catch {
    Add-Result "uploaded_dll" "POST /dlls" "FAIL" $_.Exception.Message
    $results | Format-Table -AutoSize
    exit 1
}

$dllClean = $true
# DLL status is available via pack preview (externalDlls[].vtStatus), not a separate endpoint
$preview = Invoke-Json -Method "GET" -Path "modpacks/$packCode" -Headers @{}
if ($preview.Code -eq 200 -and $preview.Json.data.externalDlls.Count -gt 0) {
    $dll = $preview.Json.data.externalDlls[0]
    Add-Result "uploaded_dll" "vtStatus from preview" "OK" "fileName=$($dll.fileName) sha256=$($dll.sha256) vtStatus=$($dll.vtStatus)"
    $sha = $dll.sha256
    $dllClean = $dll.vtStatus -eq "clean"
} else {
    Add-Result "uploaded_dll" "vtStatus from preview" "WARN" "No external DLL in preview response"
}

if (-not [string]::IsNullOrWhiteSpace($GitHubDllUrl)) {
    $githubBody = @{
        sourceKind = "github_dll"
        modType = "dll"
        displayName = "GitHub E2E DLL"
        version = "e2e"
        githubUrl = $GitHubDllUrl
        dllInstallPath = "BepInEx/plugins"
    }
    $declared = Invoke-Json -Method "POST" -Path "modpacks/$packCode/custom-github-mods" -Headers $headers -Body $githubBody
    if ($declared.Code -ne 200 -and $declared.Code -ne 201 -and $declared.Code -ne 202) {
        Add-Result "github_dll" "POST /custom-github-mods" "FAIL" "HTTP $($declared.Code): $($declared.Body)"
    }
    else {
        $artifactId = $declared.Json.data.customArtifact.artifactId
        Add-Result "github_dll" "POST /custom-github-mods" "OK" "artifactId=$artifactId"
        if (-not [string]::IsNullOrWhiteSpace($artifactId)) {
            [void](Wait-ArtifactStatus -PackCode $packCode -StatusPath "modpacks/$packCode/custom-artifacts/$artifactId/status" -Headers $headers -Area "github_dll")
        }
    }
}

$finalized = Invoke-Json -Method "POST" -Path "modpacks/$packCode/finalize" -Headers $headers -Body @{ creatorHash = $creatorHash }
if ($finalized.Code -eq 200 -and $finalized.Json.data.installable -eq $true) {
    Add-Result "finalize" "POST /finalize" "OK" "status=$($finalized.Json.data.status), installable=$($finalized.Json.data.installable)"
}
else {
    Add-Result "finalize" "POST /finalize" $(if ($dllClean) { "FAIL" } else { "WARN" }) "HTTP $($finalized.Code): $($finalized.Body)"
}

$preview = Invoke-Json -Method "GET" -Path "modpacks/$packCode" -Headers @{}
if ($preview.Code -eq 200) {
    $artifactCount = @($preview.Json.data.customArtifacts).Count
    Add-Result "preview" "GET /modpacks/:code" "OK" "customArtifacts=$artifactCount, status=$($preview.Json.data.status), installable=$($preview.Json.data.installable)"
}
else {
    Add-Result "preview" "GET /modpacks/:code" "FAIL" "HTTP $($preview.Code): $($preview.Body)"
}

$results | Format-Table -AutoSize
$failed = @($results | Where-Object { $_.Verdict -eq "FAIL" }).Count
if ($failed -gt 0) { exit 1 }
