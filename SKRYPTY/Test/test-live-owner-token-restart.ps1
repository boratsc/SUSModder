# LIVE ownerToken restart proof (Outline 02.8): two SEPARATE `dotnet test` runs = two real processes.
# Phase1 creates two packs and persists DPAPI tokens in an isolated temp store;
# Phase2 (NEW process) reads that store from disk and verifies per-pack token behavior.
# The real user store in %APPDATA% is never touched; temp dir is removed at the end.
param(
    [string]$ApiBase = "http://192.168.1.182:3380/api/v2"
)

$ErrorActionPreference = "Stop"
if (-not $IsWindows) { Write-Host "FAIL: ownerToken DPAPI wymaga Windows." -ForegroundColor Red; exit 1 }

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$testProject = Join-Path $repoRoot "SUSModder.Core.Tests\SUSModder.Core.Tests.csproj"
if (-not (Test-Path $testProject)) { Write-Host "FAIL: nie znaleziono $testProject" -ForegroundColor Red; exit 1 }

$runDir = Join-Path ([System.IO.Path]::GetTempPath()) ("SUSModder.LiveRestart-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $runDir | Out-Null
$storePath = Join-Path $runDir "pack-owner-tokens"

$prevApiBase = $env:SUSMODDER_LIVE_API_BASE
$prevStorePath = $env:SUSMODDER_LIVE_STORE_PATH
$env:SUSMODDER_LIVE_API_BASE = $ApiBase
$env:SUSMODDER_LIVE_STORE_PATH = $storePath

function Invoke-Phase {
    param([string]$Name, [string]$Filter)
    Write-Host ""
    Write-Host "=== $Name (nowy proces dotnet test) ===" -ForegroundColor Cyan
    # Out-Host: keep dotnet output out of the function's return pipeline (only $ok is returned).
    dotnet test $testProject --filter $Filter --nologo -v minimal | Out-Host
    $ok = ($LASTEXITCODE -eq 0)
    $verdict = if ($ok) { "PASS" } else { "FAIL" }
    $color = if ($ok) { "Green" } else { "Red" }
    Write-Host "=== $Name`: $verdict ===" -ForegroundColor $color
    return $ok
}

$exitCode = 1
try {
    Write-Host "API: $ApiBase"
    Write-Host "Izolowany magazyn (temp): $storePath"

    $phase1 = Invoke-Phase "Phase1_CreateTwoPacks" "FullyQualifiedName~ModPackServiceLiveRestartTests.Phase1_CreateTwoPacks"
    if ($phase1) {
        $phase2 = Invoke-Phase "Phase2_AfterProcessRestart" "FullyQualifiedName~ModPackServiceLiveRestartTests.Phase2_AfterProcessRestart"
        if ($phase2) { $exitCode = 0 }
    }
    else {
        Write-Host "Phase2 pominieta (Phase1 nie przeszla)." -ForegroundColor Yellow
    }

    Write-Host ""
    if ($exitCode -eq 0) {
        Write-Host "WYNIK: PASS (Phase1 PASS, Phase2 PASS - real process restart proven)" -ForegroundColor Green
    }
    else {
        Write-Host "WYNIK: FAIL" -ForegroundColor Red
    }
}
finally {
    if ($null -eq $prevApiBase) { Remove-Item Env:SUSMODDER_LIVE_API_BASE -ErrorAction SilentlyContinue }
    else { $env:SUSMODDER_LIVE_API_BASE = $prevApiBase }
    if ($null -eq $prevStorePath) { Remove-Item Env:SUSMODDER_LIVE_STORE_PATH -ErrorAction SilentlyContinue }
    else { $env:SUSMODDER_LIVE_STORE_PATH = $prevStorePath }
    Remove-Item $runDir -Recurse -Force -ErrorAction SilentlyContinue
}
exit $exitCode
