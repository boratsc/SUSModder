# Build and Package v2.2.0 - Complete Migration Release
# Tworzy:
# 1. Legacy ZIP (dla użytkowników z v2.0.1)
# 2. Velopack release channel
# 3. Velopack beta channel (2.3.0-beta - następny cykl)

param(
    [Parameter(Mandatory = $false)]
    [string]$ReleaseVersion = "2.2.0",
    
    [Parameter(Mandatory = $false)]
    [string]$NextBetaVersion = "2.3.0-beta",
    
    [Parameter(Mandatory = $false)]
    [string]$CertificatePath = "",
    
    [Parameter(Mandatory = $false)]
    [string]$CertificatePassword = "",
    
    [Parameter(Mandatory = $false)]
    [string]$CertificateThumbprint = "",
    
    [switch]$SkipSigning,
    [switch]$SkipLegacyZip
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ProjectFile = Join-Path $ProjectRoot "SUSModder\SUSModder.csproj"
$UpdaterProject = Join-Path $ProjectRoot "Updater\Updater.csproj"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  SUSModder v2.2.0 Release Builder" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Release version: $ReleaseVersion" -ForegroundColor Yellow
Write-Host "Next beta version: $NextBetaVersion" -ForegroundColor Yellow
Write-Host "Signing: $(if($SkipSigning){'DISABLED'}else{'ENABLED'})" -ForegroundColor Yellow
Write-Host ""

# ============================================================================
# HELPER FUNCTIONS
# ============================================================================

function Sign-Executable {
    param(
        [string]$FilePath
    )
    
    if ($SkipSigning) {
        Write-Host "    Skipping signing (SkipSigning flag set)" -ForegroundColor Gray
        return
    }
    
    # Sprawdź czy jest dostępna metoda podpisywania
    $hasThumbprint = -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)
    $hasCertFile = -not [string]::IsNullOrWhiteSpace($CertificatePath)
    
    if (-not $hasThumbprint -and -not $hasCertFile) {
        Write-Host "    Skipping signing (no certificate provided)" -ForegroundColor Yellow
        Write-Host "    Hint: Use -CertificateThumbprint or -CertificatePath parameter" -ForegroundColor Gray
        return
    }
    
    if (-not (Test-Path $FilePath)) {
        Write-Host "    ERROR: File not found: $FilePath" -ForegroundColor Red
        throw "File not found for signing"
    }
    
    Write-Host "    Signing: $(Split-Path -Leaf $FilePath)" -ForegroundColor Cyan
    
    # Metoda 1: Windows Certificate Store (Certum - obecna metoda)
    if ($hasThumbprint) {
        Write-Host "    Using certificate from Windows Store (SHA1: $CertificateThumbprint)" -ForegroundColor Gray
        
        $signToolArgs = @(
            "sign"
            "/sha1", $CertificateThumbprint
            "/fd", "sha256"
            "/tr", "http://time.certum.pl"
            "/td", "sha256"
            "/v"
            $FilePath
        )
        
        & signtool @signToolArgs
    }
    # Metoda 2: PFX File (nowa metoda)
    elseif ($hasCertFile) {
        Write-Host "    Using certificate from file: $CertificatePath" -ForegroundColor Gray
        
        $signToolArgs = @(
            "sign"
            "/f", $CertificatePath
            "/fd", "SHA256"
            "/tr", "http://timestamp.digicert.com"
            "/td", "SHA256"
        )
        
        if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) {
            $signToolArgs += @("/p", $CertificatePassword)
        }
        
        $signToolArgs += $FilePath
        
        & signtool @signToolArgs
    }
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "    ERROR: Signing failed" -ForegroundColor Red
        throw "Signing failed"
    }
    
    Write-Host "    OK: Signed successfully" -ForegroundColor Green
}

function Clean-Directory {
    param([string]$Path)
    
    if (Test-Path $Path) {
        Remove-Item $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Export-PortableUpdateExe {
    param(
        [string]$ReleasesDir
    )

    $portableZip = Get-ChildItem $ReleasesDir -Filter "*Portable.zip" | Select-Object -First 1
    if (-not $portableZip) {
        Write-Host "  WARNING: Portable.zip not found, skipping Update.exe export" -ForegroundColor Yellow
        return
    }

    $tempDir = Join-Path $ReleasesDir "portable-update-temp"
    if (Test-Path $tempDir) {
        Remove-Item $tempDir -Recurse -Force
    }

    Expand-Archive -Path $portableZip.FullName -DestinationPath $tempDir -Force

    $updateExeSource = Join-Path $tempDir "Update.exe"
    $updateExeDest = Join-Path $ReleasesDir "Update.exe"

    if (Test-Path $updateExeSource) {
        Copy-Item $updateExeSource $updateExeDest -Force
        Write-Host "  INFO: Exported Update.exe from Portable.zip" -ForegroundColor Gray
    } else {
        Write-Host "  WARNING: Update.exe not found inside Portable.zip" -ForegroundColor Yellow
    }

    Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}

# ============================================================================
# PHASE 1: BUILD LEGACY ZIP (dla użytkowników z v2.0.1)
# ============================================================================

if (-not $SkipLegacyZip) {
    Write-Host ""
    Write-Host "╔══════════════════════════════════════╗" -ForegroundColor Magenta
    Write-Host "║  PHASE 1: Legacy ZIP Package" -ForegroundColor Magenta
    Write-Host "╚══════════════════════════════════════╝" -ForegroundColor Magenta
    Write-Host ""
    
    $legacyPublishDir = Join-Path $ProjectRoot "publish-legacy"
    $legacyOutputDir = Join-Path $ProjectRoot "releases-legacy"
    
    Clean-Directory $legacyPublishDir
    Clean-Directory $legacyOutputDir
    
    # 1.1 Build Updater.exe
    Write-Host "[1/6] Building Updater.exe..." -ForegroundColor Green
    $updaterPublishDir = Join-Path $legacyPublishDir "updater"
    
    $updaterArgs = @(
        "publish"
        $UpdaterProject
        "-c", "Release"
        "-r", "win-x64"
        "--self-contained"
        "-o", $updaterPublishDir
        "-p:PublishSingleFile=true"
        "-p:DebugType=none"
        "-p:DebugSymbols=false"
    )
    
    & dotnet @updaterArgs
    
    if ($LASTEXITCODE -ne 0) {
        throw "Updater build failed"
    }
    
    Sign-Executable -FilePath (Join-Path $updaterPublishDir "Updater.exe")
    Write-Host "  OK: Updater built and signed" -ForegroundColor Green
    Write-Host ""
    
    # 1.2 Build Main App (single-file with Velopack embedded)
    Write-Host "[2/6] Building main application..." -ForegroundColor Green
    $mainPublishDir = Join-Path $legacyPublishDir "app"
    
    $mainArgs = @(
        "publish"
        $ProjectFile
        "-c", "Release"
        "-r", "win-x64"
        "--self-contained"
        "-o", $mainPublishDir
        "-p:PublishSingleFile=true"
        "-p:DebugType=none"
        "-p:DebugSymbols=false"
    )
    
    & dotnet @mainArgs
    
    if ($LASTEXITCODE -ne 0) {
        throw "Main app build failed"
    }
    
    Sign-Executable -FilePath (Join-Path $mainPublishDir "SUSModder.exe")
    Write-Host "  OK: Main app built and signed" -ForegroundColor Green
    Write-Host ""
    
    # 1.3 Copy necessary files
    Write-Host "[3/6] Copying additional files..." -ForegroundColor Green
    
    # Kopiuj tools
    $toolsSourceDir = Join-Path $ProjectRoot "SUSModder\tools"
    $toolsDestDir = Join-Path $mainPublishDir "tools"
    if (Test-Path $toolsSourceDir) {
        Copy-Item $toolsSourceDir -Destination $toolsDestDir -Recurse -Force
    }
    
    # Kopiuj appsettings.json
    $appsettingsSource = Join-Path $ProjectRoot "SUSModder\appsettings.json"
    $appsettingsDest = Join-Path $mainPublishDir "appsettings.json"
    if (Test-Path $appsettingsSource) {
        Copy-Item $appsettingsSource -Destination $appsettingsDest -Force
    }
    
    # Kopiuj uninstall.ps1
    $uninstallScript = Join-Path $ProjectRoot "SKRYPTY\Utilities\uninstall.ps1"
    $uninstallDest = Join-Path $mainPublishDir "uninstall.ps1"
    if (Test-Path $uninstallScript) {
        Copy-Item $uninstallScript -Destination $uninstallDest -Force
        Write-Host "    Added: uninstall.ps1" -ForegroundColor Gray
    }
    
    Write-Host "  OK: Additional files copied" -ForegroundColor Green
    Write-Host ""
    
    # 1.4 Generate version.json
    Write-Host "[4/6] Generating version.json..." -ForegroundColor Green
    $versionJsonPath = Join-Path $mainPublishDir "version.json"
    $versionData = @{
        currentVersion = $ReleaseVersion
        lastUpdateDate = (Get-Date).ToUniversalTime().ToString("o")
        buildNumber = ""
        channel = "release"
    } | ConvertTo-Json -Depth 10
    
    Set-Content -Path $versionJsonPath -Value $versionData -Encoding UTF8
    Write-Host "  OK: version.json created" -ForegroundColor Green
    Write-Host ""
    
    # 1.5 Create ZIP package
    Write-Host "[5/6] Creating ZIP package..." -ForegroundColor Green
    $zipFileName = "SUSModder-$ReleaseVersion-legacy.zip"
    $zipPath = Join-Path $legacyOutputDir $zipFileName
    
    # UWAGA: Legacy ZIP dla użytkowników 2.0.2 → 2.2.2
    # Struktura ZIP NIE może zawierać Velopack (Update.exe), bo:
    # 1. Legacy Updater.exe nie wie jak obsłużyć struktury current/
    # 2. Update.exe wymaga pełnej instalacji przez Setup.exe
    # 
    # Po aktualizacji użytkownik dostanie komunikat w aplikacji:
    # "Wykryto aktualizację do 2.2.2. Pobierz Setup.exe dla pełnej migracji do Velopack."
    # 
    # Struktura ZIP:
    # /SUSModder.exe
    # /appsettings.json
    # /version.json
    # /tools/...
    # /updater/Updater.exe
    
    Compress-Archive -Path "$mainPublishDir\*" -DestinationPath $zipPath -Force
    
    # Dodaj updater do ZIP
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Update')
    
    $updaterExe = Join-Path $updaterPublishDir "Updater.exe"
    $entryName = "updater\Updater.exe"
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $updaterExe, $entryName) | Out-Null
    
    $zip.Dispose()
    
    Write-Host "  OK: ZIP package created" -ForegroundColor Green
    Write-Host ""
    
    # 1.6 Verification
    Write-Host "[6/6] Verification..." -ForegroundColor Green
    $zipSize = (Get-Item $zipPath).Length / 1MB
    Write-Host "  Package: $zipFileName ($([Math]::Round($zipSize, 2)) MB)" -ForegroundColor Cyan
    Write-Host "  OK: Legacy package ready" -ForegroundColor Green
    Write-Host ""
}

# ============================================================================
# PHASE 2: BUILD VELOPACK RELEASE CHANNEL
# ============================================================================

Write-Host ""
Write-Host "╔══════════════════════════════════════╗" -ForegroundColor Magenta
Write-Host "║  PHASE 2: Velopack Release Channel" -ForegroundColor Magenta
Write-Host "╚══════════════════════════════════════╝" -ForegroundColor Magenta
Write-Host ""

$releasePublishDir = Join-Path $ProjectRoot "publish-velopack-release"
$releaseOutputDir = Join-Path $ProjectRoot "releases-release"

Clean-Directory $releasePublishDir
Clean-Directory $releaseOutputDir

# 2.1 Publish
Write-Host "[1/5] Publishing application..." -ForegroundColor Green
$publishArgs = @(
    "publish"
    $ProjectFile
    "-c", "Release"
    "-r", "win-x64"
    "--self-contained"
    "-o", $releasePublishDir
    "-p:PublishSingleFile=false"
    "-p:DebugType=none"
    "-p:DebugSymbols=false"
)

& dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    throw "Publish failed"
}

Write-Host "  OK: Published" -ForegroundColor Green
Write-Host ""

# 2.2 Sign main executable
Write-Host "[2/5] Signing executables..." -ForegroundColor Green
Sign-Executable -FilePath (Join-Path $releasePublishDir "SUSModder.exe")
Write-Host "  OK: Signed" -ForegroundColor Green
Write-Host ""

# 2.3 Generate version.json
Write-Host "[3/5] Generating version.json..." -ForegroundColor Green
$versionJsonPath = Join-Path $releasePublishDir "version.json"
$versionData = @{
    currentVersion = $ReleaseVersion
    lastUpdateDate = (Get-Date).ToUniversalTime().ToString("o")
    buildNumber = ""
    channel = "release"
} | ConvertTo-Json -Depth 10

Set-Content -Path $versionJsonPath -Value $versionData -Encoding UTF8
Write-Host "  OK: version.json created" -ForegroundColor Green
Write-Host ""

# 2.4 Create Velopack package
Write-Host "[4/5] Creating Velopack package..." -ForegroundColor Green

$iconPath = Join-Path $ProjectRoot "SUSModder\Assets\icon.ico"
$splashPath = Join-Path $ProjectRoot "SUSModder\Assets\splashscreen.jpg"
$iconArg = if (Test-Path $iconPath) { @("--icon", $iconPath) } else { @() }
$splashArg = if (Test-Path $splashPath) { @("--splashImage", $splashPath) } else { @() }

$packArgs = @(
    "pack"
    "--packId", "SUSModder"
    "--packVersion", $ReleaseVersion
    "--packDir", $releasePublishDir
    "--outputDir", $releaseOutputDir
    "--channel", "release"
    "--packTitle", "SUSModder"
    "--packAuthors", "SUSModder Team"
) + $iconArg + $splashArg

& vpk @packArgs

if ($LASTEXITCODE -ne 0) {
    throw "Velopack packaging failed"
}

Write-Host "  OK: Package created" -ForegroundColor Green
Write-Host ""

# Ensure Setup.exe exists and sign it (Velopack pack already generates it)
$genSetupRelease = Get-ChildItem $releaseOutputDir -Filter "*Setup*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($genSetupRelease) {
    $releaseSetupPath = Join-Path $releaseOutputDir "SUSModder-release-Setup.exe"
    if ($genSetupRelease.Name -ne (Split-Path -Leaf $releaseSetupPath)) {
        Move-Item -LiteralPath $genSetupRelease.FullName -Destination $releaseSetupPath -Force
    }
}

Write-Host "[4.2/5] Signing Setup.exe..." -ForegroundColor Green
$setupExe = Join-Path $releaseOutputDir "SUSModder-release-Setup.exe"
if (Test-Path $setupExe) {
    Sign-Executable -FilePath $setupExe
    Write-Host "  OK: Setup.exe signed" -ForegroundColor Green
} else {
    Write-Host "  WARNING: Setup.exe not found, skipping" -ForegroundColor Yellow
}
Write-Host ""

Export-PortableUpdateExe -ReleasesDir $releaseOutputDir
Write-Host ""

# 2.5 Verification
Write-Host "[5/5] Verification..." -ForegroundColor Green
$nupkgFile = Get-ChildItem $releaseOutputDir -Filter "*.nupkg" | Select-Object -First 1

if (-not $nupkgFile) {
    throw "No nupkg file found"
}

Write-Host "  OK: Package verified" -ForegroundColor Green
Write-Host ""

# ============================================================================
# PHASE 3: BUILD VELOPACK BETA CHANNEL
# ============================================================================

Write-Host ""
Write-Host "╔══════════════════════════════════════╗" -ForegroundColor Magenta
Write-Host "║  PHASE 3: Velopack Beta Channel" -ForegroundColor Magenta
Write-Host "╚══════════════════════════════════════╝" -ForegroundColor Magenta
Write-Host ""

$betaPublishDir = Join-Path $ProjectRoot "publish-velopack-beta"
$betaOutputDir = Join-Path $ProjectRoot "releases-beta"

Clean-Directory $betaPublishDir
Clean-Directory $betaOutputDir

# 3.1 Publish
Write-Host "[1/5] Publishing application..." -ForegroundColor Green
$publishArgs = @(
    "publish"
    $ProjectFile
    "-c", "Release"
    "-r", "win-x64"
    "--self-contained"
    "-o", $betaPublishDir
    "-p:PublishSingleFile=false"
    "-p:DebugType=none"
    "-p:DebugSymbols=false"
)

& dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    throw "Publish failed"
}

Write-Host "  OK: Published" -ForegroundColor Green
Write-Host ""

# 3.2 Sign main executable
Write-Host "[2/5] Signing executables..." -ForegroundColor Green
Sign-Executable -FilePath (Join-Path $betaPublishDir "SUSModder.exe")
Write-Host "  OK: Signed" -ForegroundColor Green
Write-Host ""

# 3.3 Generate version.json
Write-Host "[3/5] Generating version.json..." -ForegroundColor Green
$versionJsonPath = Join-Path $betaPublishDir "version.json"
$versionData = @{
    currentVersion = $NextBetaVersion
    lastUpdateDate = (Get-Date).ToUniversalTime().ToString("o")
    buildNumber = ""
    channel = "beta"
} | ConvertTo-Json -Depth 10

Set-Content -Path $versionJsonPath -Value $versionData -Encoding UTF8
Write-Host "  OK: version.json created" -ForegroundColor Green
Write-Host ""

# 3.4 Create Velopack package
Write-Host "[4/5] Creating Velopack package..." -ForegroundColor Green

$packArgs = @(
    "pack"
    "--packId", "SUSModder"
    "--packVersion", $NextBetaVersion
    "--packDir", $betaPublishDir
    "--outputDir", $betaOutputDir
    "--channel", "beta"
    "--packTitle", "SUSModder Beta"
    "--packAuthors", "SUSModder Team"
) + $iconArg + $splashArg

& vpk @packArgs

if ($LASTEXITCODE -ne 0) {
    throw "Velopack packaging failed"
}

Write-Host "  OK: Package created" -ForegroundColor Green
Write-Host ""

# Ensure Setup.exe exists and sign it (Velopack pack already generates it)
$genSetupBeta = Get-ChildItem $betaOutputDir -Filter "*Setup*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($genSetupBeta) {
    $betaSetupPath = Join-Path $betaOutputDir "SUSModder-beta-Setup.exe"
    if ($genSetupBeta.Name -ne (Split-Path -Leaf $betaSetupPath)) {
        Move-Item -LiteralPath $genSetupBeta.FullName -Destination $betaSetupPath -Force
    }
}

Write-Host "[4.2/5] Signing Setup.exe..." -ForegroundColor Green
$setupExe = Join-Path $betaOutputDir "SUSModder-beta-Setup.exe"
if (Test-Path $setupExe) {
    Sign-Executable -FilePath $setupExe
    Write-Host "  OK: Setup.exe signed" -ForegroundColor Green
} else {
    Write-Host "  WARNING: Setup.exe not found, skipping" -ForegroundColor Yellow
}
Write-Host ""

Export-PortableUpdateExe -ReleasesDir $betaOutputDir
Write-Host ""

# 3.5 Verification
Write-Host "[5/5] Verification..." -ForegroundColor Green
$nupkgFile = Get-ChildItem $betaOutputDir -Filter "*.nupkg" | Select-Object -First 1

if (-not $nupkgFile) {
    throw "No nupkg file found"
}

Write-Host "  OK: Package verified" -ForegroundColor Green
Write-Host ""

# ============================================================================
# FINAL SUMMARY
# ============================================================================

Write-Host ""
Write-Host "════════════════════════════════════════" -ForegroundColor Green
Write-Host "  ALL BUILDS SUCCESSFUL" -ForegroundColor Green
Write-Host "════════════════════════════════════════" -ForegroundColor Green
Write-Host ""

if (-not $SkipLegacyZip) {
    Write-Host "Legacy ZIP (for v2.0.1 users):" -ForegroundColor Cyan
    Write-Host "  $(Join-Path $ProjectRoot 'releases-legacy')" -ForegroundColor White
    Get-ChildItem (Join-Path $ProjectRoot 'releases-legacy') | ForEach-Object {
        $size = "{0:N2} MB" -f ($_.Length / 1MB)
        Write-Host "    - $($_.Name) ($size)" -ForegroundColor Gray
    }
    Write-Host ""
}

Write-Host "Velopack Release channel:" -ForegroundColor Cyan
Write-Host "  $(Join-Path $ProjectRoot 'releases-release')" -ForegroundColor White
Get-ChildItem (Join-Path $ProjectRoot 'releases-release') | ForEach-Object {
    $size = "{0:N2} MB" -f ($_.Length / 1MB)
    Write-Host "    - $($_.Name) ($size)" -ForegroundColor Gray
}
Write-Host ""

Write-Host "Velopack Beta channel:" -ForegroundColor Cyan
Write-Host "  $(Join-Path $ProjectRoot 'releases-beta')" -ForegroundColor White
Get-ChildItem (Join-Path $ProjectRoot 'releases-beta') | ForEach-Object {
    $size = "{0:N2} MB" -f ($_.Length / 1MB)
    Write-Host "    - $($_.Name) ($size)" -ForegroundColor Gray
}
Write-Host ""

Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Upload legacy ZIP to: /api/download-latest" -ForegroundColor Gray
Write-Host "  2. Upload release channel to: https://susmodder.app/releases/release/" -ForegroundColor Gray
Write-Host "  3. Upload beta channel to: https://susmodder.app/releases/beta/" -ForegroundColor Gray
Write-Host "  4. Update backend API:" -ForegroundColor Gray
Write-Host "     - /api/susmodder-current-version -> return 2.2.0" -ForegroundColor Gray
Write-Host "     - /api/releases?channel=release -> release manifest" -ForegroundColor Gray
Write-Host "     - /api/releases?channel=beta -> beta manifest" -ForegroundColor Gray
Write-Host "  5. Test migration path: v2.0.1 -> v2.2.0 (legacy)" -ForegroundColor Gray
Write-Host "  6. Test update path: v2.2.0 -> future versions (Velopack)" -ForegroundColor Gray
Write-Host ""

Write-Host "Deployment checklist:" -ForegroundColor Yellow
Write-Host "  [ ] Backend updated with new endpoints" -ForegroundColor Gray
Write-Host "  [ ] Legacy ZIP uploaded and accessible" -ForegroundColor Gray
Write-Host "  [ ] Velopack packages uploaded to both channels" -ForegroundColor Gray
Write-Host "  [ ] Test update from v2.0.1" -ForegroundColor Gray
Write-Host "  [ ] Test channel switching (release and beta)" -ForegroundColor Gray
Write-Host "  [ ] Monitor telemetry for migration success rate" -ForegroundColor Gray
Write-Host ""
