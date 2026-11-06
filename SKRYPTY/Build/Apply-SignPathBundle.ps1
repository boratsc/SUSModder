#Requires -Version 7.0
<#
.SYNOPSIS
  Wkleja podpisane artefakty SignPath z powrotem do katalogu releases-{channel}.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$SignedDir,

    [Parameter(Mandatory = $true)]
    [string]$ReleasesDir,

    [Parameter(Mandatory = $false)]
    [string]$MapPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $SignedDir)) {
    throw "SignedDir not found: $SignedDir"
}
if (-not (Test-Path $ReleasesDir)) {
    throw "ReleasesDir not found: $ReleasesDir"
}

# SignPath / upload-artifact sometimes nest one extra folder
$probe = @(
    (Join-Path $SignedDir 'Setup.exe'),
    (Join-Path $SignedDir 'portable.zip'),
    (Join-Path $SignedDir 'package.nupkg')
)
if (-not ($probe | Where-Object { Test-Path $_ })) {
    $nested = Get-ChildItem $SignedDir -Directory | Select-Object -First 1
    if ($nested) {
        Write-Host "Using nested signed dir: $($nested.FullName)"
        $SignedDir = $nested.FullName
    }
}

if ([string]::IsNullOrWhiteSpace($MapPath)) {
    $MapPath = Join-Path $SignedDir 'signpath-map.json'
    if (-not (Test-Path $MapPath)) {
        $MapPath = Join-Path $ReleasesDir 'signpath-map.json'
    }
}

# Prefer map from unsigned input copy kept beside releases
$mapCandidates = @(
    $MapPath,
    (Join-Path $ReleasesDir 'signpath-map.json'),
    (Join-Path (Split-Path $SignedDir -Parent) 'signpath-unsigned\signpath-map.json')
) | Where-Object { $_ -and (Test-Path $_) }

$map = $null
foreach ($c in $mapCandidates) {
    $map = Get-Content $c -Raw | ConvertFrom-Json
    Write-Host "Loaded signpath map: $c"
    break
}

if (-not $map) {
    throw "signpath-map.json not found (needed to restore original Velopack filenames)."
}

$signedSetup = Join-Path $SignedDir 'Setup.exe'
$signedUpdate = Join-Path $SignedDir 'Update.exe'
$signedPortable = Join-Path $SignedDir 'portable.zip'
$signedNupkg = Join-Path $SignedDir 'package.nupkg'

foreach ($p in @($signedSetup, $signedUpdate, $signedPortable, $signedNupkg)) {
    if (-not (Test-Path $p)) {
        throw "Missing signed artifact: $p"
    }
}

$destSetup = Join-Path $ReleasesDir $map.setupOriginalName
$destPortable = Join-Path $ReleasesDir $map.portableOriginalName
$destNupkg = Join-Path $ReleasesDir $map.nupkgOriginalName
$destUpdate = Join-Path $ReleasesDir 'Update.exe'

Copy-Item $signedSetup -Destination $destSetup -Force
Copy-Item $signedUpdate -Destination $destUpdate -Force
Copy-Item $signedPortable -Destination $destPortable -Force
Copy-Item $signedNupkg -Destination $destNupkg -Force

Write-Host "Signed artifacts applied to $ReleasesDir"
Write-Host "  Setup: $($map.setupOriginalName)"
Write-Host "  Portable: $($map.portableOriginalName)"
Write-Host "  nupkg: $($map.nupkgOriginalName)"
Write-Host "  Update.exe"
