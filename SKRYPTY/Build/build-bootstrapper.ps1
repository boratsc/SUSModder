param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$SolutionPath = Join-Path $ProjectRoot "SUSModder.Bootstrapper.sln"

if (-not (Test-Path $SolutionPath)) {
    throw "Nie znaleziono solucji bootstrappera: $SolutionPath"
}

function Find-MSBuild {
    $vswherePath = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswherePath) {
        $msbuildPath = & $vswherePath -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($msbuildPath) {
            return $msbuildPath
        }
    }

    $knownPaths = @(
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    )

    return $knownPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
}

$msbuild = Find-MSBuild
if (-not $msbuild) {
    throw "Nie znaleziono MSBuild z narzedziami C++. Zainstaluj Visual Studio lub Build Tools z komponentem Desktop development with C++."
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  SUSModder Bootstrapper Builder" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "MSBuild: $msbuild" -ForegroundColor Yellow
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host ""

& $msbuild $SolutionPath /t:Build /p:Configuration=$Configuration /p:Platform=x64 /m

if ($LASTEXITCODE -ne 0) {
    throw "Build bootstrappera nie powiodl sie."
}

Write-Host ""
Write-Host "Bootstrapper zbudowany pomyslnie." -ForegroundColor Green
