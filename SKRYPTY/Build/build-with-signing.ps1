# Build with Code Signing
# Signs all EXE files BEFORE Velopack packaging

param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    
    [string]$CertThumbprint = "97171de086564a84fa22a72c4260f72ba13096c6",
    
    [switch]$SkipBeta,
    [switch]$SkipRelease
)

$ErrorActionPreference = "Stop"

# Paths
$rootDir = "D:\Development\SUSModder"
$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe"
$timestampServer = "http://time.certum.pl"

# Check signtool
if (-not (Test-Path $signtool)) {
    Write-Host "❌ signtool.exe not found at: $signtool" -ForegroundColor Red
    exit 1
}

function Sign-ExecutableFiles {
    param([string]$PublishDir)
    
    Write-Host "  🔐 Signing executable files in: $PublishDir" -ForegroundColor Yellow
    
    $exeFiles = Get-ChildItem -Path $PublishDir -Filter "*.exe" -Recurse
    
    foreach ($file in $exeFiles) {
        Write-Host "     Signing: $($file.Name)" -ForegroundColor Gray
        
        & $signtool sign /sha1 $CertThumbprint `
            /tr $timestampServer /td sha256 /fd sha256 `
            $file.FullName 2>&1 | Out-Null
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "     ✅ $($file.Name)" -ForegroundColor Green
        } else {
            Write-Host "     ❌ Failed to sign $($file.Name)" -ForegroundColor Red
            exit 1
        }
    }
    
    Write-Host "  ✅ All executables signed`n" -ForegroundColor Green
}

function Build-Channel {
    param(
        [string]$ChannelName,
        [string]$ChannelVersion,
        [string]$ChannelCode
    )
    
    Write-Host "`n╔══════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║  Building Channel: $ChannelName" -ForegroundColor Cyan
    Write-Host "╚══════════════════════════════════════╝`n" -ForegroundColor Cyan
    
    $publishDir = "$rootDir\publish-dual-channel\$ChannelCode-temp"
    $releasesDir = "$rootDir\releases-$ChannelCode"
    
    # Step 1: Clean
    Write-Host "[1/6] Cleaning..." -ForegroundColor Yellow
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    if (Test-Path $releasesDir) { Remove-Item $releasesDir -Recurse -Force }
    New-Item -ItemType Directory -Path $releasesDir -Force | Out-Null
    Write-Host "  OK: Cleaned`n" -ForegroundColor Green
    
    # Step 2: Publish
    Write-Host "[2/6] Publishing application..." -ForegroundColor Yellow
    dotnet publish "$rootDir\SUSModder\SUSModder.csproj" `
        -c Release -r win-x64 --self-contained `
        -p:PublishSingleFile=false `
        -o $publishDir 2>&1 | Out-Null
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ERROR: Publish failed`n" -ForegroundColor Red
        exit 1
    }
    Write-Host "  OK: Published`n" -ForegroundColor Green
    
    # Step 3: SIGN EXECUTABLES (CRITICAL!)
    Write-Host "[3/6] Signing executables..." -ForegroundColor Yellow
    Sign-ExecutableFiles -PublishDir $publishDir
    
    # Step 4: Generate version.json
    Write-Host "[4/6] Generating version.json..." -ForegroundColor Yellow
    $versionJson = @{
        currentVersion = $ChannelVersion
        lastUpdateDate = (Get-Date -Format "yyyy-MM-dd")
        buildNumber = ""
    } | ConvertTo-Json
    
    $versionJson | Out-File "$publishDir\version.json" -Encoding UTF8
    Write-Host "  OK: version.json created with version $ChannelVersion`n" -ForegroundColor Green
    
    # Step 5: Create Velopack package
    Write-Host "[5/6] Creating Velopack package..." -ForegroundColor Yellow
    
    vpk pack `
        --packId "SUSModder" `
        --packVersion $ChannelVersion `
        --packDir $publishDir `
        --outputDir $releasesDir `
        --channel $ChannelCode `
        --icon "$rootDir\SUSModder\Assets\icon.ico" 2>&1 | Out-Null
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ERROR: Velopack pack failed`n" -ForegroundColor Red
        exit 1
    }
    Write-Host "  OK: Package created`n" -ForegroundColor Green
    
    # Step 5.5: SIGN FILES CREATED BY VELOPACK
    Write-Host "  🔐 Signing Velopack-generated files..." -ForegroundColor Yellow
    
    # Rozpakuj Portable.zip
    $portableZip = Get-ChildItem $releasesDir -Filter "*Portable.zip" | Select-Object -First 1
    if ($portableZip) {
        $portableExtract = "$releasesDir\portable-temp"
        Expand-Archive -Path $portableZip.FullName -DestinationPath $portableExtract -Force
        
        # Podpisz wszystkie niepodpisane EXE
        $unsignedFiles = Get-ChildItem $portableExtract -Filter "*.exe" -Recurse | Where-Object {
            $sig = Get-AuthenticodeSignature $_.FullName
            $sig.Status -ne "Valid"
        }
        
        foreach ($file in $unsignedFiles) {
            Write-Host "     Signing: $($file.Name)" -ForegroundColor Gray
            & $signtool sign /sha1 $CertThumbprint `
                /tr $timestampServer /td sha256 /fd sha256 `
                $file.FullName 2>&1 | Out-Null
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host "     ✅ $($file.Name)" -ForegroundColor Green
            }
        }
        
        # Przepakuj Portable.zip
        Remove-Item $portableZip.FullName -Force
        Compress-Archive -Path "$portableExtract\*" -DestinationPath $portableZip.FullName -CompressionLevel Optimal
        Remove-Item $portableExtract -Recurse -Force
        
        Write-Host "  ✅ Portable.zip re-packed with signed files`n" -ForegroundColor Green
    }
    
    # Step 6: Copy RELEASES file without suffix
    $releasesWithSuffix = "$releasesDir\RELEASES-$ChannelCode"
    $releasesFile = "$releasesDir\RELEASES"
    
    if (Test-Path $releasesWithSuffix) {
        Copy-Item $releasesWithSuffix $releasesFile -Force
        Write-Host "  INFO: Created RELEASES from RELEASES-$ChannelCode" -ForegroundColor Gray
    }
    
    # Step 7: Sign Setup.exe
    Write-Host "[6/6] Signing Setup.exe..." -ForegroundColor Yellow
    $setupFile = Get-ChildItem $releasesDir -Filter "*Setup.exe" | Select-Object -First 1
    
    if ($setupFile) {
        & $signtool sign /sha1 $CertThumbprint `
            /tr $timestampServer /td sha256 /fd sha256 `
            $setupFile.FullName 2>&1 | Out-Null
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  ✅ Setup.exe signed`n" -ForegroundColor Green
        } else {
            Write-Host "  ❌ Failed to sign Setup.exe`n" -ForegroundColor Red
            exit 1
        }
    }
    
    # Show generated files
    Write-Host "Generated files for $ChannelName ($releasesDir):" -ForegroundColor Cyan
    Get-ChildItem $releasesDir | ForEach-Object {
        $sizeKB = [math]::Round($_.Length / 1KB, 2)
        Write-Host "  - $($_.Name) ($sizeKB KB)" -ForegroundColor Gray
    }
    Write-Host ""
}

# Main execution
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Velopack Builder with Code Signing" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Yellow
Write-Host "Cert: $CertThumbprint`n" -ForegroundColor Yellow

cd $rootDir

# Build release channel
if (-not $SkipRelease) {
    $releaseVersion = if ($Version.Contains("-beta")) { $Version.Replace("-beta", "") } else { $Version }
    Build-Channel -ChannelName "RELEASE (Stabilne)" -ChannelVersion $releaseVersion -ChannelCode "release"
}

# Build beta channel  
if (-not $SkipBeta) {
    $betaVersion = if ($Version.Contains("-beta")) { $Version } else { "$Version-beta" }
    Build-Channel -ChannelName "BETA (Testowe)" -ChannelVersion $betaVersion -ChannelCode "beta"
}

Write-Host "════════════════════════════════════════" -ForegroundColor Green
Write-Host "  ALL BUILDS SUCCESSFUL" -ForegroundColor Green
Write-Host "════════════════════════════════════════`n" -ForegroundColor Green

Write-Host "✅ All executables signed before packaging!" -ForegroundColor Green
Write-Host "✅ Setup.exe files signed after packaging!" -ForegroundColor Green
