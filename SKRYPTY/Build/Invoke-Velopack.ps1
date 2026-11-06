# Always use the repository-pinned CLI, never an unrelated global installation.
param(
    [string[]]$Arguments = @(),
    [switch]$RestoreOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$manifestPath = Join-Path $projectRoot '.config/dotnet-tools.json'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$cliVersion = $manifest.tools.vpk.version

foreach ($relativePath in @('SUSModder/SUSModder.csproj', 'SUSModder.Core/SUSModder.Core.csproj')) {
    [xml]$project = Get-Content (Join-Path $projectRoot $relativePath) -Raw
    $reference = @($project.Project.ItemGroup.PackageReference) |
        Where-Object { $_.Include -eq 'Velopack' }
    if ($reference.Version -ne $cliVersion) {
        throw "Velopack version mismatch: $relativePath must match CLI $cliVersion."
    }
}

Push-Location $projectRoot
try {
    & dotnet tool restore --tool-manifest $manifestPath
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to restore the pinned Velopack CLI.'
    }

    if (-not $RestoreOnly) {
        & dotnet tool run vpk -- --skip-updates @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Velopack failed (exit $LASTEXITCODE)."
        }
    }
}
finally {
    Pop-Location
}
