# SUSModder Uninstaller
# Wersja: 1.0
# Dla użytkowników którzy zainstalowali aplikację z ZIP (bez Setup.exe)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "╔══════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║     SUSModder Uninstaller v1.0       ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# 1. Sprawdź czy aplikacja jest uruchomiona
Write-Host "[1/5] Sprawdzanie uruchomionych procesów..." -ForegroundColor Yellow
$processName = "SUSModder"
$runningProcesses = Get-Process -Name $processName -ErrorAction SilentlyContinue

if ($runningProcesses) {
    Write-Host "  Znaleziono uruchomiony proces SUSModder" -ForegroundColor Yellow
    Write-Host "  Zamykanie aplikacji..." -ForegroundColor Yellow
    $runningProcesses | Stop-Process -Force
    Start-Sleep -Seconds 2
    Write-Host "  ✅ Aplikacja zamknięta" -ForegroundColor Green
} else {
    Write-Host "  ✅ Aplikacja nie jest uruchomiona" -ForegroundColor Green
}
Write-Host ""

# 1.5. Usuń wpis z rejestru Windows
Write-Host "[1.5/6] Usuwanie wpisu z rejestru Windows..." -ForegroundColor Yellow
try {
    $registryPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SUSModder"
    if (Test-Path $registryPath) {
        Remove-Item -Path $registryPath -Recurse -Force
        Write-Host "  ✅ Wpis usunięty z rejestru" -ForegroundColor Green
    } else {
        Write-Host "  ℹ️ Brak wpisu w rejestrze" -ForegroundColor Gray
    }
} catch {
    Write-Host "  ⚠️ Nie udało się usunąć wpisu z rejestru: $($_.Exception.Message)" -ForegroundColor Yellow
}
Write-Host ""

# 2. Określ katalog aplikacji
$appDir = Split-Path -Parent $PSCommandPath
Write-Host "[2/6] Katalog aplikacji: $appDir" -ForegroundColor Yellow
Write-Host ""

# 3. Zapytaj o usunięcie plików aplikacji
Write-Host "[3/6] Pliki aplikacji" -ForegroundColor Yellow
$response = Read-Host "  Czy usunąć pliki aplikacji z '$appDir'? (T/N)"

if ($response -eq "T" -or $response -eq "t" -or $response -eq "Y" -or $response -eq "y") {
    Write-Host "  Usuwanie plików aplikacji..." -ForegroundColor Yellow
    
    # Usuń wszystko oprócz tego skryptu
    Get-ChildItem -Path $appDir -Exclude "uninstall.ps1" | ForEach-Object {
        Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
    
    Write-Host "  ✅ Pliki aplikacji usunięte" -ForegroundColor Green
    $appFilesDeleted = $true
} else {
    Write-Host "  ⏭️ Pliki aplikacji zachowane" -ForegroundColor Yellow
    $appFilesDeleted = $false
}
Write-Host ""

# 4. Zapytaj o dane użytkownika (zainstalowane mody)
Write-Host "[4/6] Dane użytkownika" -ForegroundColor Yellow

# Sprawdź różne możliwe lokalizacje modów
$possibleModPaths = @(
    "$env:APPDATA\Among Us - Mody",
    "$env:LOCALAPPDATA\Among Us - Mody"
)

$foundModPaths = $possibleModPaths | Where-Object { Test-Path $_ }

if ($foundModPaths.Count -gt 0) {
    Write-Host "  Znaleziono zainstalowane mody:" -ForegroundColor Yellow
    
    foreach ($path in $foundModPaths) {
        Write-Host "    - $path" -ForegroundColor Gray
        
        # Policz rozmiar
        $size = (Get-ChildItem -Path $path -Recurse -ErrorAction SilentlyContinue | 
                 Measure-Object -Property Length -Sum).Sum / 1MB
        Write-Host "      Rozmiar: $([Math]::Round($size, 2)) MB" -ForegroundColor Gray
    }
    
    Write-Host ""
    $response = Read-Host "  Czy usunąć wszystkie zainstalowane mody? (T/N)"
    
    if ($response -eq "T" -or $response -eq "t" -or $response -eq "Y" -or $response -eq "y") {
        Write-Host "  Usuwanie danych użytkownika..." -ForegroundColor Yellow
        
        foreach ($path in $foundModPaths) {
            Remove-Item $path -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "    ✅ Usunięto: $path" -ForegroundColor Green
        }
    } else {
        Write-Host "  ⏭️ Dane użytkownika zachowane" -ForegroundColor Yellow
    }
} else {
    Write-Host "  ℹ️ Nie znaleziono zainstalowanych modów" -ForegroundColor Gray
}
Write-Host ""

# 5. Usuń skróty
Write-Host "[5/6] Czyszczenie skrótów..." -ForegroundColor Yellow

$shortcuts = @(
    "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\SUSModder.lnk",
    "$env:USERPROFILE\Desktop\SUSModder.lnk",
    "$env:PUBLIC\Desktop\SUSModder.lnk"
)

$removedCount = 0
foreach ($shortcut in $shortcuts) {
    if (Test-Path $shortcut) {
        Remove-Item $shortcut -Force -ErrorAction SilentlyContinue
        $removedCount++
    }
}

if ($removedCount -gt 0) {
    Write-Host "  ✅ Usunięto $removedCount skrótów" -ForegroundColor Green
} else {
    Write-Host "  ℹ️ Nie znaleziono skrótów" -ForegroundColor Gray
}
Write-Host ""

# 6. Podsumowanie
Write-Host "╔══════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║   ✅ Deinstalacja zakończona         ║" -ForegroundColor Green
Write-Host "╚══════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""

if ($appFilesDeleted) {
    Write-Host "❗ UWAGA: Ten skrypt także zostanie usunięty po zamknięciu." -ForegroundColor Yellow
    Write-Host ""
    
    # Zamknij skrypt i usuń katalog aplikacji
    Write-Host "Naciśnij dowolny klawisz aby zakończyć..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    
    # Usuń cały katalog aplikacji (w tym ten skrypt) w osobnym procesie
    Start-Process powershell -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "Start-Sleep -Seconds 2; Remove-Item -Path '$appDir' -Recurse -Force -ErrorAction SilentlyContinue" -WindowStyle Hidden
} else {
    Write-Host "Pliki aplikacji zachowane w: $appDir" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Naciśnij dowolny klawisz aby zamknąć..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
}
