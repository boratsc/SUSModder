# Fails if a publish output directory looks like a single-file publish.
# Velopack public releases require PublishSingleFile=false (unpacked managed DLLs).

param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,

    [string]$ExeName = "SUSModder.exe",

    # Single-file self-contained builds are typically >> 80 MB for this app.
    [long]$SuspiciousExeBytes = 80MB
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PublishDir)) {
    throw "Publish directory not found: $PublishDir"
}

$exePath = Join-Path $PublishDir $ExeName
if (-not (Test-Path $exePath)) {
    throw "Missing $ExeName in publish output: $PublishDir"
}

$dlls = Get-ChildItem -Path $PublishDir -Filter "*.dll" -File -ErrorAction SilentlyContinue
$exeSize = (Get-Item $exePath).Length

if ($null -eq $dlls -or $dlls.Count -eq 0) {
    throw @"
PublishSingleFile gate FAILED: no managed/native DLLs next to $ExeName.
This looks like a single-file publish, which is not allowed for public Velopack releases.
PublishDir: $PublishDir
Exe size: $exeSize bytes
"@
}

if ($exeSize -ge $SuspiciousExeBytes) {
    throw @"
PublishSingleFile gate FAILED: $ExeName is suspiciously large ($exeSize bytes >= $SuspiciousExeBytes).
Expected unpacked publish with smaller host EXE and many sibling DLLs.
PublishDir: $PublishDir
DLL count: $($dlls.Count)
"@
}

Write-Host "PublishSingleFile gate OK: $($dlls.Count) DLLs, $ExeName = $exeSize bytes" -ForegroundColor Green
