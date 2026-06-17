# Quick Dummy Release Generator
param([string]$Version = "2.1.0", [string]$Channel = "win")

$ErrorActionPreference = "Stop"
$OutputDir = Join-Path $PSScriptRoot "dummy-release"

Write-Host "Dummy Velopack Release Generator" -ForegroundColor Cyan
Write-Host "Version: $Version | Channel: $Channel" -ForegroundColor Yellow
Write-Host ""

if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$fileName = "SUSModder-$Version-$Channel-full.nupkg"
$filePath = Join-Path $OutputDir $fileName

$nuspecContent = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
  <metadata>
    <id>SUSModder</id>
    <version>$Version</version>
    <title>SUSModder</title>
    <authors>SUSModder Team</authors>
    <description>Among Us Mod Manager</description>
  </metadata>
</package>
"@

$tempDir = Join-Path $env:TEMP "velopack-dummy-$(New-Guid)"
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
$nuspecPath = Join-Path $tempDir "SUSModder.nuspec"
Set-Content -Path $nuspecPath -Value $nuspecContent

$dummyExe = Join-Path $tempDir "SUSModder.exe"
"Dummy" | Out-File $dummyExe -Encoding ASCII

Write-Host "[1/3] Creating dummy .nupkg..." -ForegroundColor Green
$zipPath = [System.IO.Path]::ChangeExtension($filePath, ".zip")
Compress-Archive -Path "$tempDir\*" -DestinationPath $zipPath
Rename-Item $zipPath $filePath
Write-Host "  Done: $fileName" -ForegroundColor Green

Write-Host "[2/3] Calculating SHA256..." -ForegroundColor Green
$fileStream = [System.IO.File]::OpenRead($filePath)
$sha256 = [System.Security.Cryptography.SHA256]::Create()
$hashBytes = $sha256.ComputeHash($fileStream)
$fileStream.Close()
$checksum = [System.BitConverter]::ToString($hashBytes).Replace("-", "").ToLower()
Write-Host "  SHA256: $checksum" -ForegroundColor Green

$fileSize = (Get-Item $filePath).Length

Write-Host "[3/3] Generating RELEASES manifest..." -ForegroundColor Green
$releasesContent = "$checksum $fileName $fileSize $(Get-Date -Format 'yyyy-MM-dd')"
$releasesPath = Join-Path $OutputDir "RELEASES"
Set-Content -Path $releasesPath -Value $releasesContent -NoNewline

$releasesJson = @{
    LatestVersion = $Version
    Releases = @(@{
        Version = $Version
        File = $fileName
        SHA256 = $checksum
        Channel = $Channel
        Size = $fileSize
        CreateTime = (Get-Date).ToUniversalTime().ToString("o")
    })
    downloadBaseUrl = "https://susmodder.app/releases"
} | ConvertTo-Json -Depth 10

$releasesJsonPath = Join-Path $OutputDir "releases.$Channel.json"
Set-Content -Path $releasesJsonPath -Value $releasesJson
Write-Host "  Done: RELEASES and releases.$Channel.json" -ForegroundColor Green

Remove-Item $tempDir -Recurse -Force

Write-Host ""
Write-Host "SUCCESS" -ForegroundColor Green
Write-Host "Output: $OutputDir" -ForegroundColor Cyan
Write-Host ""
Write-Host "Files:" -ForegroundColor Cyan
Get-ChildItem $OutputDir | ForEach-Object {
    $size = if ($_.Length -gt 1KB) { "{0:N2} KB" -f ($_.Length / 1KB) } else { "$($_.Length) bytes" }
    Write-Host "  - $($_.Name) ($size)" -ForegroundColor White
}
Write-Host ""
Write-Host "Checksum for backend API:" -ForegroundColor Yellow
Write-Host $checksum -ForegroundColor White
Write-Host ""
