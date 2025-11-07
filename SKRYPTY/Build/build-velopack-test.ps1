# Build and Package with Velopack
param([string]$Version = "2.1.0", [string]$Channel = "win")

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot "SUSModder\SUSModder.csproj"
$PublishDir = Join-Path $ProjectRoot "publish-velopack-temp"
$ReleasesDir = Join-Path $ProjectRoot "velopack-releases"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Velopack Build & Package" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Yellow
Write-Host "Channel: $Channel" -ForegroundColor Yellow
Write-Host ""

# 1. Check Velopack CLI
Write-Host "[1/5] Checking Velopack CLI..." -ForegroundColor Green
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

# 2. Clean previous builds
Write-Host "[2/5] Cleaning previous builds..." -ForegroundColor Green
if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}
if (Test-Path $ReleasesDir) {
    Remove-Item $ReleasesDir -Recurse -Force
}
New-Item -ItemType Directory -Path $ReleasesDir -Force | Out-Null
Write-Host "  OK: Cleaned" -ForegroundColor Green
Write-Host ""

# 3. Publish application
Write-Host "[3/5] Publishing application..." -ForegroundColor Green
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
    Write-Host "  ERROR: Publish failed" -ForegroundColor Red
    exit 1
}

Write-Host "  OK: Published" -ForegroundColor Green
Write-Host ""

# 3.5. Generate version.json
Write-Host "[3.5/5] Generating version.json..." -ForegroundColor Green
$versionJsonPath = Join-Path $PublishDir "version.json"
$versionData = @{
    currentVersion = $Version
    lastUpdateDate = (Get-Date).ToUniversalTime().ToString("o")
    buildNumber = ""
} | ConvertTo-Json -Depth 10

Set-Content -Path $versionJsonPath -Value $versionData -Encoding UTF8
Write-Host "  OK: version.json created with version $Version" -ForegroundColor Green
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
    "--packVersion", $Version
    "--packDir", $PublishDir
    "--outputDir", $ReleasesDir
    "--channel", $Channel
    "--packTitle", "SUSModder"
    "--packAuthors", "SUSModder Team"
) + $iconArg + $splashArg

& vpk @packArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: Velopack packaging failed" -ForegroundColor Red
    exit 1
}

Write-Host "  OK: Package created" -ForegroundColor Green
Write-Host ""

# 5. Verification
Write-Host "[5/5] Verification..." -ForegroundColor Green
$nupkgFile = Get-ChildItem $ReleasesDir -Filter "*.nupkg" | Select-Object -First 1

if (-not $nupkgFile) {
    Write-Host "  ERROR: No nupkg file found" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  BUILD SUCCESSFUL" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Output directory:" -ForegroundColor Cyan
Write-Host "  $ReleasesDir" -ForegroundColor White
Write-Host ""
Write-Host "Generated files:" -ForegroundColor Cyan
Get-ChildItem $ReleasesDir | ForEach-Object {
    $size = if ($_.Length -gt 1MB) { 
        "{0:N2} MB" -f ($_.Length / 1MB) 
    } else { 
        "{0:N2} KB" -f ($_.Length / 1KB) 
    }
    Write-Host "  - $($_.Name) ($size)" -ForegroundColor White
}
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Upload files to server" -ForegroundColor Gray
Write-Host "  2. Test update from application" -ForegroundColor Gray
Write-Host ""

# Cleanup
Remove-Item $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
