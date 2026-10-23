#Requires -Version 5.1
# Offline checks: never run the real CLI or create/install/publish a package.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('SUSModder-vpk-tests-' + [guid]::NewGuid().ToString('N'))
$calls = [Collections.Generic.List[object]]::new()
$state = @{ FailRestore = $false; FailRun = $false }

function dotnet {
    $calls.Add(@($args))
    $global:LASTEXITCODE = 0
    if (($args[1] -eq 'restore' -and $state.FailRestore) -or
        ($args[1] -eq 'run' -and $state.FailRun)) {
        $global:LASTEXITCODE = 1
    }
}

function Assert-True($Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
}

function Assert-Fails([scriptblock]$Action, [string]$Expected) {
    $failure = $null
    try { & $Action } catch { $failure = $_ }
    Assert-True ($null -ne $failure) 'Expected failure'
    Assert-True ($failure.ToString().Contains($Expected)) "Expected error: $Expected"
}

try {
    foreach ($directory in @('SKRYPTY/Build', '.config', 'SUSModder', 'SUSModder.Core')) {
        New-Item -ItemType Directory -Path (Join-Path $tempRoot $directory) -Force | Out-Null
    }
    $helper = Join-Path $tempRoot 'SKRYPTY/Build/Invoke-Velopack.ps1'
    Copy-Item (Join-Path $root 'SKRYPTY/Build/Invoke-Velopack.ps1') $helper
    foreach ($file in @('.config/dotnet-tools.json', 'SUSModder/SUSModder.csproj', 'SUSModder.Core/SUSModder.Core.csproj')) {
        Copy-Item (Join-Path $root $file) (Join-Path $tempRoot $file)
    }

    $originalLocation = (Get-Location).Path
    & $helper -RestoreOnly
    Assert-True ($calls.Count -eq 1 -and $calls[0][1] -eq 'restore') 'RestoreOnly must not run vpk'
    Assert-True ((Get-Location).Path -eq $originalLocation) 'Restore must preserve location'

    $calls.Clear()
    & $helper -Arguments @('pack', '--packTitle', 'Title with spaces')
    Assert-True ($calls.Count -eq 2) 'Restore before invocation'
    # PowerShell consumes the -- end-of-parameters token when calling a function
    # mock, whereas the native dotnet process receives it.
    Assert-True (($calls[1] -join '|') -eq 'tool|run|vpk|--skip-updates|pack|--packTitle|Title with spaces') 'Forward arguments to local tool only'
    Assert-True ((Get-Location).Path -eq $originalLocation) 'Invocation must preserve location'

    $calls.Clear()
    $state.FailRestore = $true
    Assert-Fails { & $helper -Arguments @('pack') } 'Failed to restore'
    Assert-True ($calls.Count -eq 1) 'Restore failure must prevent packaging'
    Assert-True ((Get-Location).Path -eq $originalLocation) 'Restore failure must preserve location'
    $state.FailRestore = $false

    $state.FailRun = $true
    Assert-Fails { & $helper -Arguments @('pack') } 'Velopack failed'
    Assert-True ((Get-Location).Path -eq $originalLocation) 'CLI failure must preserve location'
    $state.FailRun = $false

    $calls.Clear()
    $corePath = Join-Path $tempRoot 'SUSModder.Core/SUSModder.Core.csproj'
    $core = [IO.File]::ReadAllText($corePath)
    $manifest = Get-Content (Join-Path $tempRoot '.config/dotnet-tools.json') -Raw | ConvertFrom-Json
    $reference = 'Include="Velopack" Version="{0}"' -f $manifest.tools.vpk.version
    [IO.File]::WriteAllText($corePath, $core.Replace($reference, 'Include="Velopack" Version="0.0.1298"'))
    Assert-Fails { & $helper -RestoreOnly } 'Velopack version mismatch'
    Assert-True ($calls.Count -eq 0) 'Mismatch must fail before restoring/running tools'

    Write-Host "PASS: pinned Velopack toolchain (PowerShell $($PSVersionTable.PSVersion)); no CLI/network calls."
}
finally {
    if (Test-Path $tempRoot) { Remove-Item $tempRoot -Recurse -Force }
}
