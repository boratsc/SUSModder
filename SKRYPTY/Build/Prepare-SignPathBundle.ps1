#Requires -Version 7.0
<#
.SYNOPSIS
  Buduje katalog wejściowy dla SignPath (Setup + Update + Portable + nupkg).

.DESCRIPTION
  Kopiuje artefakty Velopack pod stałymi nazwami, żeby Artifact Configuration
  w SignPath mogła je adresować (Setup.exe, Update.exe, portable.zip, package.nupkg).
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ReleasesDir,

    [Parameter(Mandatory = $true)]
    [string]$OutputDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ReleasesDir)) {
    throw "ReleasesDir not found: $ReleasesDir"
}

if (Test-Path $OutputDir) {
    Remove-Item $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$setup = Get-ChildItem $ReleasesDir -Filter '*Setup.exe' -File | Select-Object -First 1
$update = Get-ChildItem $ReleasesDir -Filter 'Update.exe' -File | Select-Object -First 1
$portable = Get-ChildItem $ReleasesDir -Filter '*Portable.zip' -File | Select-Object -First 1
$nupkg = Get-ChildItem $ReleasesDir -Filter '*.nupkg' -File | Select-Object -First 1

if (-not $setup) { throw "Setup.exe not found in $ReleasesDir" }
if (-not $update) { throw "Update.exe not found in $ReleasesDir" }
if (-not $portable) { throw "Portable.zip not found in $ReleasesDir" }
if (-not $nupkg) { throw "nupkg not found in $ReleasesDir" }

Copy-Item $setup.FullName -Destination (Join-Path $OutputDir 'Setup.exe') -Force
Copy-Item $update.FullName -Destination (Join-Path $OutputDir 'Update.exe') -Force
Copy-Item $portable.FullName -Destination (Join-Path $OutputDir 'portable.zip') -Force
Copy-Item $nupkg.FullName -Destination (Join-Path $OutputDir 'package.nupkg') -Force

# Metadata for Apply step — only next to releases (NOT inside SignPath upload)
$mapObject = @{
    setupOriginalName    = $setup.Name
    portableOriginalName = $portable.Name
    nupkgOriginalName    = $nupkg.Name
}
$mapJson = $mapObject | ConvertTo-Json
Set-Content -Path (Join-Path $ReleasesDir 'signpath-map.json') -Value $mapJson -Encoding UTF8

Write-Host "SignPath input prepared in $OutputDir"
Get-ChildItem $OutputDir | ForEach-Object {
    $size = if ($_.Length -gt 1MB) { '{0:N2} MB' -f ($_.Length / 1MB) } else { '{0:N2} KB' -f ($_.Length / 1KB) }
    Write-Host ("  - {0} ({1})" -f $_.Name, $size)
}
