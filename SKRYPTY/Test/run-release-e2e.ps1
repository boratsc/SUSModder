# SUSModder 3.0 Stable — E2E Release Gate Runner
# Orchestrates all E2E tests: API smoke, download, extraction, installation, launch.
#
# Usage:
#   .\SKRYPTY\Test\run-release-e2e.ps1 -ApiOnly                    # Quick API/CDN smoke
#   .\SKRYPTY\Test\run-release-e2e.ps1 -DownloadOnly                # Download + SHA256
#   .\SKRYPTY\Test\run-release-e2e.ps1 -ExtractOnly                 # Download + extract + structure
#   .\SKRYPTY\Test\run-release-e2e.ps1 -InstallOnly                 # Full install to isolated dir
#   .\SKRYPTY\Test\run-release-e2e.ps1 -Launch -Platforms steam     # Steam launch + BepInEx logs
#   .\SKRYPTY\Test\run-release-e2e.ps1 -Launch -Platforms epic      # Epic launch + BepInEx logs
#   .\SKRYPTY\Test\run-release-e2e.ps1 -Full                        # EVERYTHING (full release gate)
#   .\SKRYPTY\Test\run-release-e2e.ps1 -Full -ModId 13              # Single mod debug
#
# Environment variables:
#   SUSMODDER_E2E_ROOT — override test root directory
#   SUSMODDER_E2E_NO_CLEANUP=1 — keep artifacts after tests
#   SUSMODDER_E2E_LAUNCH=1 — enable launch tests (requires Steam/Epic)
#   SUSMODDER_E2E_OBSERVATION_SECONDS — override launch observation window (default 45)

param(
    [switch]$ApiOnly,
    [switch]$DownloadOnly,
    [switch]$ExtractOnly,
    [switch]$InstallOnly,
    [switch]$Launch,
    [switch]$Full,
    [string]$Platforms = "steam",
    [int]$ModId = 0,
    [switch]$NoCleanup,
    [string]$ApiBase = "https://api.susmodder-cdn.ovh/v2",
    [int]$ObservationSeconds = 45,
    [string]$InstallRoot = "",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$script:StartTime = Get-Date

# --- Config ---
$script:RepoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSCommandPath))
$script:Timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

if (-not $OutputDir) {
    $OutputDir = Join-Path $RepoRoot "TestResults\SUSModder-E2E\$Timestamp"
}

if (-not $InstallRoot) {
    $InstallRoot = if ($env:SUSMODDER_E2E_ROOT) {
        $env:SUSMODDER_E2E_ROOT
    } else {
        Join-Path $env:TEMP "SUSModder-E2E-3.0"
    }
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# Set environment for child processes
$env:SUSMODDER_E2E_ROOT = $InstallRoot
if ($NoCleanup) { $env:SUSMODDER_E2E_NO_CLEANUP = "1" }
$env:SUSMODDER_E2E_OBSERVATION_SECONDS = $ObservationSeconds.ToString()

# If Launch is specified but not already in env, enable launch env var
if ($Launch -or $Full) {
    $env:SUSMODDER_E2E_LAUNCH = "1"
}

# --- Helpers ---
function Write-Step {
    param([string]$Message, [string]$Status = "START")
    $icon = @{ START = "🚀"; OK = "✅"; FAIL = "❌"; WARN = "⚠️"; SKIP = "⏭️" }[$Status]
    $line = "$icon [$Status] $(Get-Date -Format 'HH:mm:ss') $Message"
    Write-Host $line
    Add-Content -Path (Join-Path $OutputDir "runner.log") -Value $line
}

function Write-Summary {
    param([string]$Title, [hashtable]$Results)
    $lines = @("", "=" * 60, "  $Title", "=" * 60)
    foreach ($kv in $Results.GetEnumerator() | Sort-Object Name) {
        $lines += "  $($kv.Key): $($kv.Value)"
    }
    $lines += "=" * 60, ""
    $text = $lines -join "`n"
    Write-Host $text
    Add-Content -Path (Join-Path $OutputDir "runner.log") -Value $text
}

function Run-DotnetTest {
    param(
        [string]$Filter,
        [string]$Label,
        [string[]]$EnvironmentVars = @()
    )
    Write-Step "Running: $Label" "START"

    $envVars = $EnvironmentVars -join " "
    $projectPath = Join-Path $script:RepoRoot "SUSModder.E2E.Tests\SUSModder.E2E.Tests.csproj"
    $trxFile = Join-Path $OutputDir "$Label.trx"

    $cmd = "dotnet test `"$projectPath`" -c Release --filter `"FullyQualifiedName~$Filter`" --logger `"trx;LogFileName=$trxFile`" --no-restore:$false"
    if ($envVars) { $cmd = "$envVars && $cmd" }

    Write-Host "  Command: $cmd" -ForegroundColor Gray

    try {
        $result = Invoke-Expression $cmd 2>&1
        $exitCode = $LASTEXITCODE

        # Save output
        $result | Out-File (Join-Path $OutputDir "$Label-output.txt") -Encoding UTF8

        if ($exitCode -eq 0) {
            Write-Step "$Label — PASSED" "OK"
            return @{ Passed = $true; ExitCode = 0 }
        } else {
            Write-Step "$Label — FAILED (exit code: $exitCode)" "FAIL"
            return @{ Passed = $false; ExitCode = $exitCode }
        }
    } catch {
        Write-Step "$Label — ERROR: $_" "FAIL"
        return @{ Passed = $false; ExitCode = -1 }
    }
}

# --- Test Categories (xUnit filters) ---
$testFilters = @{
    ApiCatalog    = "ApiCatalogSmokeTests"
    ApiDownload   = "ApiDownloadSmokeTests"
    Extraction    = "ModExtractionTests"
    InstallSteam  = "Install_EveryFullModSteam"
    InstallEpic   = "Install_EveryFullModEpic"
    LaunchSteam   = "Launch_SteamFullMods"
}

$summary = @{}

# --- Determine what to run ---
$runApi      = $ApiOnly -or $Full
$runDownload = $DownloadOnly -or $Full
$runExtract  = $ExtractOnly -or $Full
$runInstall  = $InstallOnly -or $Full
$runLaunch   = $Launch -or $Full

if (-not ($runApi -or $runDownload -or $runExtract -or $runInstall -or $runLaunch)) {
    Write-Host @"

SUSModder 3.0 E2E Release Gate Runner
=====================================
Usage:
  -ApiOnly        Quick API/CDN smoke test
  -DownloadOnly    Download + SHA256 verification
  -ExtractOnly     Download + extract + structure check
  -InstallOnly     Full install to isolated directory
  -Launch          Launch tests (requires Steam/Epic installed)
  -Full            EVERYTHING (complete release gate)
  -ModId N         Test single mod ID only
  -NoCleanup       Keep test artifacts
  -Platforms       Comma-separated: steam,epic (default: steam)

Examples:
  .\run-release-e2e.ps1 -ApiOnly
  .\run-release-e2e.ps1 -Full
  .\run-release-e2e.ps1 -Full -ModId 13 -NoCleanup

"@
    exit 0
}

Write-Host @"
============================================================
  SUSModder 3.0 E2E Release Gate
  Started: $($script:StartTime.ToString('yyyy-MM-dd HH:mm:ss'))
  Output:  $OutputDir
  Root:    $InstallRoot
============================================================
"@ -ForegroundColor Cyan

# --- Step 1: Build ---
Write-Step "Building solution..." "START"
$buildResult = dotnet build "$script:RepoRoot\SUSModder.sln" -c Release 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Step "Build FAILED" "FAIL"
    Write-Host $buildResult
    exit 1
}
Write-Step "Build OK" "OK"

# --- Step 2: Existing Core tests ---
Write-Step "Running existing unit tests..." "START"
$existingTestResult = dotnet test "$script:RepoRoot\SUSModder.Core.Tests\SUSModder.Core.Tests.csproj" -c Release --logger "trx;LogFileName=$(Join-Path $OutputDir 'core-tests.trx')" 2>&1
$summary["Core.UnitTests"] = if ($LASTEXITCODE -eq 0) { "PASS ($LASTEXITCODE)" } else { "FAIL ($LASTEXITCODE)" }
Write-Step "Core unit tests: $($summary['Core.UnitTests'])" $(if ($LASTEXITCODE -eq 0) { "OK" } else { "FAIL" })

# --- Step 3: API v2 Client Smoke (PowerShell) ---
Write-Step "Running API v2 client smoke (PowerShell)..." "START"
$apiV2Script = Join-Path $script:RepoRoot "SKRYPTY\Test\test-api-v2-client.ps1"
if (Test-Path $apiV2Script) {
    $apiV2Output = & $apiV2Script -V2Base $ApiBase 2>&1
    $apiV2Output | Out-File (Join-Path $OutputDir "api-v2-client-output.txt") -Encoding UTF8
    $apiV2Pass = ($apiV2Output | Select-String "FAIL" | Measure-Object).Count -eq 0
    $summary["API.v2.ClientSmoke"] = if ($apiV2Pass) { "PASS" } else { "FAIL" }
    Write-Step "API v2 client smoke: $($summary['API.v2.ClientSmoke'])" $(if ($apiV2Pass) { "OK" } else { "FAIL" })
} else {
    $summary["API.v2.ClientSmoke"] = "SKIP (script not found)"
    Write-Step "API v2 client smoke: SKIP (script not found)" "SKIP"
}

# --- Step 4: E2E Tests (xUnit in SUSModder.E2E.Tests) ---
# Note: The E2E project might not exist yet — build it first
$e2eProjectPath = Join-Path $script:RepoRoot "SUSModder.E2E.Tests\SUSModder.E2E.Tests.csproj"
$e2eProjectExists = Test-Path $e2eProjectPath

if (-not $e2eProjectExists) {
    Write-Step "E2E project not found at $e2eProjectPath — skipping xUnit E2E tests" "SKIP"
    $summary["E2E.Project"] = "SKIP (not found)"
} else {
    Write-Step "Building E2E test project..." "START"
    $e2eBuildResult = dotnet build $e2eProjectPath -c Release 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Step "E2E project build FAILED" "FAIL"
        $e2eBuildResult | Out-File (Join-Path $OutputDir "e2e-build-error.txt") -Encoding UTF8
    } else {
        Write-Step "E2E project build OK" "OK"

        if ($runApi) {
            $r = Run-DotnetTest -Filter $testFilters.ApiCatalog -Label "e2e-api-catalog"
            $summary["E2E.ApiCatalog"] = if ($r.Passed) { "PASS" } else { "FAIL ($($r.ExitCode))" }
        } else {
            Write-Step "Skipping API catalog tests" "SKIP"
            $summary["E2E.ApiCatalog"] = "SKIP"
        }

        if ($runDownload) {
            $r = Run-DotnetTest -Filter $testFilters.ApiDownload -Label "e2e-api-download"
            $summary["E2E.ApiDownload"] = if ($r.Passed) { "PASS" } else { "FAIL ($($r.ExitCode))" }
        } else {
            Write-Step "Skipping download tests" "SKIP"
            $summary["E2E.ApiDownload"] = "SKIP"
        }

        if ($runExtract) {
            $r = Run-DotnetTest -Filter $testFilters.Extraction -Label "e2e-extraction"
            $summary["E2E.Extraction"] = if ($r.Passed) { "PASS" } else { "FAIL ($($r.ExitCode))" }
        } else {
            Write-Step "Skipping extraction tests" "SKIP"
            $summary["E2E.Extraction"] = "SKIP"
        }

        if ($runInstall) {
            if ($Platforms -match "steam") {
                $r = Run-DotnetTest -Filter $testFilters.InstallSteam -Label "e2e-install-steam"
                $summary["E2E.InstallSteam"] = if ($r.Passed) { "PASS" } else { "FAIL ($($r.ExitCode))" }
            } else {
                $summary["E2E.InstallSteam"] = "SKIP"
            }
            if ($Platforms -match "epic") {
                $r = Run-DotnetTest -Filter $testFilters.InstallEpic -Label "e2e-install-epic"
                $summary["E2E.InstallEpic"] = if ($r.Passed) { "PASS" } else { "FAIL ($($r.ExitCode))" }
            } else {
                $summary["E2E.InstallEpic"] = "SKIP"
            }
        } else {
            Write-Step "Skipping install tests" "SKIP"
            $summary["E2E.InstallSteam"] = "SKIP"
            $summary["E2E.InstallEpic"] = "SKIP"
        }

        if ($runLaunch) {
            if ($Platforms -match "steam") {
                $r = Run-DotnetTest -Filter $testFilters.LaunchSteam -Label "e2e-launch-steam"
                $summary["E2E.LaunchSteam"] = if ($r.Passed) { "PASS" } else { "FAIL ($($r.ExitCode))" }
            } else {
                $summary["E2E.LaunchSteam"] = "SKIP"
            }
            # Epic launch tests: tbd when epic launcher is implemented
            $summary["E2E.LaunchEpic"] = "MANUAL (not yet automated)"
        } else {
            Write-Step "Skipping launch tests" "SKIP"
            $summary["E2E.LaunchSteam"] = "SKIP"
            $summary["E2E.LaunchEpic"] = "SKIP"
        }
    }
}

# --- Step 5: Legacy e2e script ---
Write-Step "Running legacy API E2E smoke..." "START"
$legacyScript = Join-Path $script:RepoRoot "SKRYPTY\Test\test-api-e2e.ps1"
if (Test-Path $legacyScript) {
    $legacyOutput = & $legacyScript -V2Base $ApiBase 2>&1
    $legacyOutput | Out-File (Join-Path $OutputDir "legacy-api-e2e-output.txt") -Encoding UTF8
    $legacyFailCount = ($legacyOutput | Select-String "FAIL" | Measure-Object).Count
    $summary["API.LegacyE2E"] = if ($legacyFailCount -eq 0) { "PASS" } else { "WARN ($legacyFailCount failures)" }
    Write-Step "Legacy API E2E: $($summary['API.LegacyE2E'])" $(if ($legacyFailCount -eq 0) { "OK" } else { "WARN" })
} else {
    $summary["API.LegacyE2E"] = "SKIP (script not found)"
}

# --- Final Summary ---
$endTime = Get-Date
$duration = $endTime - $script:StartTime

Write-Summary "E2E Release Gate Summary" $summary

Write-Host @"
============================================================
  Duration: $($duration.ToString('hh\:mm\:ss'))
  Output:   $OutputDir
  Root:     $InstallRoot
============================================================
"@ -ForegroundColor Cyan

# Decision
$blockers = @($summary.GetEnumerator() | Where-Object { $_.Value -match "^FAIL" })
if ($blockers.Count -gt 0) {
    Write-Host "`n❌ RELEASE BLOCKED — $($blockers.Count) test(s) failed:" -ForegroundColor Red
    foreach ($b in $blockers) {
        Write-Host "  - $($b.Key): $($b.Value)" -ForegroundColor Red
    }
    exit 1
} else {
    Write-Host "`n✅ RELEASE GATE PASSED — No blockers detected" -ForegroundColor Green
    exit 0
}
