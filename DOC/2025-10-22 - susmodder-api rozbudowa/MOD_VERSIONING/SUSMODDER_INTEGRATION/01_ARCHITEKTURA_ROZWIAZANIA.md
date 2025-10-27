# Architektura Rozwiązania - Integracja Nowych Systemów

## 🎯 Cel Dokumentu

Szczegółowy projekt architektury integracji:
- System wersjonowania modów
- Matryca kompatybilności modów DLL

---

## 📊 Diagram Wysokopoziomowy

```
┌────────────────────────────────────────────────────────────────┐
│                         SUSModder UI                            │
│                       (Avalonia + MVVM)                         │
├────────────────────────────────────────────────────────────────┤
│                                                                 │
│  MainWindowViewModel                                           │
│  ├─ FAB Menu → "Zainstaluj starszą wersję" (nowe)            │
│  ├─ "Sprawdź aktualizacje" (rozszerzone o DLL)                │
│  └─ Wywołuje ViewModels i Serwisy                             │
│                                                                 │
│  VersionSelectionDialog (nowy)                                │
│  └─ Lista wersji do wyboru                                     │
│                                                                 │
│  DllUpdateDialog (nowy)                                        │
│  └─ Lista lokalizacji DLL do zaktualizowania                   │
│                                                                 │
│  DllModSelectionViewModel (rozszerzony)                       │
│  └─ Pokazuje statusy kompatybilności (F/W/NT/NW)              │
│                                                                 │
└────────────────┬───────────────────────────────────────────────┘
                 │
                 ▼
┌────────────────────────────────────────────────────────────────┐
│                    SUSModder.Core (Logika)                      │
├────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌──────────────────────────────────────────────────────┐     │
│  │              NOWE SERWISY                             │     │
│  ├──────────────────────────────────────────────────────┤     │
│  │                                                        │     │
│  │  ModVersionService                                    │     │
│  │  ├─ GetVersionHistoryAsync(modId)                    │     │
│  │  │   → GET /susmodder-config-versions?modId={id}     │     │
│  │  └─ GetSpecificVersionAsync(modId, versionId)        │     │
│  │                                                        │     │
│  │  CompatibilityService                                 │     │
│  │  ├─ GetCompatibilityAsync(dllId, fullId)             │     │
│  │  │   → GET /api/compatibility?dllModId={id}&...      │     │
│  │  └─ GetCompatibilityStatusAsync(dllId, fullId)       │     │
│  │      → Zwraca F/W/NT/NW                               │     │
│  │                                                        │     │
│  │  DllUpdateManager                                     │     │
│  │  ├─ CheckDllUpdatesAsync()                           │     │
│  │  ├─ GetInstallLocationsAsync(dllMod)                 │     │
│  │  └─ UpdateDllInLocationsAsync(dll, locations)        │     │
│  │                                                        │     │
│  └──────────────────────────────────────────────────────┘     │
│                                                                 │
│  ┌──────────────────────────────────────────────────────┐     │
│  │          ROZSZERZONE SERWISY                          │     │
│  ├──────────────────────────────────────────────────────┤     │
│  │                                                        │     │
│  │  ModManager (zmiana)                                  │     │
│  │  └─ ModifyAsync(..., string? versionUrl = null)      │     │
│  │      → Jeśli versionUrl != null, używa tego zamiast  │     │
│  │        GitHubRepoOrLink                               │     │
│  │                                                        │     │
│  │  ModUpdateChecker (nowa metoda)                      │     │
│  │  └─ CheckForDllUpdatesAsync() - dla DLL              │     │
│  │      → Używa DllUpdateManager                         │     │
│  │                                                        │     │
│  │  DllModificationService (bez zmian)                  │     │
│  │  └─ GetModsWithDllInstalled(dll, platform)           │     │
│  │      → Używane przez DllUpdateManager                 │     │
│  │                                                        │     │
│  └──────────────────────────────────────────────────────┘     │
│                                                                 │
└────────────────┬───────────────────────────────────────────────┘
                 │
                 ▼
┌────────────────────────────────────────────────────────────────┐
│                         NOWE API                                │
├────────────────────────────────────────────────────────────────┤
│                                                                 │
│  GET /susmodder-config-versions?modId={id}                     │
│  └─ Zwraca historię wersji moda                                │
│                                                                 │
│  GET /api/compatibility?dllModId={id}&fullModId={id}           │
│  └─ Zwraca status kompatybilności                              │
│                                                                 │
└────────────────────────────────────────────────────────────────┘
```

---

## 🔄 Przepływy - Feature 1: Instalacja Starszej Wersji

### User Story
> Jako użytkownik, chcę zainstalować starszą wersję moda Town of Us (np. 5.3.1 zamiast 5.4.0),
> ponieważ nowa wersja nie działa poprawnie z moimi ulubionymi modami DLL.

### Sekwencja Zdarzeń

```
┌────────┐         ┌──────────────┐       ┌─────────────────┐       ┌─────────┐
│ User   │         │ MainWindow   │       │ ModVersionSvc   │       │   API   │
│        │         │  ViewModel   │       │                 │       │         │
└───┬────┘         └──────┬───────┘       └────────┬────────┘       └────┬────┘
    │                     │                        │                     │
    │ Klik PPM na mod     │                        │                     │
    ├────────────────────>│                        │                     │
    │                     │                        │                     │
    │ Pokaż FAB menu      │                        │                     │
    │<────────────────────┤                        │                     │
    │                     │                        │                     │
    │ Wybierz "Zainstaluj│                        │                     │
    │  starszą wersję"    │                        │                     │
    ├────────────────────>│                        │                     │
    │                     │                        │                     │
    │                     │ GetVersionHistoryAsync(modId=1)              │
    │                     ├───────────────────────>│                     │
    │                     │                        │                     │
    │                     │                        │ GET /susmodder-     │
    │                     │                        │  config-versions?   │
    │                     │                        │  modId=1            │
    │                     │                        ├────────────────────>│
    │                     │                        │                     │
    │                     │                        │ Response:           │
    │                     │                        │ { versions: [       │
    │                     │                        │   {5.4.0, ...},    │
    │                     │                        │   {5.3.1, ...},    │
    │                     │                        │   {5.3.0, ...}     │
    │                     │                        │ ]}                  │
    │                     │                        │<────────────────────│
    │                     │                        │                     │
    │                     │ List<ModVersionHistory>│                     │
    │                     │<───────────────────────┤                     │
    │                     │                        │                     │
    │ Pokaż VersionSelection                       │                     │
    │  Dialog z listą     │                        │                     │
    │  [ ] 5.4.0 (najnowsza)                       │                     │
    │  [x] 5.3.1 ← wybrana│                        │                     │
    │  [ ] 5.3.0          │                        │                     │
    │<────────────────────┤                        │                     │
    │                     │                        │                     │
    │ OK (wersja 5.3.1)   │                        │                     │
    ├────────────────────>│                        │                     │
    │                     │                        │                     │
    │                     │ ModManager.ModifyAsync(modConfig,            │
    │                     │   versionUrl="https://github.com/.../5.3.1") │
    │                     ├───────────────────────────────────────────>  │
    │                     │                        │                     │
    │                     │ [Instalacja jak zwykle, ale z konkretnym URL]
    │                     │                        │                     │
    │ "Zainstalowano      │                        │                     │
    │  Town of Us v5.3.1" │                        │                     │
    │<────────────────────┤                        │                     │
    │                     │                        │                     │
```

### Szczegóły Implementacyjne

#### 1. FAB Menu w MainWindowViewModel

```csharp
// MainWindowViewModel.cs
private void ShowModContextMenu(ModConfiguration mod)
{
    var menu = new FabMenu();

    // Istniejące opcje...
    menu.AddItem("Uruchom", () => LaunchMod(mod));
    menu.AddItem("Aktualizuj", () => UpdateMod(mod));

    // NOWA OPCJA
    menu.AddItem("Zainstaluj starszą wersję", async () =>
    {
        await ShowVersionSelectionDialog(mod);
    });

    menu.Show();
}
```

#### 2. Dialog Wyboru Wersji

```csharp
private async Task ShowVersionSelectionDialog(ModConfiguration mod)
{
    // Pobierz historię wersji
    var versions = await _modVersionService.GetVersionHistoryAsync(mod.Id);

    if (!versions.Any())
    {
        await ShowErrorAsync("Brak dostępnych wersji do pobrania");
        return;
    }

    // Pokaż dialog
    var dialog = new VersionSelectionDialog(versions);
    var result = await dialog.ShowDialog<VersionSelectionResult>(this);

    if (result != null && result.SelectedVersion != null)
    {
        await InstallSpecificVersion(mod, result.SelectedVersion);
    }
}
```

#### 3. Instalacja Konkretnej Wersji

```csharp
private async Task InstallSpecificVersion(
    ModConfiguration mod,
    ModVersionHistory selectedVersion)
{
    try
    {
        _progressReporter.Report(0, "Rozpoczynanie instalacji...");

        // Przygotuj URL do pobrania (Steam/Epic)
        string downloadUrl = _mode == "steam"
            ? selectedVersion.GitHubRepoOrLink
            : selectedVersion.EpicGitHubRepoOrLink ?? selectedVersion.GitHubRepoOrLink;

        // Wywołaj ModManager z konkretnym URL
        await _modManager.ModifyAsync(
            mod,
            _modConfigs,
            _progressReporter,
            _log,
            _userCallbacks,
            _mode,
            specificVersionUrl: downloadUrl  // NOWY PARAMETR
        );

        // Aktualizuj ModVersion w config.json
        mod.ModVersion = selectedVersion.ModVersion;
        mod.AmongVersion = selectedVersion.AmongVersion;
        ConfigManager.SaveConfig(_modConfigs);

        _progressReporter.Report(100, "Instalacja zakończona");
        await ShowInfoAsync($"Zainstalowano {mod.ModName} wersji {selectedVersion.ModVersion}");
    }
    catch (Exception ex)
    {
        _log.Write($"[ERROR] Błąd instalacji wersji: {ex.Message}");
        await ShowErrorAsync($"Nie udało się zainstalować wybranej wersji: {ex.Message}");
    }
}
```

---

## 🔄 Przepływy - Feature 2: Automatyczne Aktualizacje DLL

### User Story
> Jako użytkownik, chcę automatycznie sprawdzić aktualizacje dla modów DLL i wybrać
> w których lokalizacjach (modach FULL) chcę je zaktualizować.

### Sekwencja Zdarzeń

```
┌────────┐      ┌──────────────┐    ┌────────────────┐    ┌───────────────┐
│ User   │      │ ModUpdate    │    │ DllUpdate      │    │ DllModService │
│        │      │  Checker     │    │ Manager        │    │               │
└───┬────┘      └──────┬───────┘    └────────┬───────┘    └───────┬───────┘
    │                  │                     │                    │
    │ Klik "Sprawdź    │                     │                    │
    │  aktualizacje"   │                     │                    │
    ├─────────────────>│                     │                    │
    │                  │                     │                    │
    │                  │ CheckForDllUpdatesAsync()               │
    │                  ├────────────────────>│                    │
    │                  │                     │                    │
    │                  │                     │ Dla każdego DLL:   │
    │                  │                     │ 1. Sprawdź wersję  │
    │                  │                     │    remote vs local │
    │                  │                     │                    │
    │                  │                     │ 2. Jeśli nowsza:   │
    │                  │                     │   GetModsWithDll   │
    │                  │                     │   Installed(dll)   │
    │                  │                     ├───────────────────>│
    │                  │                     │                    │
    │                  │                     │ List<ModConfig>    │
    │                  │                     │ [TownOfUs, TOR]    │
    │                  │                     │<───────────────────│
    │                  │                     │                    │
    │                  │ List<DllUpdateInfo> │                    │
    │                  │ [{dll: AleLudu,     │                    │
    │                  │   locations: [TOU,  │                    │
    │                  │               TOR], │                    │
    │                  │   newVer: "2.0"}]   │                    │
    │                  │<────────────────────│                    │
    │                  │                     │                    │
    │ Pokaż DllUpdateDialog                 │                    │
    │  ┌────────────────────────┐            │                    │
    │  │ AleLuduMod: 1.5 → 2.0  │            │                    │
    │  │ Zainstalowany w:       │            │                    │
    │  │ [x] Town of Us         │            │                    │
    │  │ [x] The Other Roles    │            │                    │
    │  │ [ ] Mira (nie zainstal)│            │                    │
    │  └────────────────────────┘            │                    │
    │<─────────────────┤                     │                    │
    │                  │                     │                    │
    │ OK (wybrane: TOU,│                     │                    │
    │     TOR)         │                     │                    │
    ├─────────────────>│                     │                    │
    │                  │                     │                    │
    │                  │ UpdateDllInLocationsAsync(dll, [TOU,TOR])
    │                  ├────────────────────>│                    │
    │                  │                     │                    │
    │                  │                     │ Dla każdej lokalizacji:
    │                  │                     │ InstallDllToModAsync
    │                  │                     │  (dll, TOU, platform)
    │                  │                     ├───────────────────>│
    │                  │                     │                    │
    │                  │                     │ [Pobierz i zainstaluj]
    │                  │                     │<───────────────────┤
    │                  │                     │                    │
    │ "Zaktualizowano  │                     │                    │
    │  AleLuduMod w 2  │                     │                    │
    │  lokalizacjach"  │                     │                    │
    │<─────────────────┤                     │                    │
    │                  │                     │                    │
```

### Szczegóły Implementacyjne

#### 1. DllUpdateManager - Sprawdzanie Aktualizacji

```csharp
// SUSModder.Core/Services/DllUpdateManager.cs
public class DllUpdateManager
{
    private readonly DllModificationService _dllModService;
    private readonly ConfigService _configService;
    private readonly IDiagnosticsOutput _log;

    public async Task<List<DllUpdateInfo>> CheckDllUpdatesAsync(string platform)
    {
        var updatesList = new List<DllUpdateInfo>();

        try
        {
            // 1. Pobierz najnowsze wersje z API
            var remoteConfigs = await _configService.FetchRemoteConfigAsync();

            // 2. Pobierz lokalne konfiguracje
            var localConfigs = _configService.LoadConfig();

            // 3. Filtruj tylko mody DLL
            var remoteDlls = remoteConfigs
                .Where(m => m.ModType == "dll")
                .ToList();

            foreach (var remoteDll in remoteDlls)
            {
                // Znajdź lokalną wersję DLL
                var localDll = localConfigs
                    .FirstOrDefault(m => m.Id == remoteDll.Id);

                // Sprawdź czy jest nowsza wersja
                bool hasUpdate = localDll != null &&
                    !string.IsNullOrEmpty(localDll.ModVersion) &&
                    !string.IsNullOrEmpty(remoteDll.ModVersion) &&
                    localDll.ModVersion != remoteDll.ModVersion;

                if (hasUpdate)
                {
                    // Znajdź gdzie DLL jest zainstalowany
                    var locations = _dllModService
                        .GetModsWithDllInstalled(remoteDll, platform);

                    if (locations.Any())
                    {
                        updatesList.Add(new DllUpdateInfo
                        {
                            DllMod = remoteDll,
                            CurrentVersion = localDll!.ModVersion,
                            NewVersion = remoteDll.ModVersion,
                            InstallLocations = locations,
                            SelectedLocations = locations.ToList() // Domyślnie wszystkie
                        });
                    }
                }
            }

            _log.Write($"[DllUpdateManager] Znaleziono {updatesList.Count} aktualizacji DLL");
            return updatesList;
        }
        catch (Exception ex)
        {
            _log.Write($"[ERROR] Błąd sprawdzania aktualizacji DLL: {ex.Message}");
            return new List<DllUpdateInfo>();
        }
    }
}
```

#### 2. DllUpdateManager - Aktualizacja w Lokalizacjach

```csharp
public async Task<DllUpdateResult> UpdateDllInLocationsAsync(
    DllUpdateInfo updateInfo,
    string platform)
{
    var result = new DllUpdateResult
    {
        DllName = updateInfo.DllMod.ModName,
        TotalLocations = updateInfo.SelectedLocations.Count
    };

    foreach (var fullMod in updateInfo.SelectedLocations)
    {
        try
        {
            _log.Write($"[DllUpdate] Aktualizowanie {updateInfo.DllMod.ModName} w {fullMod.ModName}");

            var installedPath = await _dllModService.InstallDllToModAsync(
                updateInfo.DllMod,
                fullMod,
                platform
            );

            if (!string.IsNullOrEmpty(installedPath))
            {
                result.SuccessfulUpdates++;
                result.UpdatedLocations.Add(fullMod.ModName);
            }
            else
            {
                result.FailedUpdates++;
                result.FailedLocations.Add(fullMod.ModName);
            }
        }
        catch (Exception ex)
        {
            _log.Write($"[ERROR] Nie udało się zaktualizować w {fullMod.ModName}: {ex.Message}");
            result.FailedUpdates++;
            result.FailedLocations.Add(fullMod.ModName);
        }
    }

    return result;
}
```

#### 3. ModUpdateChecker - Nowa Metoda

```csharp
// SUSModder.Core/GameIntegration/ModUpdateChecker.cs
public static async Task<List<DllUpdateInfo>> CheckForDllUpdatesAsync(
    IConfiguration configuration,
    IDiagnosticsOutput log,
    string platform)
{
    try
    {
        // Użyj DllUpdateManager
        var dllUpdateManager = new DllUpdateManager(
            new DllModificationService(...),
            new ConfigService(...),
            log
        );

        return await dllUpdateManager.CheckDllUpdatesAsync(platform);
    }
    catch (Exception ex)
    {
        log.Write($"[ERROR] Błąd sprawdzania aktualizacji DLL: {ex.Message}");
        return new List<DllUpdateInfo>();
    }
}
```

---

## 🔄 Przepływy - Feature 3: System Kompatybilności

### User Story
> Jako użytkownik, chcę wiedzieć które mody DLL są kompatybilne z moim modem FULL,
> zanim je zainstaluję.

### Sekwencja Zdarzeń

```
┌────────┐    ┌──────────────────┐    ┌─────────────────┐    ┌─────┐
│ User   │    │ DllModSelection  │    │ Compatibility   │    │ API │
│        │    │   ViewModel      │    │    Service      │    │     │
└───┬────┘    └────────┬─────────┘    └────────┬────────┘    └──┬──┘
    │                  │                       │                 │
    │ Klik "Dodaj DLL" │                       │                 │
    │  do Town of Us   │                       │                 │
    ├─────────────────>│                       │                 │
    │                  │                       │                 │
    │                  │ LoadDllModsWithCompatibilityAsync()     │
    │                  │                       │                 │
    │                  │ Dla każdego DLL:      │                 │
    │                  │  GetCompatibilityStatusAsync(dllId,     │
    │                  │    fullModId=1)       │                 │
    │                  ├──────────────────────>│                 │
    │                  │                       │                 │
    │                  │                       │ GET /api/       │
    │                  │                       │  compatibility? │
    │                  │                       │  dllModId=5&    │
    │                  │                       │  fullModId=1    │
    │                  │                       ├────────────────>│
    │                  │                       │                 │
    │                  │                       │ Response:       │
    │                  │                       │ { status: "F",  │
    │                  │                       │   notes: "..." }│
    │                  │                       │<────────────────│
    │                  │                       │                 │
    │                  │ CompatibilityStatus="F"                 │
    │                  │<──────────────────────┤                 │
    │                  │                       │                 │
    │ Pokaż listę DLL z kolorami:              │                 │
    │  🟢 AleLuduMod (Polecany)                │                 │
    │  🔵 AUnlocker (Kompatybilny)             │                 │
    │  ⚪ CrowdedMod (Nieprzetestowany)         │                 │
    │  🔴 LevelImposter (Niekompatybilny)      │                 │
    │<─────────────────┤                       │                 │
    │                  │                       │                 │
    │ Wybór AleLuduMod │                       │                 │
    ├─────────────────>│                       │                 │
    │                  │                       │                 │
    │ Komunikat:       │                       │                 │
    │ "✅ Ten mod jest  │                       │                 │
    │  polecany dla    │                       │                 │
    │  Town of Us!"    │                       │                 │
    │<─────────────────┤                       │                 │
    │                  │                       │                 │
```

### Szczegóły Implementacyjne

#### 1. CompatibilityService

```csharp
// SUSModder.Core/Services/CompatibilityService.cs
public class CompatibilityService
{
    private readonly HttpClient _httpClient;
    private readonly IDiagnosticsOutput _log;
    private readonly string _apiBaseUrl;

    public async Task<CompatibilityInfo?> GetCompatibilityAsync(
        int dllModId,
        int fullModId)
    {
        try
        {
            var url = $"{_apiBaseUrl}/api/compatibility?dllModId={dllModId}&fullModId={fullModId}";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _log.Write($"[Compatibility] API zwróciło {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<CompatibilityResponse>(json);

            if (result?.Success == true && result.Compatibilities?.Any() == true)
            {
                var compat = result.Compatibilities.First();
                return new CompatibilityInfo
                {
                    Status = compat.Status,
                    Notes = compat.Notes,
                    TestedDate = compat.TestedDate,
                    IsCurrentVersion = compat.IsCurrentVersion
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            _log.Write($"[ERROR] Błąd pobierania kompatybilności: {ex.Message}");
            return null;
        }
    }

    public async Task<CompatibilityStatus> GetCompatibilityStatusAsync(
        int dllModId,
        int fullModId)
    {
        var info = await GetCompatibilityAsync(dllModId, fullModId);

        if (info == null)
            return CompatibilityStatus.NotTested;

        return info.Status switch
        {
            "F" => CompatibilityStatus.Favorite,
            "W" => CompatibilityStatus.Works,
            "NW" => CompatibilityStatus.NotWork,
            _ => CompatibilityStatus.NotTested
        };
    }
}
```

#### 2. DllModSelectionViewModel - Rozszerzenie

```csharp
// SUSModder/ViewModels/DllModSelectionViewModel.cs
private async Task LoadDllModsWithCompatibilityAsync()
{
    var mods = _dllModificationService.GetDllMods();

    foreach (var mod in mods)
    {
        // Pobierz status kompatybilności
        var status = await _compatibilityService.GetCompatibilityStatusAsync(
            mod.Id,
            _targetMod.Id
        );

        // Stwórz wrapper z dodatkową informacją
        var wrapper = new DllModWithCompatibility
        {
            ModConfiguration = mod,
            CompatibilityStatus = status,
            CompatibilityColor = GetColorForStatus(status),
            CompatibilityIcon = GetIconForStatus(status),
            CompatibilityDescription = GetDescriptionForStatus(status)
        };

        DllModsWithCompatibility.Add(wrapper);
    }
}

private Brush GetColorForStatus(CompatibilityStatus status)
{
    return status switch
    {
        CompatibilityStatus.Favorite => Brushes.Green,
        CompatibilityStatus.Works => Brushes.Blue,
        CompatibilityStatus.NotWork => Brushes.Red,
        _ => Brushes.Gray
    };
}

private string GetDescriptionForStatus(CompatibilityStatus status)
{
    return status switch
    {
        CompatibilityStatus.Favorite => "Polecany - działa idealnie",
        CompatibilityStatus.Works => "Kompatybilny - działa poprawnie",
        CompatibilityStatus.NotWork => "Niekompatybilny - nie działa",
        _ => "Nieprzetestowany - brak informacji"
    };
}
```

---

## 📦 Nowe Komponenty - Szczegółowy Przegląd

### 1. ModVersionService

**Lokalizacja**: `SUSModder.Core/Services/ModVersionService.cs`

**Odpowiedzialność**:
- Pobieranie historii wersji z `/susmodder-config-versions`
- Cache'owanie wyników (5 minut)
- Mapowanie JSON na `ModVersionHistory`

**Główne Metody**:
```csharp
Task<List<ModVersionHistory>> GetVersionHistoryAsync(int modId)
Task<ModVersionHistory?> GetSpecificVersionAsync(int modId, int versionId)
```

### 2. CompatibilityService

**Lokalizacja**: `SUSModder.Core/Services/CompatibilityService.cs`

**Odpowiedzialność**:
- Pobieranie statusów kompatybilności z `/api/compatibility`
- Cache'owanie wyników (10 minut)
- Mapowanie JSON na `CompatibilityInfo`

**Główne Metody**:
```csharp
Task<CompatibilityInfo?> GetCompatibilityAsync(int dllModId, int fullModId)
Task<CompatibilityStatus> GetCompatibilityStatusAsync(int dllModId, int fullModId)
```

### 3. DllUpdateManager

**Lokalizacja**: `SUSModder.Core/Services/DllUpdateManager.cs`

**Odpowiedzialność**:
- Sprawdzanie dostępnych aktualizacji DLL
- Wykrywanie lokalizacji gdzie DLL jest zainstalowany
- Aktualizacja DLL w wybranych lokalizacjach

**Główne Metody**:
```csharp
Task<List<DllUpdateInfo>> CheckDllUpdatesAsync(string platform)
Task<DllUpdateResult> UpdateDllInLocationsAsync(DllUpdateInfo info, string platform)
```

---

## 🔒 Obsługa Błędów i Edge Cases

### 1. API Niedostępne

**Scenariusz**: Endpoint `/susmodder-config-versions` nie odpowiada

**Obsługa**:
```csharp
try
{
    var versions = await _modVersionService.GetVersionHistoryAsync(modId);
}
catch (HttpRequestException ex)
{
    _log.Write($"[ERROR] API niedostępne: {ex.Message}");
    await ShowErrorAsync(
        "Nie można pobrać historii wersji. Sprawdź połączenie internetowe."
    );
    return;
}
```

### 2. Brak Historii Wersji

**Scenariusz**: Mod nie ma historii (nowy mod lub brak danych w API)

**Obsługa**:
```csharp
var versions = await _modVersionService.GetVersionHistoryAsync(modId);

if (!versions.Any())
{
    await ShowInfoAsync(
        "Brak dostępnych starszych wersji dla tego moda."
    );
    return;
}
```

### 3. Pobieranie Konkretnej Wersji Się Nie Powiodło

**Scenariusz**: URL do wersji jest nieprawidłowy lub plik nie istnieje

**Obsługa**:
```csharp
try
{
    await _modManager.ModifyAsync(..., specificVersionUrl);
}
catch (HttpRequestException ex)
{
    _log.Write($"[ERROR] Nie można pobrać wersji: {ex.Message}");
    await ShowErrorAsync(
        $"Nie można pobrać wybranej wersji moda.\n\n" +
        $"Możliwe przyczyny:\n" +
        $"- Plik został usunięty z GitHub\n" +
        $"- Link jest nieprawidłowy\n" +
        $"- Brak połączenia z internetem"
    );
}
```

### 4. DLL Zainstalowany, Ale Plik Nie Istnieje

**Scenariusz**: Config mówi że DLL jest zainstalowany, ale plik został ręcznie usunięty

**Obsługa**:
```csharp
// W DllModificationService.IsDllInstalledInMod()
public bool IsDllInstalledInMod(ModConfiguration dllMod, ModConfiguration targetMod, string platform)
{
    try
    {
        if (string.IsNullOrEmpty(targetMod.InstallPath))
            return false;

        string downloadUrl = GetDllDownloadUrl(dllMod, platform);
        if (string.IsNullOrEmpty(downloadUrl)) return false;

        string fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
        string filePath = Path.Combine(targetMod.InstallPath, dllMod.DllInstallPath ?? "BepInEx\\plugins", fileName);

        // Sprawdzamy faktyczne istnienie pliku
        bool exists = File.Exists(filePath);

        if (!exists)
        {
            _diagnosticsOutput.Write($"[WARNING] Config mówi że {dllMod.ModName} jest w {targetMod.ModName}, ale plik nie istnieje");
        }

        return exists;
    }
    catch (Exception ex)
    {
        _diagnosticsOutput.Write($"[ERROR] Błąd sprawdzania instalacji DLL: {ex.Message}");
        return false;
    }
}
```

### 5. Brak Informacji o Kompatybilności

**Scenariusz**: API nie ma danych o kompatybilności dla danej kombinacji

**Obsługa**:
```csharp
var status = await _compatibilityService.GetCompatibilityStatusAsync(dllId, fullId);

// Jeśli API nie zwraca danych, domyślnie NT (Not Tested)
if (status == CompatibilityStatus.NotTested)
{
    // UI pokazuje szary kolor + ikona "?"
    // Ostrzeżenie: "Ten mod nie został jeszcze przetestowany z tym modem FULL"
}
```

---

## 🎨 UX/UI Considerations

### 1. Progres Instalacji Wersji

```csharp
_progressReporter.Report(0, "Pobieranie historii wersji...");
// API call
_progressReporter.Report(30, "Pobieranie wybranej wersji...");
// Download
_progressReporter.Report(60, "Rozpakowywanie...");
// Extract
_progressReporter.Report(90, "Finalizowanie instalacji...");
// Config update
_progressReporter.Report(100, "Gotowe!");
```

### 2. Kolorowe Oznaczenia Kompatybilności

```
┌────────────────────────────────────────────┐
│ Dostępne mody DLL dla Town of Us 5.3.1:   │
├────────────────────────────────────────────┤
│ 🟢 AleLuduMod                              │
│    ✅ Polecany - działa idealnie            │
│    Ostatnie testy: 2025-10-15              │
│                                            │
│ 🔵 AUnlocker                               │
│    ℹ️ Kompatybilny - działa poprawnie      │
│    Ostatnie testy: 2025-10-14              │
│                                            │
│ ⚪ CrowdedMod                               │
│    ❓ Nieprzetestowany - brak informacji    │
│                                            │
│ 🔴 LevelImposter                           │
│    ❌ Niekompatybilny - nie działa          │
│    Notatka: Crash przy ładowaniu map      │
│    Link: github.com/issues/123            │
└────────────────────────────────────────────┘
```

### 3. Dialog Aktualizacji DLL

```
┌───────────────────────────────────────────────┐
│ Dostępne aktualizacje modów DLL               │
├───────────────────────────────────────────────┤
│                                               │
│ AleLuduMod: 1.5.0 → 2.0.0                    │
│ ────────────────────────────────────────────  │
│ Zainstalowany w następujących lokalizacjach:  │
│                                               │
│ [x] Town of Us v5.3.1                        │
│ [x] The Other Roles v4.8.0                   │
│ [ ] Mira v3.0.0                              │
│                                               │
│ Domyślnie zaznaczone: wszystkie lokalizacje   │
│ Odznacz te, których nie chcesz aktualizować   │
│                                               │
│ [Anuluj]                    [Aktualizuj (2)]  │
└───────────────────────────────────────────────┘
```

---

## 📈 Performance

### Cache'owanie

**ModVersionService**:
- Cache historii wersji: 5 minut
- Key: `version_history_{modId}`
- Invalidacja: ręczna lub timeout

**CompatibilityService**:
- Cache statusów: 10 minut
- Key: `compatibility_{dllId}_{fullId}`
- Invalidacja: ręczna lub timeout

**Przykład**:
```csharp
private static readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

public async Task<List<ModVersionHistory>> GetVersionHistoryAsync(int modId)
{
    string cacheKey = $"version_history_{modId}";

    if (_cache.TryGetValue(cacheKey, out List<ModVersionHistory>? cached))
    {
        _log.Write($"[Cache HIT] Historia wersji dla moda {modId}");
        return cached!;
    }

    var versions = await FetchVersionHistoryFromApiAsync(modId);

    _cache.Set(cacheKey, versions, TimeSpan.FromMinutes(5));
    _log.Write($"[Cache SET] Historia wersji dla moda {modId}");

    return versions;
}
```

---

## ✅ Checklist Architektury

- [x] Zdefiniowano wszystkie nowe serwisy
- [x] Zdefiniowano przepływy dla każdej funkcjonalności
- [x] Zidentyfikowano miejsca modyfikacji istniejącego kodu
- [x] Zaprojektowano obsługę błędów
- [x] Zaprojektowano UX/UI dla nowych funkcji
- [x] Zdefiniowano strategię cache'owania
- [x] Uwzględniono edge cases

---

## 📌 Następne Kroki

Po przeczytaniu tego dokumentu, przejdź do:

1. **[02_MODELE_DANYCH.md](./02_MODELE_DANYCH.md)** - Kod C# dla nowych modeli
2. **[03_INSTALACJA_STARSZYCH_WERSJI.md](./03_INSTALACJA_STARSZYCH_WERSJI.md)** - Szczegóły implementacji

---

**Ostatnia aktualizacja:** 2025-10-22
**Wersja:** 1.0
