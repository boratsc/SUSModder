# Deploy Script - Upload Release Files to Server
# Automatyczne wgrywanie plików po build na serwer produkcyjny

param(
    [Parameter(Mandatory = $false)]
    [string]$ReleaseVersion = "2.2.0",
    
    [Parameter(Mandatory = $false)]
    [string]$Server = "vps-b99a39c3.vps.ovh.net",
    
    [Parameter(Mandatory = $false)]
    [string]$Username = "debian",
    
    [switch]$SkipLegacy,
    [switch]$SkipRelease,
    [switch]$SkipBeta,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  SUSModder Deployment Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ============================================================================
# SPRAWDZENIE WYMAGAŃ
# ============================================================================

Write-Host "[1/6] Checking requirements..." -ForegroundColor Green

# Sprawdź czy są zainstalowane narzędzia SSH/SCP
$pscpPath = Get-Command "pscp" -ErrorAction SilentlyContinue
$plinkPath = Get-Command "plink" -ErrorAction SilentlyContinue

if (-not $pscpPath -or -not $plinkPath) {
    Write-Host "  ERROR: PuTTY tools not found (pscp.exe, plink.exe)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Instalacja PuTTY (zawiera pscp i plink):" -ForegroundColor Yellow
    Write-Host "  1. Przez winget:" -ForegroundColor Gray
    Write-Host "     winget install PuTTY.PuTTY" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  2. Przez Chocolatey:" -ForegroundColor Gray
    Write-Host "     choco install putty" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  3. Ręcznie:" -ForegroundColor Gray
    Write-Host "     https://www.putty.org/" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Po instalacji dodaj do PATH:" -ForegroundColor Yellow
    Write-Host "  `$env:Path += ';C:\Program Files\PuTTY'" -ForegroundColor Gray
    Write-Host ""
    exit 1
}

Write-Host "  OK: pscp found: $($pscpPath.Source)" -ForegroundColor Green
Write-Host "  OK: plink found: $($plinkPath.Source)" -ForegroundColor Green
Write-Host ""

# ============================================================================
# SPRAWDZENIE KATALOGÓW
# ============================================================================

Write-Host "[2/6] Checking release directories..." -ForegroundColor Green

$legacyDir = Join-Path $ProjectRoot "releases-legacy"
$releaseDir = Join-Path $ProjectRoot "releases-release"
$betaDir = Join-Path $ProjectRoot "releases-beta"

$dirsToCheck = @()
if (-not $SkipLegacy) { $dirsToCheck += @{Name="Legacy"; Path=$legacyDir} }
if (-not $SkipRelease) { $dirsToCheck += @{Name="Release"; Path=$releaseDir} }
if (-not $SkipBeta) { $dirsToCheck += @{Name="Beta"; Path=$betaDir} }

$missingDirs = @()
foreach ($dir in $dirsToCheck) {
    if (-not (Test-Path $dir.Path)) {
        $missingDirs += $dir.Name
        Write-Host "  WARNING: $($dir.Name) directory not found: $($dir.Path)" -ForegroundColor Yellow
    } else {
        $fileCount = (Get-ChildItem $dir.Path -File).Count
        Write-Host "  OK: $($dir.Name) directory found ($fileCount files)" -ForegroundColor Green
    }
}

if ($missingDirs.Count -gt 0) {
    Write-Host ""
    Write-Host "ERROR: Missing directories: $($missingDirs -join ', ')" -ForegroundColor Red
    Write-Host "Uruchom build najpierw:" -ForegroundColor Yellow
    Write-Host "  .\SKRYPTY\Build\build-release-2.2.0.ps1 -ReleaseVersion `"$ReleaseVersion`"" -ForegroundColor Gray
    Write-Host ""
    exit 1
}

Write-Host ""

# ============================================================================
# ZAPYTAJ O HASŁO
# ============================================================================

Write-Host "[3/6] Authentication..." -ForegroundColor Green
Write-Host "Server: $Username@$Server" -ForegroundColor Gray
Write-Host ""

if ($DryRun) {
    Write-Host "  DRY RUN: Skipping authentication" -ForegroundColor Yellow
    $password = "dummy"
} else {
    $securePassword = Read-Host "Enter SSH password for $Username@$Server" -AsSecureString
    $BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
    $password = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)
    
    if ([string]::IsNullOrWhiteSpace($password)) {
        Write-Host "  ERROR: No password provided" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "  OK: Password received" -ForegroundColor Green
}

Write-Host ""

# ============================================================================
# TEST POŁĄCZENIA
# ============================================================================

Write-Host "[4/6] Testing SSH connection..." -ForegroundColor Green

if (-not $DryRun) {
    # Test połączenia przez plink
    $testCommand = "echo 'Connection successful'"
    $plinkArgs = @(
        "-batch"
        "-pw", $password
        "$Username@$Server"
        $testCommand
    )
    
    try {
        $result = & plink @plinkArgs 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  OK: SSH connection successful" -ForegroundColor Green
        } else {
            Write-Host "  ERROR: SSH connection failed" -ForegroundColor Red
            Write-Host "  Output: $result" -ForegroundColor Yellow
            exit 1
        }
    } catch {
        Write-Host "  ERROR: Failed to connect: $_" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "  DRY RUN: Skipping connection test" -ForegroundColor Yellow
}

Write-Host ""

# ============================================================================
# PODSUMOWANIE I POTWIERDZENIE
# ============================================================================

Write-Host "[5/6] Deployment plan:" -ForegroundColor Green
Write-Host ""

$serverPaths = @{
    VelopackManifests = "/srv/synapsekit-boracik/nginx/html/susmodder-velopack"
    Releases = "/srv/synapsekit-boracik/nginx/html/susmodder/releases"
    Versions = "/srv/synapsekit-boracik/nginx/html/susmodder-versions"
}

if (-not $SkipLegacy) {
    Write-Host "  Legacy ZIP:" -ForegroundColor Cyan
    Write-Host "    Local:  $legacyDir" -ForegroundColor Gray
    Write-Host "    Remote: $($serverPaths.Releases)/legacy/" -ForegroundColor Gray
    Write-Host "            $($serverPaths.Versions)/SUSModder-$ReleaseVersion.zip" -ForegroundColor Gray
    $legacyFiles = Get-ChildItem $legacyDir -File
    foreach ($file in $legacyFiles) {
        $size = "{0:N2} MB" -f ($file.Length / 1MB)
        Write-Host "      - $($file.Name) ($size)" -ForegroundColor White
        Write-Host "        → SUSModder-$ReleaseVersion.zip (versions)" -ForegroundColor Gray
    }
    Write-Host ""
}

if (-not $SkipRelease) {
    Write-Host "  Release channel:" -ForegroundColor Cyan
    Write-Host "    Local:  $releaseDir" -ForegroundColor Gray
    Write-Host "    Remote: $($serverPaths.Releases)/release/" -ForegroundColor Gray
    Write-Host "            $($serverPaths.VelopackManifests)/releases.release.json" -ForegroundColor Gray
    $releaseFiles = Get-ChildItem $releaseDir -File
    foreach ($file in $releaseFiles) {
        $size = if ($file.Length -gt 1MB) { "{0:N2} MB" -f ($file.Length / 1MB) } else { "{0:N2} KB" -f ($file.Length / 1KB) }
        Write-Host "      - $($file.Name) ($size)" -ForegroundColor White
    }
    Write-Host ""
}

if (-not $SkipBeta) {
    Write-Host "  Beta channel:" -ForegroundColor Cyan
    Write-Host "    Local:  $betaDir" -ForegroundColor Gray
    Write-Host "    Remote: $($serverPaths.Releases)/beta/" -ForegroundColor Gray
    Write-Host "            $($serverPaths.VelopackManifests)/releases.beta.json" -ForegroundColor Gray
    $betaFiles = Get-ChildItem $betaDir -File
    foreach ($file in $betaFiles) {
        $size = if ($file.Length -gt 1MB) { "{0:N2} MB" -f ($file.Length / 1MB) } else { "{0:N2} KB" -f ($file.Length / 1KB) }
        Write-Host "      - $($file.Name) ($size)" -ForegroundColor White
    }
    Write-Host ""
}

$totalSize = 0
if (-not $SkipLegacy) { $totalSize += (Get-ChildItem $legacyDir -File | Measure-Object -Property Length -Sum).Sum }
if (-not $SkipRelease) { $totalSize += (Get-ChildItem $releaseDir -File | Measure-Object -Property Length -Sum).Sum }
if (-not $SkipBeta) { $totalSize += (Get-ChildItem $betaDir -File | Measure-Object -Property Length -Sum).Sum }

Write-Host "Total upload size: $([Math]::Round($totalSize / 1MB, 2)) MB" -ForegroundColor Yellow
Write-Host ""

if ($DryRun) {
    Write-Host "DRY RUN MODE - No files will be uploaded" -ForegroundColor Yellow
    Write-Host ""
} else {
    $confirmation = Read-Host "Continue with upload? (Y/n)"
    if ($confirmation -eq 'n' -or $confirmation -eq 'N') {
        Write-Host "Deployment cancelled by user" -ForegroundColor Yellow
        exit 0
    }
}

Write-Host ""

# ============================================================================
# UPLOAD PLIKÓW
# ============================================================================

Write-Host "[6/6] Uploading files..." -ForegroundColor Green
Write-Host ""

function Upload-Files {
    param(
        [string]$LocalPath,
        [string]$RemotePath,
        [string]$ChannelName
    )
    
    Write-Host "  Uploading $ChannelName..." -ForegroundColor Cyan
    
    if ($DryRun) {
        Write-Host "    DRY RUN: Would upload files from $LocalPath to $RemotePath" -ForegroundColor Yellow
        return $true
    }
    
    # Utwórz katalog na serwerze (jeśli nie istnieje)
    $mkdirCommand = "mkdir -p $RemotePath"
    $plinkArgs = @(
        "-batch"
        "-pw", $password
        "$Username@$Server"
        $mkdirCommand
    )
    
    & plink @plinkArgs 2>&1 | Out-Null
    
    # Upload wszystkich plików z katalogu
    $files = Get-ChildItem $LocalPath -File
    $current = 0
    $total = $files.Count
    
    foreach ($file in $files) {
        $current++
        $percent = [Math]::Round(($current / $total) * 100)
        Write-Host "    [$current/$total] Uploading $($file.Name)... " -NoNewline -ForegroundColor Gray
        
        $pscpArgs = @(
            "-batch"
            "-pw", $password
            $file.FullName
            "${Username}@${Server}:${RemotePath}/"
        )
        
        $output = & pscp @pscpArgs 2>&1
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "OK ($percent%)" -ForegroundColor Green
        } else {
            Write-Host "FAILED" -ForegroundColor Red
            Write-Host "    Error: $output" -ForegroundColor Yellow
            return $false
        }
    }
    
    return $true
}

function Upload-ManifestFile {
    param(
        [string]$LocalFile,
        [string]$RemotePath,
        [string]$FileName
    )
    
    Write-Host "  Uploading $FileName to Velopack manifests..." -ForegroundColor Cyan
    
    if ($DryRun) {
        Write-Host "    DRY RUN: Would upload $LocalFile to $RemotePath" -ForegroundColor Yellow
        return $true
    }
    
    # Utwórz katalog na serwerze (jeśli nie istnieje)
    $mkdirCommand = "mkdir -p $RemotePath"
    $plinkArgs = @(
        "-batch"
        "-pw", $password
        "$Username@$Server"
        $mkdirCommand
    )
    
    & plink @plinkArgs 2>&1 | Out-Null
    
    Write-Host "    Uploading $FileName... " -NoNewline -ForegroundColor Gray
    
    $pscpArgs = @(
        "-batch"
        "-pw", $password
        $LocalFile
        "${Username}@${Server}:${RemotePath}/${FileName}"
    )
    
    $output = & pscp @pscpArgs 2>&1
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "OK" -ForegroundColor Green
        return $true
    } else {
        Write-Host "FAILED" -ForegroundColor Red
        Write-Host "    Error: $output" -ForegroundColor Yellow
        return $false
    }
}

$uploadSuccess = $true

# Upload Legacy
if (-not $SkipLegacy) {
    # Upload do /releases/legacy/
    if (-not (Upload-Files -LocalPath $legacyDir -RemotePath "$($serverPaths.Releases)/legacy" -ChannelName "Legacy ZIP (releases)")) {
        $uploadSuccess = $false
    }
    
    # Upload do /susmodder-versions/ z odpowiednią nazwą
    Write-Host "  Uploading to susmodder-versions..." -ForegroundColor Cyan
    
    $legacyZip = Get-ChildItem $legacyDir -Filter "*.zip" | Select-Object -First 1
    if ($legacyZip) {
        $versionedName = "SUSModder-$ReleaseVersion.zip"
        
        if ($DryRun) {
            Write-Host "    DRY RUN: Would upload $($legacyZip.Name) as $versionedName" -ForegroundColor Yellow
        } else {
            # Utwórz katalog na serwerze
            $mkdirCommand = "mkdir -p $($serverPaths.Versions)"
            $plinkArgs = @(
                "-batch"
                "-pw", $password
                "$Username@$Server"
                $mkdirCommand
            )
            & plink @plinkArgs 2>&1 | Out-Null
            
            Write-Host "    Uploading $versionedName... " -NoNewline -ForegroundColor Gray
            
            # Upload z nową nazwą
            $pscpArgs = @(
                "-batch"
                "-pw", $password
                $legacyZip.FullName
                "${Username}@${Server}:$($serverPaths.Versions)/$versionedName"
            )
            
            $output = & pscp @pscpArgs 2>&1
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host "OK" -ForegroundColor Green
            } else {
                Write-Host "FAILED" -ForegroundColor Red
                Write-Host "    Error: $output" -ForegroundColor Yellow
                $uploadSuccess = $false
            }
        }
    } else {
        Write-Host "    WARNING: No legacy ZIP found in $legacyDir" -ForegroundColor Yellow
    }
    
    Write-Host ""
}

# Upload Release
if (-not $SkipRelease) {
    if (-not (Upload-Files -LocalPath $releaseDir -RemotePath "$($serverPaths.Releases)/release" -ChannelName "Release channel")) {
        $uploadSuccess = $false
    }
    
    # Upload releases.release.json do katalogu manifestów
    $releaseManifest = Join-Path $releaseDir "releases.release.json"
    if (Test-Path $releaseManifest) {
        if (-not (Upload-ManifestFile -LocalFile $releaseManifest -RemotePath $serverPaths.VelopackManifests -FileName "releases.release.json")) {
            $uploadSuccess = $false
        }
    }
    Write-Host ""
}

# Upload Beta
if (-not $SkipBeta) {
    if (-not (Upload-Files -LocalPath $betaDir -RemotePath "$($serverPaths.Releases)/beta" -ChannelName "Beta channel")) {
        $uploadSuccess = $false
    }
    
    # Upload releases.beta.json do katalogu manifestów
    $betaManifest = Join-Path $betaDir "releases.beta.json"
    if (Test-Path $betaManifest) {
        if (-not (Upload-ManifestFile -LocalFile $betaManifest -RemotePath $serverPaths.VelopackManifests -FileName "releases.beta.json")) {
            $uploadSuccess = $false
        }
    }
    Write-Host ""
}

# ============================================================================
# WERYFIKACJA PO UPLOAD
# ============================================================================

if (-not $DryRun -and $uploadSuccess) {
    Write-Host "Verifying upload..." -ForegroundColor Green
    Write-Host ""
    
    # Sprawdź czy pliki istnieją na serwerze
    $verifyCommands = @()
    
    if (-not $SkipLegacy) {
        $verifyCommands += "ls -lh $($serverPaths.Releases)/legacy/ | tail -3"
        $verifyCommands += "ls -lh $($serverPaths.Versions)/SUSModder-$ReleaseVersion.zip"
    }
    
    if (-not $SkipRelease) {
        $verifyCommands += "ls -lh $($serverPaths.Releases)/release/ | tail -5"
        $verifyCommands += "ls -lh $($serverPaths.VelopackManifests)/releases.release.json"
    }
    
    if (-not $SkipBeta) {
        $verifyCommands += "ls -lh $($serverPaths.Releases)/beta/ | tail -5"
        $verifyCommands += "ls -lh $($serverPaths.VelopackManifests)/releases.beta.json"
    }
    
    foreach ($cmd in $verifyCommands) {
        $plinkArgs = @(
            "-batch"
            "-pw", $password
            "$Username@$Server"
            $cmd
        )
        
        $result = & plink @plinkArgs 2>&1
        Write-Host $result -ForegroundColor Gray
    }
    Write-Host ""
}

# ============================================================================
# PODSUMOWANIE
# ============================================================================

if ($uploadSuccess) {
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "  DEPLOYMENT SUCCESSFUL" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    
    if (-not $DryRun) {
        Write-Host "Files uploaded to:" -ForegroundColor Cyan
        if (-not $SkipLegacy) {
            Write-Host "  https://susmodder.app/releases/legacy/" -ForegroundColor White
            Write-Host "  https://susmodder.boracik.pl/SUSModder-$ReleaseVersion.zip" -ForegroundColor White
        }
        if (-not $SkipRelease) {
            Write-Host "  https://susmodder.app/releases/release/" -ForegroundColor White
            Write-Host "  https://susmodder.app/api/releases?channel=release" -ForegroundColor White
        }
        if (-not $SkipBeta) {
            Write-Host "  https://susmodder.app/releases/beta/" -ForegroundColor White
            Write-Host "  https://susmodder.app/api/releases?channel=beta" -ForegroundColor White
        }
        Write-Host ""
        
        Write-Host "Next steps:" -ForegroundColor Yellow
        Write-Host "  1. Test legacy download: curl -I https://susmodder.boracik.pl/SUSModder-$ReleaseVersion.zip" -ForegroundColor Gray
        Write-Host "  2. Test Velopack API: curl https://susmodder.app/api/releases?channel=release" -ForegroundColor Gray
        Write-Host "  3. Test update in application (v2.0.2 → v$ReleaseVersion)" -ForegroundColor Gray
        Write-Host "  4. Monitor logs: ssh $Username@$Server 'tail -f /var/log/nginx/access.log'" -ForegroundColor Gray
    } else {
        Write-Host "DRY RUN completed - no actual upload performed" -ForegroundColor Yellow
    }
} else {
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "  DEPLOYMENT FAILED" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "Some files failed to upload. Check errors above." -ForegroundColor Yellow
    exit 1
}

Write-Host ""
