# Post-Sign Velopack Packages
# Signs Update.exe and root SUSModder.exe in already built packages

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("release", "beta", "both")]
    [string]$Channel,
    
    [string]$CertThumbprint = "97171de086564a84fa22a72c4260f72ba13096c6"
)

$ErrorActionPreference = "Stop"

$rootDir = "D:\Development\SUSModder"
$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe"
$timestampServer = "http://time.certum.pl"

function Sign-PortablePackage {
    param([string]$ChannelName)
    
    $releasesDir = "$rootDir\releases-$ChannelName"
    $portableZip = Get-ChildItem $releasesDir -Filter "*Portable.zip" | Select-Object -First 1
    
    if (-not $portableZip) {
        Write-Host "❌ Portable.zip not found in $releasesDir" -ForegroundColor Red
        return
    }
    
    Write-Host "`n🔐 Signing Portable package: $($portableZip.Name)" -ForegroundColor Cyan
    
    # Rozpakuj
    $extractDir = "$releasesDir\portable-sign-temp"
    if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force }
    Expand-Archive -Path $portableZip.FullName -DestinationPath $extractDir -Force
    
    # Znajdź niepodpisane EXE
    $unsignedFiles = @()
    Get-ChildItem $extractDir -Filter "*.exe" -Recurse | ForEach-Object {
        $sig = Get-AuthenticodeSignature $_.FullName
        if ($sig.Status -ne "Valid") {
            $unsignedFiles += $_
        }
    }
    
    if ($unsignedFiles.Count -eq 0) {
        Write-Host "  ✅ All files already signed" -ForegroundColor Green
        Remove-Item $extractDir -Recurse -Force
        return
    }
    
    # Podpisz
    foreach ($file in $unsignedFiles) {
        $relativePath = $file.FullName.Replace($extractDir, "").TrimStart("\")
        Write-Host "   Signing: $relativePath" -ForegroundColor Gray
        
        & $signtool sign /sha1 $CertThumbprint `
            /tr $timestampServer /td sha256 /fd sha256 `
            $file.FullName 2>&1 | Out-Null
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "   ✅ Signed" -ForegroundColor Green
        } else {
            Write-Host "   ❌ Failed" -ForegroundColor Red
            exit 1
        }
    }
    
    # Przepakuj
    Remove-Item $portableZip.FullName -Force
    Compress-Archive -Path "$extractDir\*" -DestinationPath $portableZip.FullName -CompressionLevel Optimal
    Remove-Item $extractDir -Recurse -Force
    
    Write-Host "  ✅ Portable.zip re-packed with signed files" -ForegroundColor Green
}

function Sign-NupkgPackage {
    param([string]$ChannelName)
    
    $releasesDir = "$rootDir\releases-$ChannelName"
    $nupkgFile = Get-ChildItem $releasesDir -Filter "*.nupkg" | Select-Object -First 1
    
    if (-not $nupkgFile) {
        Write-Host "❌ .nupkg not found in $releasesDir" -ForegroundColor Red
        return
    }
    
    Write-Host "`n🔐 Signing .nupkg package: $($nupkgFile.Name)" -ForegroundColor Cyan
    
    # Rozpakuj
    $extractDir = "$releasesDir\nupkg-sign-temp"
    if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force }
    
    # .nupkg to ZIP
    $tempZip = "$releasesDir\temp.zip"
    Copy-Item $nupkgFile.FullName $tempZip
    Expand-Archive -Path $tempZip -DestinationPath $extractDir -Force
    Remove-Item $tempZip
    
    # Znajdź niepodpisane EXE (pomijamy Squirrel.exe - to zewnętrzne)
    $unsignedFiles = @()
    Get-ChildItem $extractDir -Filter "*.exe" -Recurse | Where-Object {
        $_.Name -notlike "*Squirrel*" -and $_.Name -notlike "*ExecutionStub*"
    } | ForEach-Object {
        $sig = Get-AuthenticodeSignature $_.FullName
        if ($sig.Status -ne "Valid") {
            $unsignedFiles += $_
        }
    }
    
    if ($unsignedFiles.Count -eq 0) {
        Write-Host "  ✅ All application files already signed" -ForegroundColor Green
        Remove-Item $extractDir -Recurse -Force
        return
    }
    
    # Podpisz
    foreach ($file in $unsignedFiles) {
        $relativePath = $file.FullName.Replace($extractDir, "").TrimStart("\")
        Write-Host "   Signing: $relativePath" -ForegroundColor Gray
        
        & $signtool sign /sha1 $CertThumbprint `
            /tr $timestampServer /td sha256 /fd sha256 `
            $file.FullName 2>&1 | Out-Null
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "   ✅ Signed" -ForegroundColor Green
        } else {
            Write-Host "   ❌ Failed" -ForegroundColor Red
            exit 1
        }
    }
    
    # Przepakuj (ZIP → .nupkg)
    Remove-Item $nupkgFile.FullName -Force
    Compress-Archive -Path "$extractDir\*" -DestinationPath $tempZip -CompressionLevel Optimal
    Move-Item $tempZip $nupkgFile.FullName -Force
    Remove-Item $extractDir -Recurse -Force
    
    Write-Host "  ✅ .nupkg re-packed with signed files" -ForegroundColor Green
}

# Main
Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║        Post-Signing Velopack Packages                     ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan

if ($Channel -eq "release" -or $Channel -eq "both") {
    Write-Host "`n=== RELEASE CHANNEL ===" -ForegroundColor Yellow
    Sign-PortablePackage -ChannelName "release"
    Sign-NupkgPackage -ChannelName "release"
}

if ($Channel -eq "beta" -or $Channel -eq "both") {
    Write-Host "`n=== BETA CHANNEL ===" -ForegroundColor Yellow
    Sign-PortablePackage -ChannelName "beta"
    Sign-NupkgPackage -ChannelName "beta"
}

Write-Host "`n✅ Post-signing complete!" -ForegroundColor Green
