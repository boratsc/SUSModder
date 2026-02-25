# Build and Package with Velopack - Dual Channel (Release + Beta)
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$BetaSuffix = "beta",
    [switch]$SkipBeta,
    [switch]$SkipRelease
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ProjectFile = Join-Path $ProjectRoot "SUSModder\SUSModder.csproj"
$PublishDirBase = Join-Path $ProjectRoot "publish-dual-channel"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Velopack Dual-Channel Builder" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Yellow
Write-Host "Channels: $(if($SkipRelease){''}else{'release '})$(if($SkipBeta){''}else{'beta'})" -ForegroundColor Yellow
Write-Host ""

# Funkcja do buildowania pojedynczego kanału
function Build-Channel {
    param(
        [string]$ChannelName,
        [string]$ChannelVersion,
        [string]$ChannelCode
    )

    Write-Host ""
    Write-Host "╔══════════════════════════════════════╗" -ForegroundColor Magenta
    Write-Host "║  Building Channel: $ChannelName" -ForegroundColor Magenta
    Write-Host "╚══════════════════════════════════════╝" -ForegroundColor Magenta
    Write-Host ""

    $PublishDir = Join-Path $PublishDirBase "$ChannelCode-temp"
    $ReleasesDir = Join-Path $ProjectRoot "releases-$ChannelCode"

    # 1. Clean previous builds
    Write-Host "[1/5] Cleaning previous builds..." -ForegroundColor Green
    if (Test-Path $PublishDir) {
        Remove-Item $PublishDir -Recurse -Force
    }
    if (Test-Path $ReleasesDir) {
        Remove-Item $ReleasesDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $ReleasesDir -Force | Out-Null
    Write-Host "  OK: Cleaned" -ForegroundColor Green
    Write-Host ""

    # 2. Publish application
    Write-Host "[2/5] Publishing application..." -ForegroundColor Green
    $publishArgs = @(
        "publish"
        $ProjectFile
        "-c", "Release"
        "-r", "win-x64"
        "--self-contained"
        "-o", $PublishDir
        "-p:PublishSingleFile=false"
        "-p:DebugType=none"
        "-p:DebugSymbols=false"
    )

    & dotnet @publishArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ERROR: Publish failed for $ChannelName" -ForegroundColor Red
        throw "Publish failed"
    }

    Write-Host "  OK: Published" -ForegroundColor Green
    Write-Host ""

    # 3. Generate version.json
    Write-Host "[3/5] Generating version.json..." -ForegroundColor Green
    $versionJsonPath = Join-Path $PublishDir "version.json"
    $versionData = @{
        currentVersion = $ChannelVersion
        lastUpdateDate = (Get-Date).ToUniversalTime().ToString("o")
        buildNumber = ""
        channel = $ChannelCode
    } | ConvertTo-Json -Depth 10

    Set-Content -Path $versionJsonPath -Value $versionData -Encoding UTF8
    Write-Host "  OK: version.json created with version $ChannelVersion" -ForegroundColor Green
    Write-Host ""

    # 4. Create Velopack package
    Write-Host "[4/5] Creating Velopack package..." -ForegroundColor Green

    $iconPath = Join-Path $ProjectRoot "SUSModder\Assets\icon.ico"
    $splashPath = Join-Path $ProjectRoot "SUSModder\Assets\splashscreen.jpg"
    $iconArg = if (Test-Path $iconPath) { @("--icon", $iconPath) } else { @() }
    $splashArg = if (Test-Path $splashPath) { @("--splashImage", $splashPath) } else { @() }

    $packArgs = @(
        "pack"
        "--packId", "SUSModder"
        "--packVersion", $ChannelVersion
        "--packDir", $PublishDir
        "--outputDir", $ReleasesDir
        "--channel", $ChannelCode
        "--packTitle", "SUSModder"
        "--packAuthors", "SUSModder Team"
    ) + $iconArg + $splashArg

    & vpk @packArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ERROR: Velopack packaging failed for $ChannelName" -ForegroundColor Red
        throw "Packaging failed"
    }

    Write-Host "  OK: Package created" -ForegroundColor Green
    Write-Host ""

    # Fix: Velopack generates RELEASES-{channel}, but we need RELEASES for compatibility
    $releasesFileWithSuffix = Join-Path $ReleasesDir "RELEASES-$ChannelCode"
    $releasesFile = Join-Path $ReleasesDir "RELEASES"
    
    if (Test-Path $releasesFileWithSuffix) {
        Copy-Item $releasesFileWithSuffix $releasesFile -Force
        Write-Host "  INFO: Created RELEASES from RELEASES-$ChannelCode" -ForegroundColor Gray
    }


    # 5. Verification
    Write-Host "[5/5] Verification..." -ForegroundColor Green
    $nupkgFile = Get-ChildItem $ReleasesDir -Filter "*.nupkg" | Select-Object -First 1

    if (-not $nupkgFile) {
        Write-Host "  ERROR: No nupkg file found for $ChannelName" -ForegroundColor Red
        throw "No package found"
    }

    Write-Host "  OK: Package verified" -ForegroundColor Green
    Write-Host ""
    Write-Host "Generated files for $ChannelName ($ReleasesDir):" -ForegroundColor Cyan
    Get-ChildItem $ReleasesDir | ForEach-Object {
        $size = if ($_.Length -gt 1MB) {
            "{0:N2} MB" -f ($_.Length / 1MB)
        } else {
            "{0:N2} KB" -f ($_.Length / 1KB)
        }
        Write-Host "  - $($_.Name) ($size)" -ForegroundColor White
    }
    Write-Host ""

    # Cleanup temp publish dir
    Remove-Item $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
}

# Check Velopack CLI
Write-Host "[0/5] Checking Velopack CLI..." -ForegroundColor Green
$velopackPath = Get-Command "vpk" -ErrorAction SilentlyContinue

if (-not $velopackPath) {
    Write-Host "  Velopack CLI not found. Installing..." -ForegroundColor Yellow
    dotnet tool install -g vpk

    $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

    $velopackPath = Get-Command "vpk" -ErrorAction SilentlyContinue
    if (-not $velopackPath) {
        Write-Host "  ERROR: Failed to install Velopack CLI" -ForegroundColor Red
        exit 1
    }
}

Write-Host "  OK: Velopack CLI found" -ForegroundColor Green
Write-Host ""

# Build channels
try {
    if (-not $SkipRelease) {
        Build-Channel -ChannelName "RELEASE (Stabilne)" -ChannelVersion $Version -ChannelCode "release"
    }

    if (-not $SkipBeta) {
        # MUSIMY dodać sufix -beta do wersji, inaczej Velopack wpisze do manifestu czystą wersję (np. 2.3.6)
        # To powoduje że release channel (2.2.0) widzi beta (2.3.6) jako nowszą i próbuje aktualizować
        $betaVersion = if ($Version.Contains("-beta")) { $Version } else { "$Version-beta" }
        Build-Channel -ChannelName "BETA (Testowe)" -ChannelVersion $betaVersion -ChannelCode "beta"
    }

    # Final summary
    Write-Host ""
    Write-Host "════════════════════════════════════════" -ForegroundColor Green
    Write-Host "  ALL BUILDS SUCCESSFUL" -ForegroundColor Green
    Write-Host "════════════════════════════════════════" -ForegroundColor Green
    Write-Host ""

    if (-not $SkipRelease) {
        Write-Host "Release channel output:" -ForegroundColor Cyan
        $releaseOutDir = Join-Path $ProjectRoot "releases-release"
        Write-Host "  $releaseOutDir" -ForegroundColor White
        Write-Host ""
    }

    if (-not $SkipBeta) {
        Write-Host "Beta channel output:" -ForegroundColor Cyan
        $betaOutDir = Join-Path $ProjectRoot "releases-beta"
        Write-Host "  $betaOutDir" -ForegroundColor White
        Write-Host ""
    }

    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Upload files from releases-release/ to server (release channel)" -ForegroundColor Gray
    Write-Host "  2. Upload files from releases-beta/ to server (beta channel)" -ForegroundColor Gray
    Write-Host "  3. Update backend API to support channel parameter" -ForegroundColor Gray
    Write-Host "  4. Test update from both channels in application" -ForegroundColor Gray
    Write-Host ""

} catch {
    Write-Host ""
    Write-Host "════════════════════════════════════════" -ForegroundColor Red
    Write-Host "  BUILD FAILED" -ForegroundColor Red
    Write-Host "════════════════════════════════════════" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error: $_" -ForegroundColor Red
    exit 1
}
