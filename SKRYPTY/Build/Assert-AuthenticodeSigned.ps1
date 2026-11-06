#Requires -Version 7.0
<#
.SYNOPSIS
  Weryfikuje Authenticode kluczowych EXE po SignPath (fail jeśli nie Valid).
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ReleasesDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$targets = @()
$targets += Get-ChildItem $ReleasesDir -Filter '*Setup.exe' -File | Select-Object -First 1
$targets += Get-ChildItem $ReleasesDir -Filter 'Update.exe' -File | Select-Object -First 1

$portable = Get-ChildItem $ReleasesDir -Filter '*Portable.zip' -File | Select-Object -First 1
if (-not $portable) { throw "Portable.zip not found in $ReleasesDir" }

$extractDir = Join-Path $ReleasesDir 'authenticode-verify-temp'
if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force }
Expand-Archive -Path $portable.FullName -DestinationPath $extractDir -Force

$appExe = Get-ChildItem $extractDir -Filter 'SUSModder.exe' -Recurse -File | Select-Object -First 1
if (-not $appExe) { throw "SUSModder.exe not found inside Portable.zip" }
$targets += $appExe

$failed = @()
foreach ($file in ($targets | Where-Object { $_ })) {
    $sig = Get-AuthenticodeSignature $file.FullName
    Write-Host ("{0}: Status={1}; Subject={2}" -f $file.Name, $sig.Status, $(if ($sig.SignerCertificate) { $sig.SignerCertificate.Subject } else { '(none)' }))
    if ($sig.Status -ne 'Valid') {
        $failed += $file.FullName
    }
}

Remove-Item $extractDir -Recurse -Force -ErrorAction SilentlyContinue

if ($failed.Count -gt 0) {
    throw ("Authenticode verification failed for:`n  - {0}" -f ($failed -join "`n  - "))
}

Write-Host "Authenticode verification OK." -ForegroundColor Green
