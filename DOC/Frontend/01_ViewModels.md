# Frontend – ViewModels (Warstwa prezentacji)

## Spis treści
1. [Wprowadzenie](#wprowadzenie)
2. [ViewModelBase](#viewmodelbase)
3. [Główne ViewModels](#główne-viewmodels)
4. [Helpers - Klasy pomocnicze UI](#helpers---klasy-pomocnicze-ui)
5. [Adaptery i pomocnicze klasy](#adaptery-i-pomocnicze-klasy)
6. [Wzorce i best practices](#wzorce-i-best-practices)
7. [Refaktoryzacja 2025](#refaktoryzacja-2025)

---

## Wprowadzenie

ViewModels w aplikacji SUSModder stanowią warstwę **logiki prezentacji** w architekturze MVVM. Dziedziczą po `ViewModelBase` (który implementuje `ReactiveObject` z ReactiveUI) i zapewniają:

- **Wiązanie danych** (data binding) między View a logiką biznesową
- **Komendy** (`ReactiveCommand`) obsługujące akcje użytkownika
- **Reaktywne właściwości** z automatyczną notyfikacją o zmianach (`RaiseAndSetIfChanged`)
- **Obserwowalne kolekcje** (`ObservableCollection<T>`) do dynamicznych list

> **Uwaga:** Po refaktoryzacji w 2025, MainWindowViewModel został podzielony na partial classes i helper classes dla lepszej organizacji kodu. Zobacz sekcję [Refaktoryzacja 2025](#refaktoryzacja-2025).

---

## ViewModelBase

**Lokalizacja:** `SUSModder/ViewModels/ViewModelBase.cs`

Bazowa klasa dla wszystkich ViewModels. Dziedziczy po `ReactiveObject` z ReactiveUI.

```csharp
namespace SUSModder.ViewModels
{
    public class ViewModelBase : ReactiveObject
    {
        // Brak dodatkowej implementacji - wykorzystuje ReactiveObject
    }
}
```

**Funkcje dostarczane przez `ReactiveObject`:**
- `RaiseAndSetIfChanged<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)`
- `RaisePropertyChanged(string propertyName)`
- Implementacja `INotifyPropertyChanged`

---

## Główne ViewModels

### 1. MainWindowViewModel 🌟

**Pliki:**
- `SUSModder/ViewModels/MainWindowViewModel.cs` (główny plik, partial class)
- `SUSModder/ViewModels/MainWindowViewModel.Helpers.cs` (metody pomocnicze)

> **Uwaga:** MainWindowViewModel jest teraz partial class. Metody pomocnicze zostały przeniesione do osobnych plików dla lepszej organizacji.  
**Rozmiar:** **3081 linii** (!) – główny ViewModel całej aplikacji  
**View:** `MainWindow.axaml`

#### Odpowiedzialności

MainWindowViewModel jest "mózgiem" aplikacji i zarządza:

1. **Listą modów** – `ObservableCollection<ModItem> Mods`
2. **Instalacją/usuwaniem modów** (full i DLL)
3. **Aktualizacjami** modów i aplikacji
4. **Auto-detekcją gry Among Us** (`GameLocator`)
5. **Zarządzaniem motywami UI** (Dark/Light/Pink)
6. **Widocznością paneli** (InfoPanel, AdditionalActionsPanel, DllModifications)
7. **Dodatkowymi akcjami ToU** (Fix Black Screen, Lobby Size, Config Management)
8. **Dialogami** (UpdateDialog, ErrorDialog, ConfirmDialog, etc.)

#### Kluczowe właściwości

```csharp
// Lista modów
public ObservableCollection<ModItem> Mods { get; }

// Wybrany mod
private ModItem? _selectedMod;
public ModItem? SelectedMod
{
    get => _selectedMod;
    set
    {
        this.RaiseAndSetIfChanged(ref _selectedMod, value);
        this.RaisePropertyChanged(nameof(IsModSelected));
        // ... inne notyfikacje
    }
}

// Stan UI
public bool IsPaneOpen { get; set; }
public bool IsInfoPanelVisible { get; set; }
public bool IsAdditionalActionsVisible { get; set; }
public bool IsDllModificationsVisible { get; set; }
public bool IsModPanelVisible => IsModSelected && !IsInfoPanelVisible && !IsAdditionalActionsVisible;

// Motyw
public enum ThemeType { Dark, Light, Pink }
public ThemeType CurrentTheme { get; set; }

// Wersja i tytuł
public string AppVersion { get; set; }
public string WindowTitle { get; set; }

// Tryb deweloperski
public bool IsDeveloperMode => DeveloperModeSettings.IsEnabled;

// Kolekcje DLL
public ObservableCollection<ModItem> DllMods { get; set; }
public ObservableCollection<ModItem> AvailableFullMods { get; set; }
public ObservableCollection<ModItem> ModsWithDllInstalled { get; set; }
public ObservableCollection<ModItem> ModsWithoutDllInstalled { get; set; }
```

#### Kluczowe komendy (ReactiveCommand)

```csharp
// Nawigacja i UI
public ReactiveCommand<Unit, Unit> TogglePaneCommand { get; }
public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }
public ReactiveCommand<Unit, Unit> ShowInfoCommand { get; }
public ReactiveCommand<Unit, Unit> ShowAdditionalActionsCommand { get; }

// Operacje na modach
public ReactiveCommand<Unit, Unit> InstallCommand { get; }
public ReactiveCommand<Unit, Unit> UninstallCommand { get; }
public ReactiveCommand<Unit, Unit> UpdateCommand { get; }
public ICommand ShowRolesCommand { get; }
public ICommand OpenFolderCommand { get; }
public ICommand CreateShortcutCommand { get; }

// Zarządzanie DLL
public ReactiveCommand<Unit, Unit> ShowDllModificationsCommand { get; }
public ReactiveCommand<Unit, Unit> ShowDllSelectionCommand { get; }
public ReactiveCommand<ModItem, Unit> SelectDllModCommand { get; }
public ReactiveCommand<ModItem, Unit> InstallDllToModCommand { get; }
public ReactiveCommand<ModItem, Unit> UninstallDllFromModCommand { get; }
public ReactiveCommand<Unit, Unit> CloseDllDialogCommand { get; }

// Dodatkowe akcje (ToU)
public ReactiveCommand<Unit, Unit> FixBlackScreenCommand { get; }
public ReactiveCommand<Unit, Unit> LaunchCommand { get; }
public ReactiveCommand<Unit, Unit> LobbySetCommand { get; }

// Okna konfiguracyjne
public ReactiveCommand<Unit, Unit> ShowAppSettingsCommand { get; }
public ReactiveCommand<Unit, Unit> ShowSUStatsConfigCommand { get; }
public ReactiveCommand<Unit, Unit> ShowRecommendedDiscordsCommand { get; }

// Inne
public ReactiveCommand<Unit, Unit> OpenDonationPageCommand { get; }
```

#### Inicjalizacja (konstruktor)

```csharp
public MainWindowViewModel()
{
    // 1. Inicjalizacja serwisów
    _touConfigService = new ToUConfigService();
    _userInteractionService = new UserInteractionService(/* delegates */);
    _dllModificationService = new DllModificationService(configService, diagnosticsOutput);
    
    // 2. Inicjalizacja ConfigRepository i ModConfigHandler
    var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
    var configRepository = new ConfigRepository(exeDir);
    ModConfigHandler.Initialize(configRepository, _userInteractionService);
    
    // 3. Utworzenie komend (ReactiveCommand.Create / CreateFromTask)
    InstallCommand = ReactiveCommand.Create(Install);
    UninstallCommand = ReactiveCommand.Create(Uninstall);
    // ... wszystkie pozostałe komendy
    
    // 4. Subskrypcje błędów komend
    FixBlackScreenCommand.ThrownExceptions.Subscribe(HandleCommandError);
    LobbySetCommand.ThrownExceptions.Subscribe(HandleCommandError);
    
    // 5. Preload ikon Discord (asynchronicznie w tle)
    _ = Task.Run(async () =>
    {
        await DiscordIconPreloader.PreloadDiscordIconsAsync();
    });
    
    // 6. Inicjalizacja aplikacji
    ClearEpicLogsOnStartup();
    LoadSavedTheme();
    InitializeApplicationAsync(); // Kluczowa metoda!
    LoadAppVersion();
    LoadWindowTitle();
    CheckForAppUpdatesOnStartup();
    ApplyTheme(CurrentTheme);
    
    // 7. Subskrypcja do zmian trybu gry
    AppSettingsViewModel.GameModeChanged += LoadWindowTitle;
}
```

#### Metoda `InitializeApplicationAsync()` – Przepływ startowy

```csharp
private async void InitializeApplicationAsync()
{
    try
    {
        // 1. Wczytanie konfiguracji modów z pliku config.json (lub API jako fallback)
        await LoadConfigurationsAsync();
        
        // 2. Auto-detekcja gry Among Us (Steam/Epic) i wpis Vanilla
        await CheckAndSetupVanillaModAsync();
        
        // 3. Sprawdzenie aktualizacji modów
        await CheckForModUpdatesAsync();
        
        // 4. Odświeżenie UI
        await RefreshModsAsync();
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Error initializing: {ex.Message}");
    }
}
```

#### Kluczowe metody

##### Operacje na modach
- `Install()` – instalacja wybranego moda (full)
- `Uninstall()` – usunięcie moda
- `Update()` – sprawdzenie i instalacja aktualizacji
- `ShowRoles()` – otworzenie okna ról (`RolesWindow`)
- `OpenFolder()` – otwarcie folderu instalacji moda w eksploratorze
- `CreateShortcut()` – utworzenie skrótu do moda na pulpicie

##### Zarządzanie DLL
- `ShowDllModifications()` – wyświetlenie panelu wyboru DLL
- `ShowDllSelectionCommand` – otworzenie okna `DllModSelectionView`
- `InstallDllToMod(ModItem mod)` – instalacja DLL do wybranego moda full
- `UninstallDllFromMod(ModItem mod)` – usunięcie DLL z moda full

##### Dodatkowe akcje (ToU)
- `ExecuteFixBlackScreenAsync()` – naprawa czarnego ekranu (usunięcie prefsetu regionów)
- `ShowLobbySetDialog()` – dialog ustawienia wielkości lobby
- `ShowAdditionalActions()` – pokazanie panelu `AdditionalActionsPanel`

##### Dialogi
- `ShowConfirmDialogAsync(title, message)` – dialog potwierdzenia (Tak/Nie)
- `ShowMessageAsync(title, message)` – dialog informacyjny (OK)
- `ShowErrorDialogAsync(message, title)` – dialog błędu
- `ShowPromptDialogAsync(title, message)` – dialog z polem tekstowym
- `ShowSelectFileDialogAsync(title, filters)` – dialog wyboru pliku

##### UI i motywy
- `ToggleTheme()` – przełączanie motywu (Dark → Light → Pink → Dark)
- `ApplyTheme(ThemeType theme)` – zastosowanie wybranego motywu
- `LoadSavedTheme()` – wczytanie zapisanego motywu z `appsettings.json`

##### Aktualizacje
- `CheckForModUpdatesAsync()` – sprawdzenie dostępnych aktualizacji modów
- `CheckForAppUpdatesOnStartup()` – sprawdzenie aktualizacji aplikacji przy starcie

#### Serwisy używane

- `ConfigService` – zarządzanie konfiguracjami modów
- `ModService` – instalacja/usuwanie modów
- `DllModificationService` – zarządzanie DLL
- `ToUConfigService` – konfiguracja Town of Us (presety, SUStats, AmongToken)
- `GameLocator` – auto-detekcja gry Among Us
- `AppUpdateService` – aktualizacje aplikacji
- `DiscordIconPreloader` – preloadowanie ikon serwerów Discord

#### Wzorce projektowe

- **MVVM** – separacja logiki prezentacji od widoku
- **Command Pattern** – `ReactiveCommand` do obsługi akcji
- **Observer Pattern** – `INotifyPropertyChanged`, `ObservableCollection<T>`
- **Async/Await** – wszystkie operacje I/O asynchroniczne
- **Dependency Injection (DI)** – przekazywanie serwisów przez konstruktor (częściowe)

---

### 2. DllModSelectionViewModel

**Plik:** `SUSModder/ViewModels/DllModSelectionViewModel.cs`  
**Rozmiar:** 338 linii  
**View:** `DllModSelectionView.axaml`

#### Odpowiedzialności

ViewModel do zarządzania instalacją/usuwaniem modów DLL do wybranego moda full.

#### Właściwości

```csharp
private ModConfiguration _targetMod; // Mod docelowy (full), do którego instalujemy DLL
public ObservableCollection<ModConfiguration> DllMods { get; set; } // Lista dostępnych DLL
public ObservableCollection<ModConfiguration> SelectedDllMods { get; set; } // Wybrane DLL do instalacji
public string Platform { get; set; } // "steam" lub "epic"

// Stan po instalacji
public bool IsInstallationComplete { get; set; }
public string InstallationSummary { get; set; }
```

#### Komendy

```csharp
public ReactiveCommand<Unit, Unit> InstallSelectedDllsCommand { get; } // Instalacja wybranych DLL
public ReactiveCommand<Unit, Unit> OkCommand { get; } // Potwierdzenie (powrót do listy)
public ReactiveCommand<Unit, Unit> CloseCommand { get; } // Zamknięcie okna
```

#### Konstruktor

```csharp
public DllModSelectionViewModel(
    DllModificationService dllModificationService, 
    ModConfiguration targetMod, 
    string platform = "steam")
{
    _dllModificationService = dllModificationService;
    _targetMod = targetMod;
    Platform = platform;
    
    // Debugowanie ścieżki dla Epic (naprawa pustych ścieżek)
    if (platform.Equals("epic", StringComparison.OrdinalIgnoreCase) 
        && string.IsNullOrEmpty(targetMod?.InstallPath))
    {
        string? epicPath = TryFindEpicInstallPath(targetMod?.ModName);
        if (!string.IsNullOrEmpty(epicPath))
        {
            _targetMod = new ModConfiguration { InstallPath = epicPath, /* ... */ };
        }
    }
    
    LoadDllMods();
    InstallSelectedDllsCommand = ReactiveCommand.CreateFromTask(async () =>
    {
        await InstallSelectedDllsAsync(Platform);
    });
}
```

#### Kluczowe metody

- `LoadDllMods()` – wczytanie listy dostępnych modyfikacji DLL z `config.json`
- `InstallSelectedDllsAsync(string platform)` – instalacja wybranych DLL do moda docelowego
- `TryFindEpicInstallPath(string? modName)` – automatyczne znalezienie ścieżki dla Epic Games

#### Przepływ

1. Użytkownik wybiera mod full i klika "Dodatkowe modyfikacje DLL"
2. `MainWindowViewModel.ShowDllSelectionCommand` otwiera okno `DllModSelectionView` z tym ViewModelem
3. Użytkownik zaznacza checkboxy przy wybranych DLL
4. Klika "Zainstaluj wybrane modyfikacje"
5. `InstallSelectedDllsAsync` instaluje DLL do folderu `BepInEx/plugins` w modzie docelowym
6. Wyświetla podsumowanie instalacji

---

### 3. AppSettingsViewModel

**Plik:** `SUSModder/ViewModels/AppSettingsViewModel.cs`  
**Rozmiar:** 582 linie  
**View:** `AppSettingsWindow.axaml`

#### Odpowiedzialności

Zarządzanie ustawieniami aplikacji:
- Ścieżka instalacji modów (`ModsInstallPath`)
- Tryb gry (`steam` / `epic`)
- Tryb deweloperski (włączenie/wyłączenie)
- Factory reset (przywrócenie domyślnych ustawień)

#### Właściwości

```csharp
public string ModsInstallPath { get; set; } // Ścieżka instalacji modów
public bool DeveloperMode { get; set; } // Tryb deweloperski
public string GameMode { get; set; } // "steam" lub "epic"

// Wykrywanie niezapisanych zmian
public bool HasUnsavedChanges { get; private set; }

// Oryginalne wartości (do wykrywania zmian)
private string _originalModsInstallPath;
private bool _originalDeveloperMode;
private string _originalGameMode;
```

#### Komendy

```csharp
public ReactiveCommand<Unit, Unit> BrowseFolderCommand { get; } // Wybór folderu instalacji
public ReactiveCommand<Unit, Unit> SaveCommand { get; } // Zapisanie ustawień
public ReactiveCommand<Unit, Unit> CancelCommand { get; } // Anulowanie zmian
public ReactiveCommand<Unit, Unit> ResetToDefaultCommand { get; } // Reset do domyślnych
public ReactiveCommand<Unit, Unit> FactoryResetCommand { get; } // Factory reset (usuwa config.json!)
```

#### Eventy

```csharp
public event Action? SettingsSaved; // Powiadomienie o zapisaniu ustawień
public static event Action? GameModeChanged; // Powiadomienie o zmianie trybu gry (static!)
```

#### Kluczowe metody

- `LoadCurrentSettings()` – wczytanie obecnych ustawień z `appsettings.json`
- `SaveSettings()` – zapisanie ustawień do `appsettings.json`
- `BrowseFolder()` – dialog wyboru folderu (Avalonia `IStorageProvider`)
- `FactoryResetAsync()` – usunięcie `config.json` i przywrócenie domyślnych ustawień

#### Przepływ

1. Użytkownik klika "Ustawienia" w `MainWindow`
2. `MainWindowViewModel.ShowAppSettings()` otwiera `AppSettingsWindow`
3. Użytkownik edytuje ustawienia
4. Klika "Zapisz" → `SaveSettings()` zapisuje do `appsettings.json`
5. Event `SettingsSaved` notyfikuje `MainWindow` o zmianach

---

### 4. SUStatsConfigViewModel

**Plik:** `SUSModder/ViewModels/SUStatsConfigViewModel.cs`  
**View:** `SUStatsConfigWindow.axaml`

#### Odpowiedzialności

Konfiguracja SUStats (systemu statystyk dla Town of Us):
- Wybór serwera SUStats
- Zarządzanie wieloma serwerami
- Wczytanie/zapis konfiguracji SUStats

#### Właściwości

```csharp
public ObservableCollection<ServerItem> Servers { get; } // Lista serwerów SUStats
public ServerItem? SelectedServer { get; set; } // Wybrany serwer (globalny stan)
public static bool HasSelectedServer => /* ... */; // Czy wybrano serwer (static)
```

#### Komendy

```csharp
public ReactiveCommand<Unit, Unit> AddServerCommand { get; }
public ReactiveCommand<ServerItem, Unit> RemoveServerCommand { get; }
public ReactiveCommand<Unit, Unit> SaveCommand { get; }
public ReactiveCommand<Unit, Unit> CancelCommand { get; }
```

#### Kluczowe metody

- `LoadServers()` – wczytanie listy serwerów z `config.json` (sekcja SUStats)
- `SaveServers()` – zapis listy serwerów
- `GetSelectedServerData()` – zwrócenie danych wybranego serwera (static)
- `ClearGlobalSelection()` – wyczyszczenie globalnego wyboru (static)

---

### 5. RecommendedDiscordsViewModel

**Plik:** `SUSModder/ViewModels/RecommendedDiscordsViewModel.cs`  
**View:** `RecommendedDiscordsWindow.axaml`

#### Odpowiedzialności

Wyświetlanie listy polecanych serwerów Discord (z API).

#### Właściwości

```csharp
public ObservableCollection<DiscordServerViewModel> DiscordServers { get; } // Lista serwerów
public bool IsLoading { get; set; } // Stan ładowania
```

#### Kluczowe metody

- `LoadDiscordServersAsync()` – pobranie listy serwerów z API lub z preloadera
- `GetPreloadedServers()` – użycie cache'owanych danych z `DiscordIconPreloader`

---

### 6. DiscordServerViewModel

**Plik:** `SUSModder/ViewModels/DiscordServerViewModel.cs`

#### Odpowiedzialności

Reprezentacja pojedynczego serwera Discord w UI.

#### Właściwości

```csharp
public string ServerName { get; }
public string? ServerIconUrl { get; }
public string? InviteUrl { get; }
public string? OwnerName { get; }
public string? OwnerAvatar { get; }
```

#### Komendy

```csharp
public ReactiveCommand<Unit, Unit> OpenInviteCommand { get; } // Otwarcie linku invite w przeglądarce
```

---

### 7. AmongTokenViewModel

**Plik:** `SUSModder/ViewModels/AmongTokenViewModel.cs`

#### Odpowiedzialności

Reprezentacja pojedynczego tokenu Among Us (w konfiguracji ToU).

#### Właściwości

```csharp
public string Token { get; }
public string FriendCode { get; }
public string TokenName { get; }
```

---

## Adaptery i pomocnicze klasy

### ModItem (quasi-ViewModel)

**Plik:** `SUSModder/ViewModels/ModItem.cs`  
**Rozmiar:** 153 linie

**Rola:** Adapter UI dla `ModConfiguration` (Core). Używany w kolekcjach `ObservableCollection<ModItem>`.

#### Właściwości

```csharp
public int Id { get; set; }
public string Name { get; set; } // Zmapowane z ModConfiguration.ModName
public string Description { get; set; }
public string PngFileName { get; set; }
public string ModVersion { get; set; }
public string AmongVersion { get; set; }
public string? InstallPath { get; set; }
public string GitHubRepoOrLink { get; set; }
public string? EpicGitHubRepoOrLink { get; set; }
public string ModType { get; set; } // "full" | "dll" | "Vanilla"
public string DllInstallPath { get; set; }
public DateTime? LastUpdated { get; set; }

// Właściwości dla UI instalacji
public bool IsInstalling { get; set; }
public int InstallProgress { get; set; } // 0-100
public string InstallStatusMessage { get; set; }
public bool ShowProgress { get; set; }

// Właściwości derived
public bool IsInstalled => !string.IsNullOrEmpty(InstallPath);
public bool CanInstall => !IsInstalling && !IsInstalled;
public bool CanUninstall => !IsInstalling && IsInstalled;
public string IconPath => $"avares://SUSModder/Assets/{PngFileName}";
```

---

### ModItemAdapter (konwersja)

**Plik:** `SUSModder/ViewModels/ModItemAdapter.cs`

**Rola:** Konwersja między `ModConfiguration` (Core) a `ModItem` (UI).

```csharp
public static ModItem FromConfig(ModConfiguration config) { /* ... */ }
public static ModConfiguration ToConfig(ModItem item) { /* ... */ }
```

**Użycie:**
```csharp
// Core → UI
ModItem uiMod = ModItemAdapter.FromConfig(coreConfig);

// UI → Core
ModConfiguration coreConfig = ModItemAdapter.ToConfig(uiMod);
```

---

### PresetFileItem

**Plik:** `SUSModder/ViewModels/PresetFileItem.cs`

**Rola:** Reprezentacja pliku presetu Town of Us (w dialogu zmiany nazw presetów).

```csharp
public string FileName { get; set; }
public string NewName { get; set; }
```

---

### SavedConfigItem

**Plik:** `SUSModder/ViewModels/SavedConfigItem.cs`

**Rola:** Reprezentacja zapisanej konfiguracji SUStats/AmongToken (w dialogu wczytywania konfiguracji z serwera).

```csharp
public string ConfigName { get; set; }
public string ConfigData { get; set; }
```

---

### EpicErrorDialogViewModel ⚠️

**Plik:** `SUSModder/ViewModels/FileName.cs` ⚠️ **BŁĘDNA NAZWA PLIKU**  
**Powinno być:** `EpicErrorDialogViewModel.cs`

**Rola:** ViewModel dla `EpicErrorDialog` – dialog błędu instalacji Epic z logiem.

#### Właściwości

```csharp
public string ModName { get; set; }
public string LogContent { get; set; }
```

#### Komendy

```csharp
public ReactiveCommand<Unit, Unit> CopyLogCommand { get; } // Kopiowanie logu do schowka
public ReactiveCommand<Unit, Unit> CloseCommand { get; } // Zamknięcie dialogu
```

---

## Wzorce i best practices

### 1. ReactiveUI – właściwości reaktywne

```csharp
private string _myProperty;
public string MyProperty
{
    get => _myProperty;
    set => this.RaiseAndSetIfChanged(ref _myProperty, value);
}
```

**Efekt:** Automatyczna notyfikacja UI o zmianie wartości.

### 2. ReactiveCommand – komendy asynchroniczne

```csharp
// Synchroniczna
MyCommand = ReactiveCommand.Create(MyMethod);

// Asynchroniczna
MyAsyncCommand = ReactiveCommand.CreateFromTask(MyAsyncMethod);

// Z parametrem
MyParamCommand = ReactiveCommand.Create<ModItem>(MyMethodWithParam);
```

**Efekt:** UI automatycznie obsługuje stan "Executing" (np. przycisk disabled podczas wykonywania).

### 3. Obserwowalne kolekcje

```csharp
public ObservableCollection<ModItem> Mods { get; } = new();
```

**Efekt:** UI automatycznie aktualizuje listę po `Add()`, `Remove()`, `Clear()`.

### 4. Obsługa błędów komend

```csharp
MyCommand.ThrownExceptions.Subscribe(ex =>
{
    // Obsługa błędu
    Dispatcher.UIThread.InvokeAsync(async () =>
    {
        await ShowErrorDialogAsync($"Błąd: {ex.Message}", "Błąd");
    });
});
```

### 5. Async/Await w UI

```csharp
private async Task MyLongOperationAsync()
{
    // Długa operacja (pobieranie z API, I/O)
    var result = await SomeServiceAsync();
    
    // Aktualizacja UI - automatycznie na UI thread (dzięki ReactiveUI)
    MyProperty = result;
}
```

### 6. Derived properties (właściwości wyliczane)

```csharp
public bool IsModSelected => SelectedMod != null;
public bool IsModPanelVisible => IsModSelected && !IsInfoPanelVisible;
```

**Pamiętaj:** Zgłoś zmianę po zmianie zależnych właściwości!

```csharp
public ModItem? SelectedMod
{
    get => _selectedMod;
    set
    {
        this.RaiseAndSetIfChanged(ref _selectedMod, value);
        this.RaisePropertyChanged(nameof(IsModSelected)); // ⭐
        this.RaisePropertyChanged(nameof(IsModPanelVisible)); // ⭐
    }
}
```

### 7. Eventy dla komunikacji między ViewModels

```csharp
// ViewModel A
public event Action? SomethingChanged;

// ViewModel B (subskrybent)
viewModelA.SomethingChanged += OnSomethingChanged;
```

**Przykład:** `AppSettingsViewModel.GameModeChanged` → `MainWindowViewModel.LoadWindowTitle`

---

## Statystyki ViewModels

> **Uwaga:** Po refaktoryzacji w 2025, rozmiary niektórych ViewModels uległy zmniejszeniu.

| ViewModel | Rozmiar | Główne odpowiedzialności |
|-----------|---------|---------------------------|
| **MainWindowViewModel** | 2405 linii | Główny ViewModel, zarządza całą aplikacją (partial class) |
| **MainWindowViewModel.Helpers** | 144 linie | Metody pomocnicze (DeterminePlatform, RefreshMods) |
| **DllModSelectionViewModel** | 338 linii | Wybór i instalacja DLL do modów full |
| **AppSettingsViewModel** | 582 linie | Ustawienia aplikacji (ścieżki, tryb gry, dev mode) |
| **SUStatsConfigViewModel** | ? linii | Konfiguracja SUStats (serwery) |
| **RecommendedDiscordsViewModel** | ? linii | Lista polecanych serwerów Discord |
| **DiscordServerViewModel** | ~50 linii | Pojedynczy serwer Discord |
| **AmongTokenViewModel** | ~30 linii | Token Among Us |
| **EpicErrorDialogViewModel** | ~71 linii | Dialog błędu Epic z logiem |
| **ModItem** | 153 linie | Adapter UI dla ModConfiguration |
| **ModItemAdapter** | ~50 linii | Konwersja ModConfiguration ↔ ModItem |
| **PresetFileItem** | ~30 linii | Plik presetu ToU |
| **SavedConfigItem** | ~30 linii | Zapisana konfiguracja |
| **ViewModelBase** | ~10 linii | Bazowa klasa (ReactiveObject) |

**Helpers (nowy folder):**
- **UIProgressReporter** | ~27 linii | Reporter postępu dla UI thread
- **UIDiagnosticsOutput** | ~23 linie | Wyjście diagnostyczne dla UI
- **SilentUserInteractionWrapper** | ~35 linii | Wrapper z pomijaniem info messages
- **EpicUserInteractionAdapter** | ~30 linii | Adapter dla Epic operations

---

## Refaktoryzacja 2025

### Zmiany w strukturze MainWindowViewModel

**Problem:** MainWindowViewModel miał 3081 linii, co utrudniało nawigację i utrzymanie kodu.

**Rozwiązanie:**
1. ✅ Przekształcenie w **partial class**
2. ✅ Wydzielenie **ViewModels/Helpers/** folder dla klas pomocniczych UI
3. ✅ Utworzenie **MainWindowViewModel.Helpers.cs** dla metod pomocniczych
4. ✅ Usunięcie ~676 linii duplikatów

**Rezultat:** Redukcja z 3081 → 2405 linii (22% zmniejszenie)

### Nowa struktura

```
ViewModels/
├── MainWindowViewModel.cs (2405 linii, partial)
├── MainWindowViewModel.Helpers.cs (144 linie)
├── Helpers/
│   ├── UIProgressReporter.cs
│   ├── UIDiagnosticsOutput.cs
│   ├── SilentUserInteractionWrapper.cs
│   └── EpicUserInteractionAdapter.cs
└── [pozostałe ViewModels...]
```

### Metody w MainWindowViewModel.Helpers.cs

#### `DeterminePlatform(): string`
Określa platformę gry (Steam/Epic) na podstawie konfiguracji Among Us.

```csharp
public string DeterminePlatform()
{
    // Sprawdza ścieżkę instalacji Among Us
    // Zwraca "epic" lub "steam"
}
```

#### `RefreshModsSortingKeepSelection(ModItem)`
Odświeża listę modów z zachowaniem bieżącego wyboru.

```csharp
private void RefreshModsSortingKeepSelection(ModItem selectedMod)
{
    // Sortowanie: Vanilla → zainstalowane → niezainstalowane
    // Przywraca wybór użytkownika
}
```

#### `RefreshModsListAsync(): Task`
Asynchronicznie odświeża listę modów z konfiguracji.

```csharp
private async Task RefreshModsListAsync()
{
    // Ładuje konfigurację
    // Używa ModItemAdapter.FromConfig()
    // Aktualizuje UI przez Dispatcher
}
```

#### `DebugDiagnosticsOutput` (helper class)
Wewnętrzna klasa do diagnostyki operacji aktualizacji modów.

### Helpers - Klasy pomocnicze UI

Szczegóły w sekcji [Helpers - Klasy pomocnicze UI](#helpers---klasy-pomocnicze-ui) poniżej.

---

## Helpers - Klasy pomocnicze UI

**Lokalizacja:** `SUSModder/ViewModels/Helpers/`

Folder zawiera klasy pomocnicze używane przez ViewModels do komunikacji z warstwą Core i operacji UI.

### 1. UIProgressReporter

**Plik:** `Helpers/UIProgressReporter.cs`  
**Interfejs:** `IProgressReporter` (z Core.Utilities)

Reporter postępu przekazujący aktualizacje do UI thread przez Dispatcher.

```csharp
public class UIProgressReporter : IProgressReporter
{
    private readonly Action<int, string> _progressCallback;
    
    public void Report(int percentage, string? message = null)
    {
        var safeMessage = message ?? "Przetwarzanie...";
        Dispatcher.UIThread.InvokeAsync(() => 
            _progressCallback(percentage, safeMessage));
    }
}
```

**Użycie:**
```csharp
var progressReporter = new UIProgressReporter((progress, msg) => {
    modItem.InstallProgress = progress;
    modItem.InstallStatusMessage = msg;
});
```

---

### 2. UIDiagnosticsOutput

**Plik:** `Helpers/UIDiagnosticsOutput.cs`  
**Interfejs:** `IDiagnosticsOutput` (z Core.Diagnostics)

Wyjście diagnostyczne przekazujące komunikaty debug do UI thread.

```csharp
public class UIDiagnosticsOutput : IDiagnosticsOutput
{
    private readonly Action<string> _messageCallback;
    
    public void Write(string message)
    {
        Dispatcher.UIThread.InvokeAsync(() => 
            _messageCallback(message));
    }
}
```

**Użycie:**
```csharp
var diagnosticsOutput = new UIDiagnosticsOutput((message) => {
    System.Diagnostics.Debug.WriteLine($"[Install] {message}");
});
```

---

### 3. SilentUserInteractionWrapper

**Plik:** `Helpers/SilentUserInteractionWrapper.cs`  
**Interfejs:** `IUserInteraction` (z Core.Utilities)

Wrapper dla `UserInteractionService` - pomija niektóre komunikaty informacyjne (loguje je tylko do debug).

```csharp
public class SilentUserInteractionWrapper : IUserInteraction
{
    private readonly UserInteractionService _inner;
    
    public void ShowInfo(string message, string title = "")
    {
        // Pomija UI dialog, tylko loguje
        System.Diagnostics.Debug.WriteLine($"[Silent] Info: {message}");
    }
    
    // Pozostałe metody delegują do _inner
}
```

**Użycie:** Podczas operacji które nie powinny przerywać użytkownika wieloma dialogami.

---

### 4. EpicUserInteractionAdapter

**Plik:** `Helpers/EpicUserInteractionAdapter.cs`  
**Interfejs:** `IEpicUserInteraction` (z Core.GameIntegration)

Adapter dla interakcji użytkownika w kontekście operacji Epic Games.

```csharp
public class EpicUserInteractionAdapter : IEpicUserInteraction
{
    public bool Confirm(string message)
    {
        // Auto-confirm dla operacji Epic
        System.Diagnostics.Debug.WriteLine($"[Epic] Auto-confirm: {message}");
        return true;
    }
    
    public void ShowError(string message)
    {
        // Loguje błędy Epic
        System.Diagnostics.Debug.WriteLine($"[Epic] Error: {message}");
    }
}
```

**Użycie:** Przekazywany do `EpicVersionManager` podczas instalacji modów Epic.

---

## Problemy do naprawy

Zobacz [REFACTOR.md](REFACTOR.md) dla szczegółów:

1. **FileName.cs** → powinno być `EpicErrorDialogViewModel.cs` ⚠️
2. **Duplikat `InstallationSilentUserInteraction`** – na końcu `MainWindowViewModel.cs` (linia ~2980) ⚠️

---

**Autor dokumentacji:** AI Assistant  
**Data utworzenia:** 2025-10-19  
**Status:** Wersja robocza
