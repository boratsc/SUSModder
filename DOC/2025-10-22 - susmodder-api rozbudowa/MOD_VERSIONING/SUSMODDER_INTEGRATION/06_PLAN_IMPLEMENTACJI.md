# Plan Implementacji - Roadmap Krok po Kroku

## 🎯 Cel Dokumentu

Szczegółowy plan implementacji integracji nowych systemów z podziałem na etapy, priorytety i szacowany czas.

---

## 📊 Podsumowanie Projektu

### Zakres Prac

| Kategoria | Zadania | Szacowany Czas |
|-----------|---------|----------------|
| **🔴 Installation Map System** | Trwała mapa instalacji modów | 11 godzin |
| **Modele Danych** | 7 nowych klas (wersjonowanie + kompatybilność) | 2 godziny |
| **Nowe Serwisy** | 3 serwisy (ModVersionService, CompatibilityService, DllUpdateManager) | 8 godzin |
| **Modyfikacje Istniejących** | 2 komponenty (ModManager, ModUpdateChecker) | 4 godziny |
| **UI/ViewModels** | 3 dialogi + rozszerzenia | 8 godzin |
| **Testy** | Unit tests + integracyjne | 6 godzin |
| **Dokumentacja i QA** | Dokumentacja kodu + testy manualne | 4 godziny |
| **RAZEM** | | **43 godziny (5.5 dni roboczych)** |

### Priorytety

🔴 **P0 - Must Have (Fundament)**
- **Installation Map System** - NAJWYŻSZY PRIORYTET (bez tego reszta jest niestabilna!)

🔴 **P1 - Must Have (MVP)**
- Feature 1: Instalacja starszych wersji modów
- Feature 2: Automatyczne aktualizacje DLL

🟡 **P2 - Should Have**
- Feature 3: System kompatybilności (wersja podstawowa)

🟢 **P2 - Nice to Have**
- Feature 3: System kompatybilności (UI rozszerzone z kolorami, ostrzeżeniami)
- Cache'owanie wyników API
- Zaawansowane raporty aktualizacji

---

## 🚀 Fazy Implementacji

---

## ⚠️ FAZA 0: Installation Map System (11h) - ✅ **UKOŃCZONA**

### ✅ Cel
Stworzyć trwały system śledzenia zainstalowanych modów, niezależny od config.json.

**Priorytet**: 🔴 **P0 - FUNDAMENTALNY** (bez tego reszta jest niestabilna!)

**Status**: ✅ **UKOŃCZONA** (2025-10-22)

**Szczegółowa dokumentacja**: Zobacz **[07_INSTALLATION_MAP_SYSTEM.md](./07_INSTALLATION_MAP_SYSTEM.md)**

### Zadania Fazy 0

#### 0.1 Modele (1h) - ✅ UKOŃCZONE
- ✅ `InstallationMap.cs`
- ✅ `FullModInstallation.cs`
- ✅ `DllModInstallation.cs`
- ✅ `InstallationMetadata.cs`

#### 0.2 InstallationMapManager - Core (2h) - ✅ UKOŃCZONE
- ✅ `SaveInstallationMapAsync()`
- ✅ `LoadInstallationMapAsync()`
- ✅ `InstallationMapExists()`
- ✅ `DiscoverInstalledModsAsync()`
- ✅ `ImportDiscoveredMods()`
- ✅ `MigrateExistingInstallationsAsync()`
- ✅ `ValidateAndCleanInstalledMods()`

#### 0.3 Integracja z Instalacją FULL (3h) - ✅ UKOŃCZONE
- ✅ ModManager.InstallSteamAsync - tworzenie mapy
- ✅ EpicVersionManager.ModifyEpicAsync - tworzenie mapy
- ✅ Testy manualne: Pliki `.susmodder-install.json` są tworzone poprawnie

#### 0.4 Integracja z DLL (2h) - ✅ UKOŃCZONE
- ✅ DllModificationService.InstallDllToModAsync - aktualizacja mapy
- ✅ DllModificationService.UninstallDllFromModAsync - aktualizacja mapy
- ✅ Testy manualne: DLL są śledzone w InstallationMap

#### 0.5 Odkrywanie i Import (2h) - ✅ UKOŃCZONE
- ✅ `DiscoverInstalledModsAsync()` - skanuje katalogi
- ✅ `ImportDiscoveredMods()` - importuje do config.json
- ✅ Wywołanie w MainWindowViewModel.Initialization

#### 0.6 Migracja Istniejących (1h) - ✅ UKOŃCZONE
- ✅ `MigrateExistingInstallationsAsync()` - tworzy mapy dla istniejących modów
- ✅ Hook w MainWindowViewModel przy starcie aplikacji

### ✅ Rezultat Fazy 0
✅ Każdy mod ma trwałą mapę instalacji  
✅ System jest odporny na utratę config.json  
✅ Możliwe jest automatyczne odkrycie zainstalowanych modów  
✅ DLL mody są śledzone w poszczególnych lokalizacjach  
✅ **TESTY MANUALNE PRZESZŁY POMYŚLNIE**

---

## FAZA 1: Przygotowanie Modeli dla Wersjonowania/Kompatybilności (2h) - ✅ **UKOŃCZONA**

### ✅ Cel
Przygotować fundament - modele danych i strukturę projektu.

**Status**: ✅ **UKOŃCZONA** (2025-10-22)

### Zadania

#### 1.1 Modele Danych - ✅ UKOŃCZONE
- ✅ `ModVersionHistory.cs` - historia wersji moda
- ✅ `ModVersionsResponse.cs` - response z API
- ✅ `CompatibilityStatus.cs` - enum + extension methods
- ✅ `CompatibilityInfo.cs` - szczegóły kompatybilności
- ✅ `CompatibilityResponse.cs` - response z API kompatybilności
- ✅ `DllUpdateInfo.cs` - info o aktualizacji DLL
- ✅ `DllUpdateResult.cs` - wynik aktualizacji DLL

#### 1.2 Kompilacja - ✅ SUKCES
- ✅ Wszystkie modele kompilują się poprawnie
- ✅ Brak błędów i ostrzeżeń

---

## FAZA 2: ModVersionService - Instalacja Starszych Wersji (4h) - ✅ **UKOŃCZONA**

### ✅ Cel
Umożliwić użytkownikom instalację starszych wersji modów.

**Priorytet**: 🔴 P1 (MVP)

**Status**: ✅ **UKOŃCZONA** (2025-10-22)

### Zadania

#### 2.1 Stwórz ModVersionService (1h 30min) - ✅ UKOŃCZONE
**Plik**: `SUSModder.Core/Services/ModVersionService.cs`

- ✅ `GetVersionHistoryAsync(modId)` - pobiera historię wersji z API
- ✅ `GetSpecificVersionAsync(modId, versionId)` - pobiera konkretną wersję
- ✅ `GetAvailableVersionsForUIAsync(modId)` - lista dla UI
- ✅ `IsNewerVersionAvailableAsync(modId, currentVersion)` - sprawdza dostępność nowszej wersji
- ✅ Cache 5-minutowy (MemoryCache)
- ✅ Obsługa błędów (timeout, HTTP errors, JSON parsing)
- ✅ Dodano pakiet: `Microsoft.Extensions.Caching.Memory` v9.0.10

#### 2.2 UI dla Wyboru Wersji (2h 30min) - ✅ UKOŃCZONE

**ViewModel**: `SUSModder/ViewModels/VersionSelectionViewModel.cs`
- ✅ Automatyczne ładowanie wersji przy otwarciu dialogu
- ✅ Obsługa stanów: Loading, Error, Success
- ✅ Wybór wersji z listy
- ✅ Komendy: ConfirmCommand, CancelCommand
- ✅ Eventy: VersionSelected, Cancelled

**View**: `SUSModder/Views/VersionSelectionDialog.axaml + .cs`
- ✅ Dialog z nagłówkiem (emoji 📦, nazwa moda, obecna wersja)
- ✅ Stan ładowania (⏳)
- ✅ Obsługa błędów (❌)
- ✅ Przewijalna lista wersji
- ✅ Klikalna lista (Tapped event)
- ✅ Responsywny layout (tekst nie nachodzi na przyciski)
- ✅ Info o wybranej wersji w stopce
- ✅ Przyciski: "Anuluj", "Instaluj wybraną wersję"

**Integracja**: `MainWindowViewModel.ModOperations.cs`
- ✅ Nowa komenda: `InstallWithVersionSelectionCommand`
- ✅ Metoda: `InstallWithVersionSelection()` - pokazuje dialog
- ✅ Metoda: `InstallSpecificVersionAsync()` - instaluje wybraną wersję
- ✅ Nadpisywanie GitHubRepoOrLink z wybranej wersji
- ✅ Obsługa Steam i Epic

**UI**: `MainWindow.axaml`
- ✅ Nowy przycisk: "📦 Wybierz wersję..." 
- ✅ Widoczny tylko dla niezainstalowanych modów (nie-Vanilla)
- ✅ Pod przyciskiem "Instaluj (najnowsza wersja)"

#### 2.3 Testy - ✅ SUKCES
- ✅ Kompilacja bez błędów
- ✅ Kompilacja bez ostrzeżeń
- ✅ Dialog wyświetla się poprawnie
- ✅ Layout responsywny (fix: tekst nie nachodzi na przyciski)

### ✅ Rezultat Fazy 2
✅ Użytkownicy mogą wybierać starsze wersje modów przed instalacją  
✅ Dialog pokazuje historię wersji z API  
✅ Instalacja wybranej wersji działa dla Steam i Epic  
✅ Cache optymalizuje zapytania do API  
✅ Pełna obsługa błędów i stanów UI  

---

## FAZA 3: CompatibilityService - System Kompatybilności (3h) - 🔄 **W TOKU**
    [Fact]
    public void ModVersionHistory_Deserializes_Correctly()
    {
        var json = @"{
            ""VersionId"": 2,
            ""ModId"": 1,
            ""ModVersion"": ""5.3.1"",
            ""AmongVersion"": ""2024.10.01"",
            ""GitHubRepoOrLink"": ""https://github.com/test"",
            ""CreatedAt"": ""2024-10-01T14:30:00Z""
        }";

        var version = JsonSerializer.Deserialize<ModVersionHistory>(json);

        Assert.NotNull(version);
        Assert.Equal(2, version.VersionId);
        Assert.Equal("5.3.1", version.ModVersion);
    }

    [Fact]
    public void ModVersionsResponse_Deserializes_Correctly()
    {
        var json = @"{
            ""success"": true,
            ""modId"": 1,
            ""count"": 1,
            ""versions"": [{
                ""VersionId"": 2,
                ""ModId"": 1,
                ""ModVersion"": ""5.3.1"",
                ""AmongVersion"": ""2024.10.01"",
                ""GitHubRepoOrLink"": ""https://github.com/test"",
                ""CreatedAt"": ""2024-10-01T14:30:00Z""
            }]
        }";

        var response = JsonSerializer.Deserialize<ModVersionsResponse>(json);

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal(1, response.Count);
        Assert.Single(response.Versions);
    }

    [Fact]
    public void CompatibilityStatus_Extensions_Work()
    {
        var status = CompatibilityStatusExtensions.FromApiCode("F");
        Assert.Equal(CompatibilityStatus.Favorite, status);
        Assert.Equal("F", status.ToApiCode());
        Assert.Contains("Polecany", status.GetDescription());
        Assert.Equal("🟢", status.GetEmoji());
    }
}
```

Uruchom testy:
```bash
dotnet test SUSModder.Core.Tests
```

**Expected Result**: Wszystkie testy przechodzą ✅

---

## FAZA 2: ModVersionService - Instalacja Starszych Wersji (4h)

### ✅ Cel
Umożliwić użytkownikom instalację starszych wersji modów.

**Priorytet**: 🔴 P0 (MVP)

### Zadania

#### 2.1 Stwórz ModVersionService (1h 30min)

**Plik**: `SUSModder.Core/Services/ModVersionService.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services
{
    public class ModVersionService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly IDiagnosticsOutput _log;
        private readonly string _apiBaseUrl;
        private static readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
        private const int CacheMinutes = 5;

        public ModVersionService(
            IConfiguration configuration,
            IDiagnosticsOutput log)
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _log = log;

            var baseUrl = configuration.GetSection("Configuration")["BaseUrl"]
                ?? "https://susmodder.app/";
            _apiBaseUrl = baseUrl.TrimEnd('/');
        }

        /// <summary>
        /// Pobierz historię wersji dla moda
        /// </summary>
        public async Task<List<ModVersionHistory>> GetVersionHistoryAsync(int modId)
        {
            string cacheKey = $"version_history_{modId}";

            // Sprawdź cache
            if (_cache.TryGetValue(cacheKey, out List<ModVersionHistory>? cached))
            {
                _log.Write($"[Cache HIT] Historia wersji dla moda {modId}");
                return cached!;
            }

            try
            {
                var url = $"{_apiBaseUrl}/api/susmodder-config-versions?modId={modId}";
                _log.Write($"[ModVersionService] Pobieranie historii z: {url}");

                // Dodaj token autoryzacji
                string token = SecretProvider.GetDownloadToken();
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", token);

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ModVersionsResponse>(json);

                if (result?.Success == true && result.Versions != null)
                {
                    _log.Write($"[ModVersionService] Pobrano {result.Count} wersji dla moda {modId}");

                    // Cache na 5 minut
                    _cache.Set(cacheKey, result.Versions, TimeSpan.FromMinutes(CacheMinutes));

                    return result.Versions;
                }

                _log.Write($"[ModVersionService] Brak wersji dla moda {modId}");
                return new List<ModVersionHistory>();
            }
            catch (HttpRequestException ex)
            {
                _log.Write($"[ERROR] Błąd HTTP: {ex.Message}");
                throw new Exception($"Nie można pobrać historii wersji: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex)
            {
                _log.Write($"[ERROR] Timeout: {ex.Message}");
                throw new TimeoutException("Przekroczono czas oczekiwania na odpowiedź API", ex);
            }
            catch (Exception ex)
            {
                _log.Write($"[ERROR] Nieoczekiwany błąd: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Pobierz konkretną wersję moda
        /// </summary>
        public async Task<ModVersionHistory?> GetSpecificVersionAsync(int modId, int versionId)
        {
            var versions = await GetVersionHistoryAsync(modId);
            return versions.FirstOrDefault(v => v.VersionId == versionId);
        }

        /// <summary>
        /// Wyczyść cache (użyteczne po aktualizacji danych)
        /// </summary>
        public void ClearCache(int? modId = null)
        {
            if (modId.HasValue)
            {
                _cache.Remove($"version_history_{modId.Value}");
                _log.Write($"[Cache] Wyczyszczono cache dla moda {modId}");
            }
            // Uwaga: MemoryCache nie ma Clear(), trzeba usuwać poszczególne klucze
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
```

#### 2.2 Modyfikuj ModManager - Dodaj Parametr VersionUrl (30 min)

**Plik**: `SUSModder.Core/GameIntegration/ModManager.cs`

**Linia 33**: Dodaj parametr `specificVersionUrl`:

```csharp
public async Task ModifyAsync(
    ModConfiguration modConfig,
    List<ModConfiguration> modConfigs,
    IProgressReporter progress,
    IDiagnosticsOutput log,
    ModManagerUserCallbacks userCallbacks,
    string mode,
    string? specificVersionUrl = null)  // NOWY PARAMETR
{
    this.log = log;

    if (modConfig.ModType == "full")
    {
        if (mode == "steam")
        {
            await InstallSteamAsync(
                modConfig,
                modConfigs,
                progress,
                log,
                userCallbacks,
                specificVersionUrl);  // PRZEKAŻ DALEJ
        }
        else
        {
            // Epic...
        }
    }
}
```

**Linia 57**: Rozszerz `InstallSteamAsync`:

```csharp
private async Task InstallSteamAsync(
    ModConfiguration modConfig,
    List<ModConfiguration> modConfigs,
    IProgressReporter progress,
    IDiagnosticsOutput log,
    ModManagerUserCallbacks userCallbacks,
    string? specificVersionUrl = null)  // NOWY PARAMETR
{
    // ... (kod do linii 138)

    // Linia 138-140: ZMIEŃ
    string downloadUrl = !string.IsNullOrEmpty(specificVersionUrl)
        ? specificVersionUrl  // UŻYJ KONKRETNEGO URL JEŚLI PODANO
        : !string.IsNullOrEmpty(modConfig.GitHubRepoOrLink)
            ? modConfig.GitHubRepoOrLink
            : throw new InvalidOperationException("Brak linku do pobrania moda.");

    // ... (reszta bez zmian)
}
```

#### 2.3 Stwórz VersionSelectionDialog (1h)

**Plik**: `SUSModder/Views/VersionSelectionDialog.axaml`

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d" d:DesignWidth="500" d:DesignHeight="450"
        x:Class="SUSModder.Views.VersionSelectionDialog"
        Title="Wybierz wersję do zainstalowania"
        Width="500" Height="450"
        CanResize="False">

    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Nagłówek -->
        <TextBlock Grid.Row="0"
                   Text="Wybierz wersję do zainstalowania:"
                   FontSize="16"
                   FontWeight="Bold"
                   Margin="0,0,0,15"/>

        <!-- Lista wersji -->
        <Border Grid.Row="1"
                BorderBrush="Gray"
                BorderThickness="1"
                CornerRadius="5"
                Padding="10">
            <ListBox Items="{Binding Versions}"
                     SelectedItem="{Binding SelectedVersion}"
                     Background="Transparent">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Margin="5">
                            <TextBlock Text="{Binding DisplayText}"
                                       FontWeight="Bold"
                                       FontSize="14"/>
                            <TextBlock Text="{Binding Notes}"
                                       FontSize="12"
                                       Foreground="Gray"
                                       TextWrapping="Wrap"
                                       Margin="0,5,0,0"/>
                        </StackPanel>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
        </Border>

        <!-- Przyciski -->
        <StackPanel Grid.Row="2"
                    Orientation="Horizontal"
                    HorizontalAlignment="Right"
                    Margin="0,15,0,0"
                    Spacing="10">
            <Button Content="Anuluj"
                    Width="100"
                    Click="OnCancelClick"/>
            <Button Content="Instaluj"
                    Width="100"
                    Classes="accent"
                    Click="OnInstallClick"
                    IsEnabled="{Binding SelectedVersion, Converter={x:Static ObjectConverters.IsNotNull}}"/>
        </StackPanel>
    </Grid>
</Window>
```

**CodeBehind**: `SUSModder/Views/VersionSelectionDialog.axaml.cs`

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SUSModder.ViewModels;

namespace SUSModder.Views
{
    public partial class VersionSelectionDialog : Window
    {
        public VersionSelectionDialog()
        {
            InitializeComponent();
        }

        private void OnInstallClick(object? sender, RoutedEventArgs e)
        {
            var vm = DataContext as VersionSelectionDialogViewModel;
            Close(vm?.SelectedVersion);
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }
    }
}
```

**ViewModel**: `SUSModder/ViewModels/VersionSelectionDialogViewModel.cs`

```csharp
using System.Collections.Generic;
using ReactiveUI;
using SUSModder.Core.Models;

namespace SUSModder.ViewModels
{
    public class VersionSelectionDialogViewModel : ViewModelBase
    {
        private ModVersionHistory? _selectedVersion;

        public List<ModVersionHistory> Versions { get; }

        public ModVersionHistory? SelectedVersion
        {
            get => _selectedVersion;
            set => this.RaiseAndSetIfChanged(ref _selectedVersion, value);
        }

        public VersionSelectionDialogViewModel(List<ModVersionHistory> versions)
        {
            Versions = versions;
            // Domyślnie wybierz pierwszą (najnowszą)
            SelectedVersion = versions.FirstOrDefault();
        }
    }
}
```

#### 2.4 Integracja z MainWindowViewModel (1h)

**Plik**: `SUSModder/ViewModels/MainWindowViewModel.cs`

Dodaj pole:
```csharp
private ModVersionService? _modVersionService;
```

W konstruktorze:
```csharp
_modVersionService = new ModVersionService(_configuration, _log);
```

Dodaj metodę:
```csharp
private async Task ShowVersionSelectionDialogAsync(ModConfiguration mod)
{
    try
    {
        _log.Write($"[VersionSelection] Pobieranie historii dla {mod.ModName}");

        var versions = await _modVersionService!.GetVersionHistoryAsync(mod.Id);

        if (!versions.Any())
        {
            await ShowErrorAsync("Brak dostępnych starszych wersji dla tego moda");
            return;
        }

        var dialog = new VersionSelectionDialog
        {
            DataContext = new VersionSelectionDialogViewModel(versions)
        };

        var selectedVersion = await dialog.ShowDialog<ModVersionHistory?>(GetWindow());

        if (selectedVersion != null)
        {
            await InstallSpecificVersionAsync(mod, selectedVersion);
        }
    }
    catch (Exception ex)
    {
        _log.Write($"[ERROR] Błąd wyboru wersji: {ex.Message}");
        await ShowErrorAsync($"Nie można pobrać historii wersji:\n{ex.Message}");
    }
}

private async Task InstallSpecificVersionAsync(
    ModConfiguration mod,
    ModVersionHistory selectedVersion)
{
    try
    {
        _progressReporter.Report(0, "Rozpoczynanie instalacji wybranej wersji...");

        string downloadUrl = _mode == "steam"
            ? selectedVersion.GitHubRepoOrLink ?? throw new Exception("Brak linku Steam")
            : selectedVersion.EpicGitHubRepoOrLink ?? selectedVersion.GitHubRepoOrLink
                ?? throw new Exception("Brak linku Epic");

        await _modManager!.ModifyAsync(
            mod,
            ModConfigs.ToList(),
            _progressReporter,
            _log,
            new ModManagerUserCallbacks
            {
                ConfirmAsync = ConfirmAsync,
                ShowErrorAsync = ShowErrorAsync,
                ShowInfoAsync = ShowInfoAsync
            },
            _mode,
            specificVersionUrl: downloadUrl
        );

        // Aktualizuj config
        mod.ModVersion = selectedVersion.ModVersion;
        mod.AmongVersion = selectedVersion.AmongVersion;
        ConfigManager.SaveConfig(ModConfigs.ToList());

        _progressReporter.Report(100, "Instalacja zakończona");
        await ShowInfoAsync($"Zainstalowano {mod.ModName} wersji {selectedVersion.ModVersion}");
    }
    catch (Exception ex)
    {
        _log.Write($"[ERROR] Błąd instalacji wersji: {ex.Message}");
        await ShowErrorAsync($"Nie udało się zainstalować wybranej wersji:\n{ex.Message}");
    }
}
```

Dodaj do FAB menu (w odpowiednim miejscu):
```csharp
fabMenu.AddItem("Zainstaluj starszą wersję", async () =>
{
    await ShowVersionSelectionDialogAsync(mod);
});
```

### ✅ Testy Fazy 2

1. **Unit Test**: ModVersionService pobiera dane
2. **Integracja**: Dialog pokazuje listę wersji
3. **E2E**: Instalacja starszej wersji kończy się sukcesem

---

## FAZA 3: DllUpdateManager - Aktualizacje DLL (6h)

### ✅ Cel
Automatyczne wykrywanie i aktualizacja modów DLL w wielu lokalizacjach.

**Priorytet**: 🔴 P0 (MVP)

### Zadania

#### 3.1 Stwórz DllUpdateManager (2h)

**Plik**: `SUSModder.Core/Services/DllUpdateManager.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;

namespace SUSModder.Core.Services
{
    public class DllUpdateManager
    {
        private readonly DllModificationService _dllModService;
        private readonly ConfigService _configService;
        private readonly IDiagnosticsOutput _log;

        public DllUpdateManager(
            DllModificationService dllModService,
            ConfigService configService,
            IDiagnosticsOutput log)
        {
            _dllModService = dllModService;
            _configService = configService;
            _log = log;
        }

        /// <summary>
        /// Sprawdź dostępne aktualizacje modów DLL
        /// </summary>
        public async Task<List<DllUpdateInfo>> CheckDllUpdatesAsync(string platform)
        {
            var updatesList = new List<DllUpdateInfo>();

            try
            {
                _log.Write("[DllUpdateManager] Sprawdzanie aktualizacji DLL...");

                // 1. Pobierz najnowsze wersje z API
                var remoteConfigs = await _configService.FetchRemoteConfigAsync();

                // 2. Pobierz lokalne konfiguracje
                var localConfigs = _configService.LoadConfig();

                // 3. Filtruj tylko mody DLL
                var remoteDlls = remoteConfigs
                    .Where(m => m.ModType.Equals("dll", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                _log.Write($"[DllUpdateManager] Sprawdzam {remoteDlls.Count} modów DLL");

                foreach (var remoteDll in remoteDlls)
                {
                    // Znajdź lokalną wersję DLL
                    var localDll = localConfigs
                        .FirstOrDefault(m => m.Id == remoteDll.Id);

                    if (localDll == null)
                    {
                        _log.Write($"[DllUpdateManager] {remoteDll.ModName} nie jest zainstalowany lokalnie");
                        continue;
                    }

                    // Sprawdź czy jest nowsza wersja
                    bool hasUpdate = !string.IsNullOrEmpty(localDll.ModVersion) &&
                        !string.IsNullOrEmpty(remoteDll.ModVersion) &&
                        !localDll.ModVersion.Equals(remoteDll.ModVersion, StringComparison.OrdinalIgnoreCase);

                    if (hasUpdate)
                    {
                        _log.Write($"[DllUpdateManager] Znaleziono aktualizację: {remoteDll.ModName} {localDll.ModVersion} → {remoteDll.ModVersion}");

                        // Znajdź gdzie DLL jest zainstalowany
                        var locations = _dllModService.GetModsWithDllInstalled(remoteDll, platform);

                        if (locations.Any())
                        {
                            _log.Write($"[DllUpdateManager] {remoteDll.ModName} zainstalowany w {locations.Count} lokalizacjach");

                            updatesList.Add(new DllUpdateInfo
                            {
                                DllMod = remoteDll,
                                CurrentVersion = localDll.ModVersion,
                                NewVersion = remoteDll.ModVersion,
                                InstallLocations = locations,
                                SelectedLocations = locations.ToList() // Domyślnie wszystkie
                            });
                        }
                        else
                        {
                            _log.Write($"[DllUpdateManager] {remoteDll.ModName} nie jest zainstalowany w żadnym modzie FULL");
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

        /// <summary>
        /// Zaktualizuj DLL w wybranych lokalizacjach
        /// </summary>
        public async Task<DllUpdateResult> UpdateDllInLocationsAsync(
            DllUpdateInfo updateInfo,
            string platform)
        {
            var result = new DllUpdateResult
            {
                DllName = updateInfo.DllMod.ModName,
                TotalLocations = updateInfo.SelectedLocations.Count
            };

            _log.Write($"[DllUpdateManager] Aktualizowanie {result.DllName} w {result.TotalLocations} lokalizacjach");

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
                        _log.Write($"[DllUpdate] ✓ Zaktualizowano w {fullMod.ModName}");
                    }
                    else
                    {
                        result.FailedUpdates++;
                        result.FailedLocations.Add(fullMod.ModName);
                        _log.Write($"[DllUpdate] ✗ Nie udało się zaktualizować w {fullMod.ModName}");
                    }
                }
                catch (Exception ex)
                {
                    _log.Write($"[ERROR] Błąd aktualizacji w {fullMod.ModName}: {ex.Message}");
                    result.FailedUpdates++;
                    result.FailedLocations.Add(fullMod.ModName);
                }
            }

            _log.Write($"[DllUpdateManager] Aktualizacja zakończona: {result.SuccessfulUpdates}/{result.TotalLocations} udanych");
            return result;
        }
    }
}
```

#### 3.2 Rozszerz ModUpdateChecker (1h)

**Plik**: `SUSModder.Core/GameIntegration/ModUpdateChecker.cs`

Dodaj metodę:

```csharp
/// <summary>
/// Sprawdź aktualizacje modów DLL
/// </summary>
public static async Task<List<DllUpdateInfo>> CheckForDllUpdatesAsync(
    IConfiguration configuration,
    IDiagnosticsOutput log,
    string platform)
{
    try
    {
        log.Write("[ModUpdateChecker] Sprawdzanie aktualizacji DLL...");

        var configService = new ConfigService(configuration);
        var dllModService = new DllModificationService(configService, log);
        var dllUpdateManager = new DllUpdateManager(dllModService, configService, log);

        return await dllUpdateManager.CheckDllUpdatesAsync(platform);
    }
    catch (Exception ex)
    {
        log.Write($"[ERROR] Błąd sprawdzania aktualizacji DLL: {ex.Message}");
        return new List<DllUpdateInfo>();
    }
}
```

#### 3.3 Stwórz DllUpdateDialog (2h)

**Plik**: `SUSModder/Views/DllUpdateDialog.axaml`

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="SUSModder.Views.DllUpdateDialog"
        Title="Dostępne aktualizacje modów DLL"
        Width="600" Height="500">

    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Nagłówek -->
        <TextBlock Grid.Row="0"
                   Text="Dostępne aktualizacje modów DLL"
                   FontSize="18"
                   FontWeight="Bold"
                   Margin="0,0,0,15"/>

        <!-- Lista aktualizacji -->
        <ScrollViewer Grid.Row="1">
            <ItemsControl Items="{Binding Updates}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border BorderBrush="Gray"
                                BorderThickness="1"
                                CornerRadius="5"
                                Padding="15"
                                Margin="0,0,0,10">
                            <StackPanel>
                                <!-- Nagłówek aktualizacji -->
                                <TextBlock Text="{Binding VersionChangeText}"
                                           FontSize="16"
                                           FontWeight="Bold"/>

                                <!-- Separator -->
                                <Border Height="1"
                                        Background="LightGray"
                                        Margin="0,10,0,10"/>

                                <!-- Lokalizacje -->
                                <TextBlock Text="Zainstalowany w:"
                                           FontSize="14"
                                           Margin="0,0,0,5"/>

                                <ItemsControl Items="{Binding InstallLocations}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <CheckBox Content="{Binding ModName}"
                                                      IsChecked="True"
                                                      Margin="20,3,0,3"/>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>

        <!-- Przyciski -->
        <StackPanel Grid.Row="2"
                    Orientation="Horizontal"
                    HorizontalAlignment="Right"
                    Margin="0,15,0,0"
                    Spacing="10">
            <Button Content="Anuluj"
                    Width="120"
                    Click="OnCancelClick"/>
            <Button Content="Aktualizuj"
                    Width="120"
                    Classes="accent"
                    Click="OnUpdateClick"
                    IsEnabled="{Binding HasSelectedUpdates}"/>
        </StackPanel>
    </Grid>
</Window>
```

### ✅ Testy Fazy 3

1. **Unit Test**: DllUpdateManager wykrywa aktualizacje
2. **Integracja**: Dialog pokazuje poprawnie lokalizacje
3. **E2E**: Aktualizacja DLL w wielu miejscach działa

---

## FAZA 4: CompatibilityService - System Kompatybilności (4h)

### ✅ Cel
Pokazywać użytkownikom informacje o kompatybilności modów DLL z FULL.

**Priorytet**: 🟡 P1 (Should Have)

### Zadania

#### 4.1 Stwórz CompatibilityService (1h 30min)

**Plik**: `SUSModder.Core/Services/CompatibilityService.cs`

```csharp
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;

namespace SUSModder.Core.Services
{
    public class CompatibilityService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly IDiagnosticsOutput _log;
        private readonly string _apiBaseUrl;
        private static readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
        private const int CacheMinutes = 10;

        public CompatibilityService(
            IConfiguration configuration,
            IDiagnosticsOutput log)
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            _log = log;

            var baseUrl = configuration.GetSection("Configuration")["BaseUrl"]
                ?? "https://susmodder.app/";
            _apiBaseUrl = baseUrl.TrimEnd('/');
        }

        /// <summary>
        /// Pobierz informacje o kompatybilności
        /// </summary>
        public async Task<CompatibilityInfo?> GetCompatibilityAsync(
            int dllModId,
            int fullModId)
        {
            string cacheKey = $"compatibility_{dllModId}_{fullModId}";

            // Sprawdź cache
            if (_cache.TryGetValue(cacheKey, out CompatibilityInfo? cached))
            {
                _log.Write($"[Cache HIT] Kompatybilność {dllModId}+{fullModId}");
                return cached;
            }

            try
            {
                var url = $"{_apiBaseUrl}/api/compatibility?dllModId={dllModId}&fullModId={fullModId}";
                _log.Write($"[CompatibilityService] Pobieranie z: {url}");

                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _log.Write($"[CompatibilityService] API zwróciło {response.StatusCode}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<CompatibilityResponse>(json);

                if (result?.Success == true && result.HasCompatibilities)
                {
                    var entry = result.FirstCompatibility!;

                    var info = new CompatibilityInfo
                    {
                        Id = entry.Id,
                        StatusCode = entry.Status,
                        TestedDate = string.IsNullOrEmpty(entry.TestedDate)
                            ? null
                            : DateTime.Parse(entry.TestedDate),
                        TestedBy = entry.TestedBy,
                        AmongUsVersion = entry.AmongUsVersion,
                        Notes = entry.Notes,
                        IssuesUrl = entry.IssuesUrl,
                        IsCurrentVersion = entry.IsCurrentVersion,
                        Warning = entry.Warning
                    };

                    // Cache na 10 minut
                    _cache.Set(cacheKey, info, TimeSpan.FromMinutes(CacheMinutes));

                    _log.Write($"[CompatibilityService] Status: {info.StatusCode}");
                    return info;
                }

                _log.Write("[CompatibilityService] Brak danych o kompatybilności");
                return null;
            }
            catch (Exception ex)
            {
                _log.Write($"[ERROR] Błąd pobierania kompatybilności: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Pobierz tylko status kompatybilności
        /// </summary>
        public async Task<CompatibilityStatus> GetCompatibilityStatusAsync(
            int dllModId,
            int fullModId)
        {
            var info = await GetCompatibilityAsync(dllModId, fullModId);
            return info?.Status ?? CompatibilityStatus.NotTested;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
```

#### 4.2 Rozszerz DllModSelectionViewModel (2h)

Dodaj wyświetlanie statusów kompatybilności przy wyborze DLL.

#### 4.3 Testy (30 min)

### ✅ Testy Fazy 4

1. **Unit Test**: CompatibilityService pobiera dane
2. **Integracja**: UI pokazuje kolory dla statusów
3. **E2E**: Ostrzeżenia przed instalacją niekompatybilnych działają

---

## 📋 Checklist Ogólny

### Przed Rozpoczęciem
- [ ] Przeczytano całą dokumentację w SUSMODDER_INTEGRATION
- [ ] **🔴 WAŻNE**: Przeczytano i zrozumiano 07_INSTALLATION_MAP_SYSTEM.md
- [ ] Skonfigurowano środowisko deweloperskie (.NET 8.0, Avalonia)
- [ ] Dostęp do testowego/produkcyjnego API

### 🔴 Faza 0: Installation Map System (11h) - NAJPIERW!
- [ ] Modele (InstallationMap, FullModInstallation, DllModInstallation)
- [ ] InstallationMapManager - Save/Load/Discover
- [ ] Integracja z ModManager.InstallSteamAsync
- [ ] Integracja z EpicVersionManager
- [ ] Integracja z DllModificationService (Install/Uninstall)
- [ ] UI: Przycisk "Odkryj mody" + dialog
- [ ] Migracja istniejących instalacji
- [ ] Testy E2E: utrata config → odkrycie → import

### Faza 1: Modele Wersjonowania/Kompatybilności (2h)
- [ ] Utworzono katalog Models (jeśli nie istnieje)
- [ ] Dodano wszystkie 7 klas modeli
- [ ] Testy deserializacji przechodzą

### Faza 2: Instalacja Wersji (4h)
- [ ] ModVersionService działa
- [ ] ModManager przyjmuje specificVersionUrl
- [ ] VersionSelectionDialog działa
- [ ] Integracja z MainWindow OK
- [ ] Testy E2E przechodzą

### Faza 3: Aktualizacje DLL (6h)
- [ ] DllUpdateManager działa
- [ ] ModUpdateChecker rozszerzony
- [ ] DllUpdateDialog działa
- [ ] Aktualizacje w wielu miejscach działają
- [ ] Testy E2E przechodzą

### Faza 4: Kompatybilność (4h)
- [ ] CompatibilityService działa
- [ ] UI pokazuje statusy
- [ ] Ostrzeżenia działają
- [ ] Testy E2E przechodzą

### Finalizacja
- [ ] Wszystkie testy jednostkowe przechodzą
- [ ] Testy integracyjne przechodzą
- [ ] Dokumentacja kodu zaktualizowana
- [ ] Code review wykonane
- [ ] Merge do brancha głównego

---

## 🎯 MVP Definition (Minimum Viable Product)

### MVP = Faza 0 (Fundament) + Faza 1-3 (Podstawowe Funkcje)

**Szacowany czas**: **23h / 3 dni robocze**

### Funkcjonalności MVP:
0. ✅ **Installation Map System** (11h) - 🔴 **FUNDAMENT - NAJPIERW!**
1. ✅ Instalacja starszych wersji modów FULL (4h)
2. ✅ Automatyczne aktualizacje modów DLL w wielu lokalizacjach (6h)

### Funkcjonalności POST-MVP (Faza 4):
3. 🟡 System kompatybilności (4h) - można dodać później

**Zalecenie**: Zaimplementuj Fazę 0 PRZED rozpoczęciem Faz 1-3!

---

## 📊 Timeline - Szczegółowy

### Sprint 0 (Dzień 1-2) - FUNDAMENT
- **Dzień 1 (8h)**: Faza 0 - Installation Map System (modele + core + integracje)
- **Dzień 2 (3h)**: Faza 0 - dokończenie (odkrywanie + migracja + testy)

**Wynik**: System ma stabilny fundament ✅

### Sprint 1 (Dzień 2-4) - MVP
- **Dzień 2 (5h)**: Faza 1 (2h) + Faza 2 rozpoczęcie (3h)
- **Dzień 3 (8h)**: Faza 2 dokończenie (1h) + Faza 3 (6h) + Testy (1h)
- **Dzień 4 (2h)**: Dokończenie Fazy 3 + Testy E2E

**Wynik**: MVP gotowe ✅

### Sprint 2 (Dzień 5) - Rozszerzenia
- **Dzień 5 (4h)**: Faza 4 (System kompatybilności)

**Wynik**: Pełna funkcjonalność ✅

### Sprint 3 (Dzień 5-6) - QA
- **Dzień 5-6 (4h)**: QA, bugfix, dokumentacja, polish

**Wynik**: Produkcyjne ready ✅

**RAZEM**: 5.5 dni roboczych (43 godziny)

---

## 🔗 Linki do Dokumentacji

- **[README.md](./README.md)** - Ogólne wprowadzenie
- **[00_ANALIZA_OBECNEGO_STANU.md](./00_ANALIZA_OBECNEGO_STANU.md)** - Co już działa
- **[01_ARCHITEKTURA_ROZWIAZANIA.md](./01_ARCHITEKTURA_ROZWIAZANIA.md)** - Jak to połączyć
- **[02_MODELE_DANYCH.md](./02_MODELE_DANYCH.md)** - Klasy C# do skopiowania

---

## ✅ Kryteria Sukcesu

### Funkcjonalne
- [ ] Użytkownik może zainstalować starszą wersję moda FULL
- [ ] Użytkownik może zaktualizować mod DLL w wybranych lokalizacjach
- [ ] Użytkownik widzi statusy kompatybilności przy wyborze DLL
- [ ] Wszystkie funkcje działają zarówno dla Steam jak i Epic

### Techniczne
- [ ] Kod jest testowalny (>70% code coverage)
- [ ] Brak memory leaks (checked with profiler)
- [ ] API calls są cache'owane
- [ ] Obsługa błędów jest kompletna

### UX/UI
- [ ] Dialogi są intuicyjne
- [ ] Progressbary pokazują postęp
- [ ] Komunikaty błędów są pomocne
- [ ] Ładowanie nie blokuje UI

---

**Ostatnia aktualizacja:** 2025-10-22
**Wersja:** 1.0
**Status:** ✅ Gotowy do implementacji
