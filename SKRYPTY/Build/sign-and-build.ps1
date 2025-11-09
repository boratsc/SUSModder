# Sign and Build Helper Script
# Pomocnik do budowania z podpisywaniem (Certum)

param(
    [Parameter(Mandatory = $false)]
    [string]$ReleaseVersion = "2.2.0",
    
    [Parameter(Mandatory = $false)]
    [string]$NextBetaVersion = "2.3.0-beta",
    
    [switch]$SkipLegacyZip
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build with Certum Code Signing" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Sprawdź czy signtool jest dostępny
$signtool = Get-Command "signtool" -ErrorAction SilentlyContinue
if (-not $signtool) {
    Write-Host "ERROR: signtool.exe not found in PATH" -ForegroundColor Red
    Write-Host ""
    Write-Host "Możliwe rozwiązania:" -ForegroundColor Yellow
    Write-Host "  1. Zainstaluj Windows SDK:" -ForegroundColor Gray
    Write-Host "     https://developer.microsoft.com/windows/downloads/windows-sdk/" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  2. Dodaj signtool do PATH:" -ForegroundColor Gray
    Write-Host "     `$env:Path += ';C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64'" -ForegroundColor Gray
    Write-Host ""
    exit 1
}

Write-Host "✓ signtool found: $($signtool.Source)" -ForegroundColor Green
Write-Host ""

# Lista dostępnych certyfikatów w Windows Store
Write-Host "Checking available certificates in Windows Store..." -ForegroundColor Cyan
Write-Host ""

$certs = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert | 
    Where-Object { $_.NotAfter -gt (Get-Date) } |
    Select-Object Thumbprint, Subject, NotBefore, NotAfter

if ($certs.Count -eq 0) {
    Write-Host "ERROR: No valid code signing certificates found in Windows Store" -ForegroundColor Red
    Write-Host ""
    Write-Host "Sprawdź certyfikaty:" -ForegroundColor Yellow
    Write-Host "  certmgr.msc → Personal → Certificates" -ForegroundColor Gray
    Write-Host ""
    exit 1
}

Write-Host "Available certificates:" -ForegroundColor Green
$certs | Format-Table -AutoSize

# Domyślny thumbprint (Certum)
$defaultThumbprint = "YOUR_CERTIFICATE_THUMBPRINT_HERE"

# Sprawdź czy domyślny certyfikat istnieje
$certExists = $certs | Where-Object { $_.Thumbprint -eq $defaultThumbprint }

if ($certExists) {
    Write-Host "Found Certum certificate (SHA1: [REDACTED])" -ForegroundColor Green
    Write-Host "Subject: $($certExists.Subject)" -ForegroundColor Gray
    Write-Host "Valid until: $($certExists.NotAfter.ToString('yyyy-MM-dd'))" -ForegroundColor Gray
    Write-Host ""
}

# Zapytaj użytkownika o thumbprint
Write-Host "Enter certificate thumbprint to use for signing:" -ForegroundColor Yellow
if ($certExists) {
    Write-Host "[Press ENTER to use default: $defaultThumbprint]" -ForegroundColor Gray
}

$thumbprint = Read-Host "Thumbprint"

# Użyj domyślnego jeśli puste
if ([string]::IsNullOrWhiteSpace($thumbprint)) {
    if ($certExists) {
        $thumbprint = $defaultThumbprint
        Write-Host "Using default Certum certificate" -ForegroundColor Green
    } else {
        Write-Host "ERROR: No thumbprint provided" -ForegroundColor Red
        exit 1
    }
}

# Usuń spacje i ewentualne separatory
$thumbprint = $thumbprint -replace '\s', '' -replace ':', ''

# Walidacja thumbprint (40 znaków hex)
if ($thumbprint -notmatch '^[0-9a-fA-F]{40}$') {
    Write-Host "ERROR: Invalid thumbprint format. Expected 40 hex characters." -ForegroundColor Red
    Write-Host "Provided: $thumbprint" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "Using certificate: $thumbprint" -ForegroundColor Green
Write-Host ""

# Potwierdzenie
Write-Host "Build configuration:" -ForegroundColor Cyan
Write-Host "  Release version: $ReleaseVersion" -ForegroundColor White
Write-Host "  Beta version: $NextBetaVersion" -ForegroundColor White
Write-Host "  Legacy ZIP: $(if($SkipLegacyZip){'No'}else{'Yes'})" -ForegroundColor White
Write-Host "  Code signing: Yes (Certum)" -ForegroundColor White
Write-Host ""

$confirmation = Read-Host "Continue with build? (Y/n)"
if ($confirmation -eq 'n' -or $confirmation -eq 'N') {
    Write-Host "Build cancelled by user" -ForegroundColor Yellow
    exit 0
}

Write-Host ""
Write-Host "Starting build..." -ForegroundColor Green
Write-Host ""

# Uruchom build z podpisywaniem
$buildParams = @{
    ReleaseVersion = $ReleaseVersion
    NextBetaVersion = $NextBetaVersion
    CertificateThumbprint = $thumbprint
}

if ($SkipLegacyZip) {
    $buildParams['SkipLegacyZip'] = $true
}

& "$PSScriptRoot\build-release-2.2.0.ps1" @buildParams

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "  Build completed successfully!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "  Build failed!" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    exit 1
}
