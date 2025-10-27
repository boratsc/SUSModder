# Analiza Obecnego Stanu SUSModder

## 🎯 Cel Dokumentu

Szczegółowa analiza istniejącego kodu SUSModder pod kątem integracji nowych systemów:
- System wersjonowania modów
- Matryca kompatybilności

---

## 📊 Przegląd Architektury

### Główne Komponenty

```
SUSModder/
├── SUSModder/                    # Projekt UI (Avalonia)
│   ├── ViewModels/
│   │   ├── MainWindowViewModel.cs        (469 linii)
│   │   ├── DllModSelectionViewModel.cs   (333 linie)
│   │   └── ...
│   └── Views/
│       └── ...
│
└── SUSModder.Core/              # Projekt logiki biznesowej
    ├── Configuration/
    │   ├── ModConfig.cs                  # Model ModConfiguration
    │   ├── ModConfigHandler.cs           # Obsługa konfiguracji lokalnych/serwerowych
    │   └── ConfigManager.cs              # Zarządzanie config.json
    │
    ├── GameIntegration/
    │   ├── ModManager.cs                 # Instalacja modów FULL (Steam/Epic)
    │   ├── ModUpdate.cs                  # Aktualizacja pojedynczego moda FULL
    │   ├── ModUpdateChecker.cs           # Sprawdzanie aktualizacji modów FULL
    │   ├── ModDelete.cs                  # Usuwanie modów
    │   └── EpicVersionManager.cs         # Zarządzanie wersjami Epic
    │
    └── Services/
        ├── DllModificationService.cs     # Instalacja/deinstalacja modów DLL
        ├── ConfigService.cs              # Ładowanie/zapisywanie konfiguracji
        └── ModService.cs                 # Wysokopoziomowe operacje na modach
```

---

## 1️⃣ ModConfiguration - Model Danych

### Lokalizacja
`SUSModder.Core/Configuration/ModConfig.cs:14-69`

### Analiza

```csharp
public class ModConfiguration : INotifyPropertyChanged
{
    [JsonPropertyName("Id")]
    public int Id { get; set; }

    [JsonPropertyName("ModName")]
    public string ModName { get; set; } = string.Empty;

    [JsonPropertyName("PngFileName")]
    public string PngFileName { get; set; } = string.Empty;

    [JsonPropertyName("InstallPath")]
    public string? InstallPath { get; set; }

    [JsonPropertyName("GitHubRepoOrLink")]
    public string GitHubRepoOrLink { get; set; } = string.Empty;

    [JsonPropertyName("EpicGitHubRepoOrLink")]
    public string? EpicGitHubRepoOrLink { get; set; }

    [JsonPropertyName("ModType")]
    public string ModType { get; set; } = string.Empty;  // "full" lub "dll"

    [JsonPropertyName("DllInstallPath")]
    public string? DllInstallPath { get; set; }

    [JsonPropertyName("ModVersion")]
    public string ModVersion { get; set; } = string.Empty;

    [JsonPropertyName("LastUpdated")]
    public DateTime? LastUpdated { get; set; }

    [JsonPropertyName("AmongVersion")]
    public string AmongVersion { get; set; } = string.Empty;

    [JsonPropertyName("Description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("HasRoles")]
    public bool? HasRoles { get; set; }
}
```

### ✅ Co działa dobrze

- **Zgodność z API**: Pola odpowiadają strukturze `/susmodder-config`
- **INotifyPropertyChanged**: Wspiera binding w UI
- **Nullable types**: Poprawne używanie `?` dla opcjonalnych pól
- **ModVersion i AmongVersion**: Już istnieją - zgodne z systemem wersjonowania

### ⚠️ Co wymaga uwagi

- **Brak pola CreatedAt/UpdatedAt**: Nie śledzimy kiedy mod został dodany/zaktualizowany lokalnie
- **Brak informacji o kompatybilności**: Trzeba będzie dodać osobny model
- **Brak listy dostępnych wersji**: Potrzebny nowy model `ModVersionHistory`

### 📝 Wnioski

**Model jest wystarczający** do reprezentacji najnowszej wersji moda. Nie wymaga zmian.

Potrzebne **nowe modele**:
- `ModVersionHistory` - reprezentuje wersję moda z historii
- `CompatibilityInfo` - reprezentuje informację o kompatybilności

---

## 2️⃣ ModUpdateChecker - Sprawdzanie Aktualizacji

### Lokalizacja
`SUSModder.Core/GameIntegration/ModUpdateChecker.cs`

### Analiza Kluczowych Metod

#### a) CheckForModUpdatesAsync (linie 20-56)

```csharp
public static async Task CheckForModUpdatesAsync(
    IConfiguration configuration,
    IDiagnosticsOutput log,
    IUserInteraction userInteraction,
    IProgressReporter? progress = null)
{
    // 1. Pobierz config.json z endpointu
    var remoteConfigs = await DownloadRemoteConfigAsync(configuration, log);

    // 2. Pobierz lokalne konfiguracje
    var localConfigs = ConfigManager.LoadConfig();

    // 3. Znajdź zainstalowane mody które mają dostępne aktualizacje
    var modsToUpdate = FindModsWithUpdates(localConfigs, remoteConfigs);

    // 4. Zaproponuj aktualizację użytkownikowi
    if (modsToUpdate.Any())
    {
        ProposeUpdatesToUser(modsToUpdate, configuration, log, userInteraction, progress);
    }
}
```

#### b) FindModsWithUpdates (linie 164-197)

```csharp
private static List<ModUpdateInfo> FindModsWithUpdates(
    List<ModConfiguration> localConfigs,
    List<ModConfiguration> remoteConfigs)
{
    var modsToUpdate = new List<ModUpdateInfo>();

    foreach (var localMod in localConfigs)
    {
        // Sprawdź tylko zainstalowane mody typu "full" i "dll"
        if ((localMod.ModType != "full" && localMod.ModType != "dll") ||
            string.IsNullOrEmpty(localMod.InstallPath) ||
            !Directory.Exists(localMod.InstallPath))
            continue;

        var remoteMod = remoteConfigs.FirstOrDefault(r => r.Id == localMod.Id);
        if (remoteMod == null)
            continue;

        // Porównaj tylko ModVersion
        if (HasNewerVersion(localMod, remoteMod))
        {
            modsToUpdate.Add(new ModUpdateInfo
            {
                LocalMod = localMod,
                RemoteMod = remoteMod,
                CurrentVersion = localMod.ModVersion ?? "Nieznana",
                NewVersion = remoteMod.ModVersion ?? "Nieznana",
                ModName = localMod.ModName ?? "Nieznany",
                Description = remoteMod.Description ?? "",
                IsSelected = true
            });
        }
    }

    return modsToUpdate;
}
```

#### c) HasNewerVersion (linie 199-210)

```csharp
private static bool HasNewerVersion(ModConfiguration localMod, ModConfiguration remoteMod)
{
    if (!string.IsNullOrEmpty(localMod.ModVersion) &&
        !string.IsNullOrEmpty(remoteMod.ModVersion))
    {
        return !string.Equals(localMod.ModVersion, remoteMod.ModVersion,
                             StringComparison.OrdinalIgnoreCase);
    }
    if (string.IsNullOrEmpty(localMod.ModVersion) &&
        !string.IsNullOrEmpty(remoteMod.ModVersion))
    {
        return true;
    }
    return false;
}
```

### ✅ Co działa dobrze

- **Asynchroniczność**: Wszystkie operacje sieciowe są async
- **Obsługa błędów**: Try-catch w odpowiednich miejscach
- **Dependency Injection**: Przyjmuje `IConfiguration`, `IDiagnosticsOutput`
- **Sprawdza DLL**: Już wspiera mody typu "dll" (linia 171)

### ⚠️ Problemy z Modami DLL

**Problem 1: Nie wykrywa wielu lokalizacji**
```csharp
// Linia 172: Sprawdza tylko czy InstallPath istnieje
if (string.IsNullOrEmpty(localMod.InstallPath) ||
    !Directory.Exists(localMod.InstallPath))
    continue;
```

**Problem**: DLL mody nie mają `InstallPath` - są instalowane w katalogach modów FULL!

**Przykład**:
- AleLuduMod (DLL) jest zainstalowany w:
  - `C:/Mody/Town of Us/BepInEx/plugins/AleLuduMod.dll`
  - `C:/Mody/The Other Roles/BepInEx/plugins/AleLuduMod.dll`
  - `C:/Mody/Mira/BepInEx/plugins/AleLuduMod.dll`

- `localMod.InstallPath` dla AleLuduMod = `null` lub pusty
- **Rezultat**: DLL nie zostanie wykryty jako zainstalowany!

**Problem 2: Brak logiki wyboru lokalizacji**
Nawet jeśli wykryjemy aktualizację DLL, nie ma mechanizmu pozwalającego użytkownikowi wybrać gdzie zaktualizować.

### 📝 Wnioski

**Wymaga rozszerzenia**:
1. Nowa metoda `CheckForDllUpdatesAsync()` - dedykowana dla DLL
2. Logika wykrywania lokalizacji DLL w modach FULL
3. Dialog wyboru lokalizacji do aktualizacji

**Istniejąca logika dla FULL modów jest OK** - można ją zostawić bez zmian.

---

## 3️⃣ DllModificationService - Instalacja/Deinstalacja DLL

### Lokalizacja
`SUSModder.Core/Services/DllModificationService.cs`

### Analiza Kluczowych Metod

#### a) InstallDllToModAsync (linie 102-171)

```csharp
public async Task<string?> InstallDllToModAsync(
    ModConfiguration dllMod,
    ModConfiguration targetMod,
    string platform)
{
    // 1. Sprawdź czy targetMod ma InstallPath
    if (string.IsNullOrEmpty(targetMod.InstallPath))
    {
        _diagnosticsOutput.Write("Target mod has no install path");
        return null;
    }

    // 2. Wybierz odpowiedni link do pobrania (Steam/Epic)
    string downloadUrl = GetDllDownloadUrl(dllMod, platform);

    // 3. Wyciągnij nazwę pliku z URL
    string fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);

    // 4. Zbuduj ścieżkę docelową
    string targetPath = Path.Combine(
        targetMod.InstallPath,
        dllMod.DllInstallPath ?? "BepInEx\\plugins",
        fileName
    );

    // 5. Pobierz i zapisz plik
    var response = await _httpClient.GetAsync(downloadUrl);
    response.EnsureSuccessStatusCode();

    var content = await response.Content.ReadAsByteArrayAsync();
    await File.WriteAllBytesAsync(targetPath, content);

    return targetPath;
}
```

#### b) IsDllInstalledInMod (linie 221-244)

```csharp
public bool IsDllInstalledInMod(
    ModConfiguration dllMod,
    ModConfiguration targetMod,
    string platform)
{
    if (string.IsNullOrEmpty(targetMod.InstallPath))
        return false;

    string downloadUrl = GetDllDownloadUrl(dllMod, platform);
    if (string.IsNullOrEmpty(downloadUrl)) return false;

    string fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
    string filePath = Path.Combine(
        targetMod.InstallPath,
        dllMod.DllInstallPath ?? "BepInEx\\plugins",
        fileName
    );

    return File.Exists(filePath);
}
```

#### c) GetModsWithDllInstalled (linie 61-80)

```csharp
public List<ModConfiguration> GetModsWithDllInstalled(
    ModConfiguration dllMod,
    string platform)
{
    var configs = _configService.LoadConfig();
    var installedFullMods = configs
        .Where(m => m.ModType.Equals("full", StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrEmpty(m.InstallPath))
        .Where(m => IsDllInstalledInMod(dllMod, m, platform))
        .OrderBy(m => m.ModName)
        .ToList();

    return installedFullMods;
}
```

### ✅ Co działa dobrze

- **Pełna implementacja instalacji/deinstalacji**
- **Wspiera Steam i Epic**: `GetDllDownloadUrl()` wybiera odpowiedni link
- **Wykrywanie zainstalowanych DLL**: `GetModsWithDllInstalled()` - **TO JEST KLUCZOWE!**
- **Asynchroniczność**: Pobieranie plików jest async

### ✅ Co możemy wykorzystać

**Metoda `GetModsWithDllInstalled()` jest DOKŁADNIE tym czego potrzebujemy!**

Przykładowe użycie dla aktualizacji:
```csharp
// Pobierz DLL mod do aktualizacji
var dllMod = configs.FirstOrDefault(m => m.Id == 5); // AleLuduMod

// Znajdź wszystkie mody FULL gdzie jest zainstalowany
var locations = dllModService.GetModsWithDllInstalled(dllMod, "steam");
// Zwraca: [Town of Us, The Other Roles, Mira]

// Użytkownik wybiera które zaktualizować
// ...

// Aktualizuj w wybranych lokalizacjach
foreach (var fullMod in selectedLocations)
{
    await dllModService.InstallDllToModAsync(dllMod, fullMod, "steam");
}
```

### 📝 Wnioski

**Serwis jest gotowy do użycia!** Wymaga tylko **opakowania w wyższym poziomie**:
- Nowy serwis `DllUpdateManager` który użyje `GetModsWithDllInstalled()`
- Dialog UI do wyboru lokalizacji

---

## 4️⃣ ModManager - Instalacja Modów FULL

### Lokalizacja
`SUSModder.Core/GameIntegration/ModManager.cs`

### Analiza Kluczowej Metody

#### ModifyAsync (linie 33-55)

```csharp
public async Task ModifyAsync(
    ModConfiguration modConfig,
    List<ModConfiguration> modConfigs,
    IProgressReporter progress,
    IDiagnosticsOutput log,
    ModManagerUserCallbacks userCallbacks,
    string mode)
{
    this.log = log;

    if (modConfig.ModType == "full")
    {
        if (mode == "steam")
        {
            await InstallSteamAsync(modConfig, modConfigs, progress, log, userCallbacks);
        }
        else
        {
            // Epic - obsługa przez EpicVersionManager
        }
    }
}
```

#### InstallSteamAsync (linie 57-300+)

**Przepływ**:
1. Pobiera vanilla Among Us (jeśli nie istnieje)
2. Pobiera mod z `modConfig.GitHubRepoOrLink`
3. Rozpakuje vanilla do katalogu moda
4. Rozpakuje moda i skopiuje pliki
5. Aktualizuje `config.json`

### ⚠️ Problem z Wersjami

**Obecnie pobiera ZAWSZE z `modConfig.GitHubRepoOrLink`**

```csharp
// Linia 138
string downloadUrl = !string.IsNullOrEmpty(modConfig.GitHubRepoOrLink)
    ? modConfig.GitHubRepoOrLink
    : throw new InvalidOperationException("Brak linku do pobrania moda.");
```

**Problem**: Nie ma możliwości przekazania konkretnego linka do starszej wersji!

### 💡 Rozwiązanie

**Opcja 1: Dodać parametr `string? specificVersionUrl = null`**

```csharp
public async Task ModifyAsync(
    ModConfiguration modConfig,
    List<ModConfiguration> modConfigs,
    IProgressReporter progress,
    IDiagnosticsOutput log,
    ModManagerUserCallbacks userCallbacks,
    string mode,
    string? specificVersionUrl = null)  // NOWE
{
    // ...
}
```

**Opcja 2: Rozszerzyć `ModConfiguration` o pole `OverrideDownloadUrl`**

```csharp
// Przed wywołaniem ModifyAsync:
modConfig.OverrideDownloadUrl = "https://github.com/tou/v5.3.1.zip";
await modManager.ModifyAsync(...);
```

**Rekomendacja**: **Opcja 1** - czystsza, nie modyfikuje modelu

### 📝 Wnioski

**Wymaga małej modyfikacji**:
- Dodać parametr opcjonalny `specificVersionUrl`
- Użyć tego parametru jeśli jest podany, w przeciwnym razie `GitHubRepoOrLink`

---

## 5️⃣ ConfigManager - Zarządzanie Konfiguracją

### Lokalizacja
`SUSModder.Core/Configuration/ModConfig.cs:71-298`

### Analiza Kluczowych Metod

#### LoadConfig (linie 77-116)

```csharp
public static List<ModConfiguration> LoadConfig()
{
    var configRepo = new ConfigRepository(exeDir);
    var localConfigs = configRepo.LoadConfig();

    if (localConfigs.Count > 0)
    {
        return localConfigs;  // Użyj lokalnego config.json
    }

    // Jeśli brak lokalnego, pobierz z API
    var apiConfigs = Task.Run(async () =>
        await FetchConfigFromApiAsync()).GetAwaiter().GetResult();

    if (apiConfigs.Count > 0)
    {
        configRepo.SaveConfig(apiConfigs);
    }

    return apiConfigs;
}
```

#### FetchConfigFromApiAsync (linie 118-155)

```csharp
private static async Task<List<ModConfiguration>> FetchConfigFromApiAsync()
{
    using (var httpClient = new HttpClient())
    {
        httpClient.Timeout = TimeSpan.FromSeconds(15);

        string configApiUrl = GetUpdateServerUrl();
        // URL = https://susmodder.app/api/susmodder-config

        string downloadToken = SecretProvider.GetDownloadToken();
        httpClient.DefaultRequestHeaders.Add("Authorization", downloadToken);

        var response = await httpClient.GetStringAsync(configApiUrl);
        var configs = JsonSerializer.Deserialize<List<ModConfiguration>>(response);

        return configs;
    }
}
```

### ✅ Co działa dobrze

- **Automatyczne pobieranie**: Jeśli brak lokalnego config, pobiera z API
- **Autoryzacja**: Używa tokena z `SecretProvider`
- **Timeout**: Ustawiony na 15 sekund

### 📝 Wnioski

**ConfigManager jest OK** - nie wymaga zmian do obsługi wersjonowania.

Historię wersji będziemy pobierać osobno przez **nowy serwis `ModVersionService`**.

---

## 6️⃣ DllModSelectionViewModel - UI Wyboru DLL

### Lokalizacja
`SUSModder/ViewModels/DllModSelectionViewModel.cs`

### Analiza

#### Konstruktor (linie 57-109)

```csharp
public DllModSelectionViewModel(
    DllModificationService dllModificationService,
    ModConfiguration targetMod,
    string platform = "steam")
{
    _dllModificationService = dllModificationService;
    _targetMod = targetMod;
    Platform = platform;

    LoadDllMods();  // Ładuje listę dostępnych DLL

    InstallSelectedDllsCommand = ReactiveCommand.CreateFromTask(async () =>
    {
        await InstallSelectedDllsAsync(Platform);
    });
}
```

#### LoadDllMods (linie 110-129)

```csharp
private void LoadDllMods()
{
    var mods = _dllModificationService.GetDllMods();
    foreach (var mod in mods)
    {
        mod.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ModConfiguration.IsSelected))
            {
                if (mod.IsSelected && !SelectedDllMods.Contains(mod))
                    SelectedDllMods.Add(mod);
                else if (!mod.IsSelected && SelectedDllMods.Contains(mod))
                    SelectedDllMods.Remove(mod);
            }
        };
    }
    DllMods = new ObservableCollection<ModConfiguration>(mods);
}
```

### ✅ Co działa dobrze

- **ReactiveUI**: Wspiera binding i komendy
- **ObservableCollection**: UI automatycznie się aktualizuje
- **Multi-select**: Użytkownik może wybrać wiele DLL

### ⚠️ Brak Informacji o Kompatybilności

**Obecnie**: Lista DLL jest "płaska" - wszystkie mody wyglądają tak samo

**Potrzeba**: Pokazać statusy kompatybilności:
- 🟢 Polecany (F)
- 🔵 Kompatybilny (W)
- ⚪ Nieprzetestowany (NT)
- 🔴 Niekompatybilny (NW)

### 💡 Rozwiązanie

Rozszerzyć `LoadDllMods()`:

```csharp
private async Task LoadDllModsWithCompatibilityAsync()
{
    var mods = _dllModificationService.GetDllMods();

    foreach (var mod in mods)
    {
        // NOWE: Pobierz status kompatybilności
        var compatibility = await _compatibilityService
            .GetCompatibilityStatusAsync(mod.Id, _targetMod.Id);

        mod.CompatibilityStatus = compatibility; // Potrzebne nowe pole

        // Binding jak dotychczas
        mod.PropertyChanged += ...;
    }

    DllMods = new ObservableCollection<ModConfiguration>(mods);
}
```

### 📝 Wnioski

**Wymaga rozszerzenia**:
1. Dodać pole `CompatibilityStatus` do `ModConfiguration` (lub wrapper)
2. Pobrać statusy kompatybilności z API
3. Zaktualizować UI (kolory, ikony, opisy)

---

## 📊 Podsumowanie - Co Działa, Co Wymaga Zmian

### ✅ Gotowe do Użycia (bez zmian)

| Komponent | Status | Użycie |
|-----------|--------|--------|
| `ModConfiguration` | ✅ Gotowy | Model danych jest wystarczający |
| `ConfigManager.LoadConfig()` | ✅ Gotowy | Ładowanie najnowszych wersji modów |
| `DllModificationService.GetModsWithDllInstalled()` | ✅ Gotowy | Wykrywa lokalizacje DLL - kluczowe dla aktualizacji! |
| `DllModificationService.InstallDllToModAsync()` | ✅ Gotowy | Instalacja DLL - można użyć do aktualizacji |

### 🔨 Wymaga Małych Modyfikacji

| Komponent | Modyfikacja | Priorytet |
|-----------|-------------|-----------|
| `ModManager.ModifyAsync()` | Dodać parametr `specificVersionUrl?` | 🔴 Wysoki |
| `ModUpdateChecker.FindModsWithUpdates()` | Poprawić logikę dla DLL | 🟡 Średni |

### 🆕 Wymaga Nowych Komponentów

| Komponent | Opis | Priorytet |
|-----------|------|-----------|
| `ModVersionService` | Pobieranie historii wersji z `/susmodder-config-versions` | 🔴 Wysoki |
| `CompatibilityService` | Pobieranie statusów z `/api/compatibility` | 🔴 Wysoki |
| `DllUpdateManager` | Orkiestracja aktualizacji DLL w wielu lokalizacjach | 🔴 Wysoki |
| `VersionSelectionDialog` | UI wyboru wersji do instalacji | 🔴 Wysoki |
| `DllUpdateDialog` | UI wyboru lokalizacji do aktualizacji DLL | 🔴 Wysoki |

### 📋 Nowe Modele Danych

| Model | Opis | Priorytet |
|-------|------|-----------|
| `ModVersionHistory` | Reprezentuje wersję z historii | 🔴 Wysoki |
| `ModVersionsResponse` | Response z `/susmodder-config-versions` | 🔴 Wysoki |
| `CompatibilityInfo` | Informacja o kompatybilności DLL z FULL | 🔴 Wysoki |
| `CompatibilityResponse` | Response z `/api/compatibility` | 🔴 Wysoki |
| `DllUpdateInfo` | Info o dostępnej aktualizacji DLL z lokalizacjami | 🟡 Średni |

---

## 🎯 Kluczowe Odkrycia

### 1. DllModificationService.GetModsWithDllInstalled() jest ZŁOTEM! 💎

Ta metoda **rozwiązuje główny problem** z aktualizacjami DLL!

```csharp
// Dla DLL moda AleLuduMod (Id=5):
var locations = dllModService.GetModsWithDllInstalled(aleLuduMod, "steam");

// Zwraca listę modów FULL gdzie AleLuduMod jest zainstalowany:
// [
//   { Id=1, ModName="Town of Us", InstallPath="C:/Mody/TownOfUs" },
//   { Id=4, ModName="The Other Roles", InstallPath="C:/Mody/TOR" }
// ]

// Teraz możemy:
// 1. Pokazać użytkownikowi dialog z lokalizacjami
// 2. Dać wybór które zaktualizować
// 3. Zaktualizować w wybranych miejscach
```

**Nie musimy pisać nowej logiki wykrywania - już istnieje!**

### 2. ModUpdateChecker Prawie Wspiera DLL

Kod już iteruje po modach typu "dll" (linia 171), ale:
- ❌ Pomija DLL które nie mają `InstallPath`
- ❌ Nie wykrywa wielu lokalizacji

**Rozwiązanie**: Osobna metoda `CheckForDllUpdatesAsync()` używająca `GetModsWithDllInstalled()`

### 3. ModManager Potrzebuje Tylko 1 Parametru

Wystarczy dodać `string? specificVersionUrl = null` i gotowe!

### 4. UI (DllModSelectionViewModel) Wymaga Rozbudowy

Trzeba dodać:
- Pobieranie statusów kompatybilności
- Wizualne oznaczenia (kolory, ikony)
- Ostrzeżenia przed instalacją niekompatybilnych

---

## 🔄 Przepływy Danych - Obecnie

### Sprawdzanie Aktualizacji FULL

```
ModUpdateChecker.CheckForModUpdatesAsync()
    ↓
1. Pobierz remote config: GET /susmodder-config
    ↓
2. Porównaj z lokalnym config.json (ConfigManager.LoadConfig())
    ↓
3. Znajdź różnice w ModVersion (HasNewerVersion())
    ↓
4. Zaproponuj użytkownikowi aktualizację
    ↓
5. Jeśli TAK → ModUpdates.UpdateModAsync()
        ↓
    Usuń stary mod (ModDelete)
        ↓
    Zainstaluj nowy (ModManager.ModifyAsync())
```

### Instalacja DLL

```
DllModSelectionViewModel
    ↓
1. LoadDllMods() → pobiera listę DLL z config
    ↓
2. Użytkownik wybiera DLL
    ↓
3. InstallSelectedDllsAsync()
        ↓
    DllModificationService.InstallDllToModAsync(dll, targetMod, platform)
        ↓
    Pobiera plik z GitHubRepoOrLink/EpicGitHubRepoOrLink
        ↓
    Zapisuje do targetMod.InstallPath/BepInEx/plugins/
```

---

## 📌 Następne Kroki

Po przeczytaniu tej analizy, przejdź do:

1. **[01_ARCHITEKTURA_ROZWIAZANIA.md](./01_ARCHITEKTURA_ROZWIAZANIA.md)** - Jak zintegrować nowe API
2. **[02_MODELE_DANYCH.md](./02_MODELE_DANYCH.md)** - Nowe klasy C# do stworzenia

---

**Ostatnia aktualizacja:** 2025-10-22
**Wersja:** 1.0
