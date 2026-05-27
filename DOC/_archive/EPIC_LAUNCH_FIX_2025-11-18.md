# Fix: Epic Launch - Infinite Reinstallation Loop

**Data**: 2025-11-18  
**Wersja**: 2.3.14-beta (planowana)  
**Problem**: Użytkownicy raportują nieskończoną pętlę reinstalacji przy uruchamianiu modów Epic

## Problem

### Symptomy:
1. Legendary instaluje grę pomyślnie
2. Próbuje uruchomić grę
3. Natychmiast odinstalowuje i instaluje ponownie
4. Powtarza w nieskończoność (nawet 10+ razy)
5. Gra nigdy się nie uruchamia

### Przykładowy log:
```
[cli] INFO: Finished installation process in 12.90 seconds.
[cli] INFO: Launching 963137e4c29d4c79a81323b8fab03a40...
[cli] INFO: Removing "Among Us" from "C:\Program Files\Epic Games\AmongUs"...
[cli] INFO: Game has been uninstalled.
[Core] INFO: Downloading latest manifest for "963137e4c29d4c79a81323b8fab03a40"
[cli] ERROR: No files belonging to Game "Among Us" found...
[cli] INFO: Did you mean "C:\Users\User\AppData\Roaming\Among Us - Mody\Town of Us\AmongUs"?
[... loop continues ...]
```

### Root Cause:
1. **Brak limitu prób** - kod próbował reinstalacji w nieskończoność
2. **Brak weryfikacji plików** - kod nie sprawdzał czy pliki faktycznie istnieją przed importem/launchem
3. **Race condition** - Legendary kończy instalację, ale pliki nie są tam gdzie kod oczekuje

## Rozwiązanie

### 1. Dodano limit prób reinstalacji (Max 2 próby)

**Przed:**
```csharp
private bool _isRetryingInstallation = false;

if (_hasLaunchError && !_isRetryingInstallation)
{
    // Próba bez limitu - INFINITE LOOP!
    await PerformReinstallationSequence(modConfig);
}
```

**Po:**
```csharp
private int _retryCount = 0;
private const int MaxRetryAttempts = 2;

if (_hasLaunchError && _retryCount < MaxRetryAttempts)
{
    Write($"Wykryto błąd uruchamiania - próba reinstalacji ({_retryCount + 1}/{MaxRetryAttempts})...");
    _retryCount++;
    await PerformReinstallationSequence(modConfig);
}
else if (_retryCount >= MaxRetryAttempts)
{
    Write($"Przekroczono limit prób ({MaxRetryAttempts}). Zgłaszanie błędu...");
    // Pokaż dialog błędu użytkownikowi
    EpicLaunchError?.Invoke(modConfig.ModName, logContent);
}
```

### 2. Dodano weryfikację plików przed importem

**Problem:** Kod próbował import gry z folderu, gdzie plików już nie ma (zostały przeniesione/usunięte).

**Rozwiązanie:**
```csharp
// Przed importem - sprawdź czy Among Us.exe istnieje
string gameExePath = Path.Combine(installDirectory, "Among Us.exe");
if (!File.Exists(gameExePath))
{
    Write($"OSTRZEŻENIE: Plik gry nie istnieje w {installDirectory}");
    Write("Wymuszanie pełnej reinstalacji zamiast importu...");
    
    // Pomiń import, od razu reinstaluj
    _retryCount++;
    await PerformReinstallationSequence(modConfig);
    return;
}
```

### 3. Dodano weryfikację po instalacji

**Problem:** Legendary zgłaszał sukces instalacji, ale pliki nie były tam gdzie powinny.

**Rozwiązanie:**
```csharp
public async Task InstallGameAsync(ModConfiguration modConfig, string amongVersionFormatted)
{
    // ... instalacja ...
    
    // NOWE: Weryfikacja po instalacji
    string gameExePath = Path.Combine(installDirectory, "AmongUs", "Among Us.exe");
    if (!File.Exists(gameExePath))
    {
        Write($"OSTRZEŻENIE: Plik gry nie został znaleziony po instalacji: {gameExePath}");
        Write($"Instalacja mogła się nie powieść. Sprawdź logi legendary.");
    }
    else
    {
        Write($"Weryfikacja instalacji OK: {gameExePath}");
    }
}
```

### 4. Ulepszone logowanie

**Dodano szczegółowe komunikaty:**
- Numer próby reinstalacji (`próba 1/2`)
- Dokładną ścieżkę sprawdzanych plików
- Czy weryfikacja plików się powiodła
- Powód przerwania retry loop

## Testing

### Test Case 1: Normalny flow (happy path)
1. Użytkownik uruchamia mod Epic
2. Import się powiedzie, launch OK
3. Gra się uruchamia ✅

### Test Case 2: Pierwsza próba fail, retry sukces
1. Użytkownik uruchamia mod Epic
2. Import fail (pliki nie istnieją)
3. Kod wykrywa problem, robi reinstalację (próba 1/2)
4. Reinstalacja OK, launch OK
5. Gra się uruchamia ✅

### Test Case 3: Obie próby fail
1. Użytkownik uruchamia mod Epic
2. Import fail
3. Reinstalacja #1 fail (próba 1/2)
4. Reinstalacja #2 fail (próba 2/2)
5. **STOP** - pokazuje dialog błędu użytkownikowi ✅
6. NIE próbuje 3, 4, 5... raz (FIX!)

### Test Case 4: Pliki nie istnieją w InstallPath
1. Użytkownik ma zapisany `lastLaunchId`, ale usunął folder moda
2. Kod wykrywa brak plików przed importem
3. Wymusza pełną reinstalację ✅
4. Gra się instaluje i uruchamia

## Instrukcje dla użytkownika (support)

Jeśli użytkownik nadal ma problem z nieskończoną pętlą:

### Krok 1: Sprawdź logi
Poproś o plik `legendary.log.txt` z katalogu aplikacji.

### Krok 2: Wyczyść metadane Legendary
```powershell
# Usuń cache legendary
Remove-Item "$env:LOCALAPPDATA\legendary" -Recurse -Force

# Usuń config.json aplikacji (backup najpierw!)
Copy-Item "$env:APPDATA\SUSModder\config.json" "$env:APPDATA\SUSModder\config.json.backup"
Remove-Item "$env:APPDATA\SUSModder\config.json"
```

### Krok 3: Reinstalacja moda
1. Otwórz SUSModder
2. Usuń problematyczny mod
3. Zainstaluj ponownie

### Krok 4: Sprawdź uprawnienia
Upewnij się że użytkownik ma write access do:
- `C:\Program Files\Epic Games\` (instalacja vanilla Epic)
- `%APPDATA%\Among Us - Mody\` (mody)

## Monitoring

Po wydaniu tej wersji, monitoruj:
- Czy użytkownicy nadal raportują "infinite loop"?
- Ile prób reinstalacji się kończy sukcesem (czy 2 próby to wystarczająco dużo)?
- Czy nowe logi pomagają zdiagnozować problemy?

## Related Issues

- Epic launch errors (FileNotFoundError)
- Legendary metadata corruption
- Multiple mods in same ModsInstallPath causing confusion

## Changelog Entry

```
## [2.3.14-beta] - 2025-11-18

### Fixed
- **Epic launch**: Naprawiono nieskończoną pętlę reinstalacji przy błędach uruchamiania
  - Dodano limit prób reinstalacji (max 2 próby)
  - Dodano weryfikację plików przed importem i po instalacji
  - Ulepszone logowanie dla łatwiejszego debugowania
  - Po 2 nieudanych próbach pokazywany jest dialog błędu zamiast dalszych prób
```

## Files Changed

- `SUSModder.Core/GameIntegration/EpicVersionManager.cs`:
  - Dodano `_retryCount` i `MaxRetryAttempts`
  - Zmodyfikowano `LaunchGameForModAsync()` - weryfikacja plików + limit retry
  - Zmodyfikowano `PerformReinstallationSequence()` - weryfikacja post-install + recursive retry
  - Zmodyfikowano `InstallGameAsync()` - weryfikacja po instalacji

## Next Steps

1. **Zbuduj i przetestuj lokalnie** z różnymi scenariuszami
2. **Deploy jako beta** (2.3.14-beta)
3. **Poproś użytkowników o test** (szczególnie tego, który raportował problem)
4. **Zbierz feedback** - czy fix działa?
5. **Jeśli OK** → merge do release channel w następnej stable (2.4.0)
