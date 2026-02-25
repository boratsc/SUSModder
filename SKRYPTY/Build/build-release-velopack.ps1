param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$PreviousVersion = "",

    [switch]$SignPackages
)

$ErrorActionPreference = "Stop"

Write-Host "════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  SUSModder Velopack Release Builder" -ForegroundColor Cyan
Write-Host "════════════════════════════════════════" -ForegroundColor Cyan

# --- Step 1: Validate environment -------------------------------------------------
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet CLI not found in PATH. Install .NET 10 SDK."
}

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "Velopack CLI (vpk) not available. Install with 'dotnet tool install -g vpk'."
}

# --- Step 2: Update version metadata ----------------------------------------------
$appSettingsPath = Join-Path $PSScriptRoot 'SUSModder/appsettings.json'
if (-not (Test-Path $appSettingsPath)) {
    throw "Cannot find appsettings.json at $appSettingsPath"
}

Write-Host "[1/6] Updating configuration version -> $Version" -ForegroundColor Yellow
$appSettingsContent = Get-Content $appSettingsPath -Raw
$appSettingsContent = $appSettingsContent -replace '"CurrentVersion"\s*:\s*"[^"]*"', '"CurrentVersion": "' + $Version + '"'
Set-Content -Path $appSettingsPath -Value $appSettingsContent -Encoding UTF8

# --- Step 3: Clean publish/output directories -------------------------------------
$publishDir = Join-Path $PSScriptRoot 'SUSModder/bin/Release/net10.0-windows/win-x64/publish'
$releasesDir = Join-Path $PSScriptRoot 'Releases'

Write-Host "[2/6] Cleaning previous artifacts" -ForegroundColor Yellow
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $releasesDir) { Remove-Item $releasesDir -Recurse -Force }

# --- Step 4: Publish single-file app ----------------------------------------------
Write-Host "[3/6] Publishing SUSModder" -ForegroundColor Yellow
$publishArgs = @(
    'publish',
    (Join-Path $PSScriptRoot 'SUSModder/SUSModder.csproj'),
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true'
)

$publishResult = & dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

if (-not (Test-Path $publishDir)) {
    throw "Publish directory not found at $publishDir"
}

# --- Step 4.5: Generate version.json -----------------------------------------------
Write-Host "[3.5/6] Generating version.json" -ForegroundColor Yellow
$versionJsonPath = Join-Path $publishDir "version.json"
$versionData = @{
    currentVersion = $Version
    lastUpdateDate = (Get-Date).ToUniversalTime().ToString("o")
    buildNumber = ""
} | ConvertTo-Json -Depth 10

Set-Content -Path $versionJsonPath -Value $versionData -Encoding UTF8
Write-Host "  version.json created with version $Version" -ForegroundColor Green

# --- Step 5: Pack with Velopack ----------------------------------------------------
Write-Host "[4/6] Packing Velopack release" -ForegroundColor Yellow
New-Item -ItemType Directory -Path $releasesDir -Force | Out-Null

$iconPath = Join-Path $PSScriptRoot 'SUSModder/Assets/icon.ico'
$splashPath = Join-Path $PSScriptRoot 'SUSModder/Assets/splashscreen.jpg'

$vpkArgs = @(
    'pack',
    '--packId', 'SUSModder',
    '--packVersion', $Version,
    '--packDir', $publishDir,
    '--mainExe', 'SUSModder.exe',
    '--packTitle', 'SUSModder - Among Us Mod Manager',
    '--icon', $iconPath,
    '--outputDir', $releasesDir
)

# Dodaj splash screen jeśli istnieje
if (Test-Path $splashPath) {
    $vpkArgs += @('--splashImage', $splashPath)
    Write-Host "  Using splash image: $splashPath" -ForegroundColor Cyan
}

if ($PreviousVersion) {
    $previousPackage = Join-Path $releasesDir ("SUSModder-${PreviousVersion}-full.nupkg")
    if (Test-Path $previousPackage) {
        $vpkArgs += @('--delta', $previousPackage)
    }
    else {
        Write-Warning "Delta source package $previousPackage not found. Generating full package only."
    }
}

if ($SignPackages) {
    if (-not $env:VELOPACK_SIGN_TEMPLATE) {
        throw "Sign requested but VELOPACK_SIGN_TEMPLATE env variable is not set."
    }
    $vpkArgs += @('--signTemplate', $env:VELOPACK_SIGN_TEMPLATE)
}

& vpk @vpkArgs
if ($LASTEXITCODE -ne 0) {
    throw "vpk pack failed."
}

# --- Step 6: Summary ----------------------------------------------------------------
$fullPackage = Join-Path $releasesDir ("SUSModder-${Version}-full.nupkg")
$setupExe = Get-ChildItem $releasesDir -Filter "*Setup*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1

Write-Host "[5/6] Generated package:" -ForegroundColor Yellow
if (Test-Path $fullPackage) {
    Write-Host "  • $fullPackage" -ForegroundColor Green
}
if ($setupExe) {
    Write-Host "  • $($setupExe.FullName)" -ForegroundColor Green
}

Write-Host "[6/6] Release build completed successfully" -ForegroundColor Cyan
Write-Host "Next steps:" -ForegroundColor Gray
Write-Host "  1. Upload contents of $releasesDir to the /releases endpoint." -ForegroundColor Gray
Write-Host "  2. If distributing legacy ZIP, ensure bridge package is still published." -ForegroundColor Gray
Write-Host "  3. Smoke test installer and update flow before announcing release." -ForegroundColor Gray
