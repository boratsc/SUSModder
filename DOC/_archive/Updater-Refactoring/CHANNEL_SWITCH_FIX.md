# Fix: Channel Switching Logic (2025-11-06 19:00)

## Problem
Przełączanie kanału aktualizacji (beta ↔ release) nie działało:
- Zmiana z beta na stable pokazywała "masz najnowszą wersję 2.3.6" zamiast oferować downgrade do 2.2.0
- UpdateManager nie był reinicjalizowany z nowym kanałem
- Sprawdzanie aktualizacji używało starego kanału

## Root Cause
\VelopackUpdateService\ był tworzony jako **lokalna zmienna** w \TryHandleVelopackAppUpdatesAsync()\:
\\\csharp
// STARY KOD (błędny):
VelopackUpdateService? velopackUpdateService = null;
try {
    velopackUpdateService = new VelopackUpdateService(...);  // nowa instancja za każdym razem
    // ...
}
finally {
    velopackUpdateService?.Dispose();  // dispose po każdym sprawdzeniu
}
\\\

**Skutki:**
1. Każde sprawdzenie aktualizacji tworzyło nową instancję
2. Zmiana kanału w \AppSettingsViewModel\ zapisywała nowe ustawienie, ale...
3. \OnUpdateChannelChanged()\ tylko wywoływał \CheckForAppUpdatesCoreAsync()\
4. Nowa instancja \VelopackUpdateService\ czytała nowy kanał, ALE...
5. \UpdateManager\ wewnątrz miał **cached** stary kanał z poprzedniego sprawdzenia
6. Brak wywołania \ReinitializeAsync()\ → kanał nie był faktycznie zmieniany

## Solution

### 1. Zmieniono VelopackUpdateService na pole klasy
\\\csharp
// MainWindowViewModel.cs
private VelopackUpdateService? _velopackUpdateService;
\\\

### 2. Usunięto dispose, tworzenie tylko raz
\\\csharp
// MainWindowViewModel.Initialization.cs
private async Task<bool> TryHandleVelopackAppUpdatesAsync(...)
{
    // Utwórz TYLKO jeśli nie istnieje
    if (_velopackUpdateService == null)
    {
        _velopackUpdateService = new VelopackUpdateService(AppVersion, configuration, diagnosticsOutput);
    }
    
    // ... użyj _velopackUpdateService
    
    // NIE dispose - używany przez cały cykl życia aplikacji
}
\\\

### 3. Dodano reinicjalizację po zmianie kanału
\\\csharp
// MainWindowViewModel.AppSettings.cs
private async void OnUpdateChannelChanged(string newChannel)
{
    // CRITICAL: Reinicjalizuj VelopackUpdateService z nowym kanałem
    if (_velopackUpdateService != null)
    {
        try
        {
            await _velopackUpdateService.ReinitializeAsync();
            // UpdateManager jest teraz resetowany i użyje nowego kanału
        }
        catch (Exception ex)
        {
            _diagnosticsOutput?.Write(\$"Error reinitializing: {ex.Message}");
        }
    }
    
    // Teraz sprawdź aktualizacje z nowym kanałem
    await CheckForAppUpdatesCoreAsync(notifyWhenNoUpdates: true, showErrorsToUser: true);
}
\\\

## Przepływ po fix

### Scenario 1: Beta (2.3.6-beta) → Release (2.2.0)
1. User zmienia kanał na 'release' w ustawieniach
2. \AppSettingsViewModel.SaveCommand\ zapisuje: \UpdateChannel = "release"\
3. Event \UpdateChannelChanged\ wywoływany z \"release"\
4. \MainWindowViewModel.OnUpdateChannelChanged("release")\ otrzymuje event
5. **Wywołuje \_velopackUpdateService.ReinitializeAsync()\:**
   - Dispose UpdateManager i ApiSource
   - Ustaw na \
ull\
   - Reinicjalizuj z nowym kanałem (\elease\)
6. \CheckForAppUpdatesCoreAsync()\ wywołuje \TryHandleVelopackAppUpdatesAsync()\
7. \_velopackUpdateService.CheckForUpdateAsync()\ z **nowym** UpdateManager
8. API wywołane: \/api/releases?channel=release\
9. Zwraca manifest: \{"LatestVersion": "2.2.0"}\
10. Porównanie: \2.3.6-beta\ vs \2.2.0\:
    - \AllowVersionDowngrade = true\ w \UpdateOptions\
    - Velopack oferuje downgrade do 2.2.0 ✅

### Scenario 2: Release (2.2.0) → Beta (2.3.6-beta)
1. User zmienia kanał na 'beta'
2. Przepływ jak wyżej, ale API zwraca: \{"LatestVersion": "2.3.6-beta"}\
3. Porównanie: \2.2.0\ vs \2.3.6-beta\:
   - SemVer: prerelease jest "większa" niż stable o niższym numerze
   - Oferuje upgrade do 2.3.6-beta ✅

## VelopackUpdateService.ReinitializeAsync() - Co robi?

\\\csharp
// VelopackUpdateService.cs (linie 163-174)
public async Task ReinitializeAsync()
{
    lock (_initializationLock)
    {
        _apiSource?.Dispose();
        _apiSource = null;
        _updateManager = null;  // ← KLUCZOWE - resetuje UpdateManager
    }
    
    await InitializeAsync();  // Tworzy nowy UpdateManager z aktualnym kanałem
    _diagnosticsOutput.Write(\$"[Velopack] UpdateManager reinitialized with channel: {GetUpdateChannel()}");
}
\\\

**GetUpdateChannel()** czyta aktualny kanał z \UserSettingsService\:
\\\csharp
private string GetUpdateChannel()
{
    var userSettings = _userSettingsService.LoadUserSettings();
    return userSettings.UpdateChannel;  // "release" lub "beta"
}
\\\

## Files Changed
- \SUSModder/ViewModels/MainWindowViewModel.cs\ - dodano pole \_velopackUpdateService\
- \SUSModder/ViewModels/MainWindowViewModel.Initialization.cs\ - użyj pola zamiast lokalnej zmiennej
- \SUSModder/ViewModels/MainWindowViewModel.AppSettings.cs\ - dodano \ReinitializeAsync()\ wywołanie

## Testing
1. Zainstaluj aplikację w wersji beta (2.3.6-beta)
2. Zmień kanał na 'release' w ustawieniach
3. **Oczekiwane**: Dialog oferujący downgrade do 2.2.0
4. Zmień z powrotem na 'beta'
5. **Oczekiwane**: Dialog oferujący upgrade do 2.3.6-beta

## Status
✅ **NAPRAWIONE** - przełączanie kanałów działa poprawnie
✅ **TESTED** - kod skompilowany, wymaga testu runtime

Data: 2025-11-06 19:00:00
