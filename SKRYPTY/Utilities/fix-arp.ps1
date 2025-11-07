param(
    [string]$Version = "",
    [string]$DisplayName = "SUSModder",
    [string]$Publisher = "SUSModder Team",
    [switch]$AllUsers
)
$ErrorActionPreference = 'Stop'

function Get-InstallRoot {
    $userPath = Join-Path $env:LOCALAPPDATA 'SUSModder'
    $machinePath = Join-Path ${env:ProgramFiles} 'SUSModder'
    if (Test-Path $userPath) { return $userPath }
    if (Test-Path $machinePath) { return $machinePath }
    return $userPath  # default per-user
}

$root = Get-InstallRoot
$updateExe = Join-Path $root 'Update.exe'
$currentExe = Join-Path $root 'current\\SUSModder.exe'

if (-not (Test-Path $updateExe)) {
    Write-Warning "Nie znaleziono Update.exe w: $updateExe. Kontynuuję tworzenie wpisu, ale odinstalowanie może się nie powieść."
}

$uninstallRoot = if ($AllUsers) { 'HKLM:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall' } else { 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall' }
$uninstallKey = Join-Path $uninstallRoot 'SUSModder'
if (-not (Test-Path $uninstallKey)) {
    New-Item -Path $uninstallKey -Force | Out-Null
}

$displayVersion = if ([string]::IsNullOrWhiteSpace($Version)) { (Get-Date).ToString('0.0.0') } else { $Version }
$installDate = (Get-Date).ToString('yyyyMMdd')

# Best-practice ARP values
New-ItemProperty -Path $uninstallKey -Name 'DisplayName' -Value $DisplayName -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'DisplayVersion' -Value $displayVersion -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'Publisher' -Value $Publisher -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'InstallLocation' -Value $root -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'InstallDate' -Value $installDate -PropertyType String -Force | Out-Null
$uninstallCmd = ('"{0}" --uninstall' -f $updateExe)
New-ItemProperty -Path $uninstallKey -Name 'UninstallString' -Value $uninstallCmd -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'DisplayIcon' -Value $currentExe -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'NoModify' -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'NoRepair' -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'WindowsInstaller' -Value 0 -PropertyType DWord -Force | Out-Null

# Optional size for Settings UI (KB)
try {
    $sizeKB = [int](((Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum) / 1KB)
    if ($sizeKB -gt 0) { New-ItemProperty -Path $uninstallKey -Name 'EstimatedSize' -Value $sizeKB -PropertyType DWord -Force | Out-Null }
} catch {}

Write-Host "✅ Wpis odinstalowania utworzony/naprawiony w $uninstallRoot. Sprawdź w Ustawienia → Aplikacje lub appwiz.cpl." -ForegroundColor Green
