# Frontend – Services i Utilities

## Spis treści
1. [Wprowadzenie](#wprowadzenie)
2. [Services](#services)
3. [Utilities](#utilities)
4. [Wzorce i architektura](#wzorce-i-architektura)

---

## Wprowadzenie

Ten dokument opisuje **serwisy pomocnicze** frontendu oraz **narzędzia utility** wspierające działanie interfejsu użytkownika SUSModder.

**Różnica między Services a Core.Services:**
- **`SUSModder/Services/`** – serwisy specyficzne dla warstwy UI (preloadowanie, logowanie debug, komunikacja z API dla UI)
- **`SUSModder.Core/Services/`** – serwisy logiki biznesowej (instalacja modów, konfiguracja, aktualizacje)

---

## Services

### 1. RolesService

**Plik:** `Services/RolesService.cs`  
**Odpowiedzialności:** Pobieranie listy ról dla modów z API

#### Funkcjonalność

- Komunikacja z API endpoint `/api/roles?configId={id}`
- Deserializacja JSON do modelu `Role`
- Zwraca listę ról dla wybranego moda (configId)

#### Właściwości

```csharp
private readonly HttpClient _httpClient;
private readonly string _baseUrl; // z appsettings.json: Configuration:BaseUrl
private readonly string _rolesEndpoint; // z appsettings.json: Configuration:RolesEndpoint
```

#### Kluczowe metody

##### `GetRolesAsync(int configId)`

```csharp
public async Task<List<Role>> GetRolesAsync(int configId)
{
    try
    {
        var url = $"{_baseUrl.TrimEnd('/')}{_rolesEndpoint}?configId={configId}";
        
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        
        var jsonContent = await response.Content.ReadAsStringAsync();
        var roles = JsonSerializer.Deserialize<List<Role>>(jsonContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        return roles ?? new List<Role>();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error fetching roles: {ex.Message}");
        return new List<Role>();
    }
}
```

**Parametry:**
- `configId` – ID moda (np. Town of Us = 1)

**Zwraca:**
- `List<Role>` – lista ról (Name, Description, Category, Type, Abilities)

#### Użycie

**W `RolesWindow.axaml.cs`:**
```csharp
private readonly RolesService _rolesService;

public RolesWindow(int configId, int modId, string modName)
{
    InitializeComponent();
    
    _rolesService = new RolesService();
    _configId = configId;
    _modName = modName;
    
    LoadRolesAsync();
}

private async Task LoadRolesAsync()
{
    var allRoles = await _rolesService.GetRolesAsync(_configId);
    // Wyświetlenie w ListBox, filtrowanie po kategorii
}
```

#### Implementacja IDisposable

```csharp
public void Dispose()
{
    _httpClient?.Dispose();
}
```

**Ważne:** Zawsze wywołaj `Dispose()` po zakończeniu pracy z serwisem (lub użyj `using`).

---

### 2. DiscordIconPreloader

**Plik:** `Services/DiscordIconPreloader.cs`  
**Odpowiedzialności:** Preloadowanie ikon serwerów Discord w tle (cache)

#### Funkcjonalność

- Pobiera listę serwerów Discord z API (`DiscordFavoritesService` z Core)
- Tworzy `DiscordServerViewModel` dla każdego serwera
- **Asynchronicznie ładuje ikony** (`await serverVM.LoadIconAsync()`)
- Przechowuje w statycznej zmiennej `_preloadedServers` (cache)

#### Właściwości (static)

```csharp
private static List<DiscordServerViewModel>? _preloadedServers; // Cache
private static bool _isPreloading = false; // Flaga trwającego preloadu
private static bool _preloadCompleted = false; // Flaga zakończenia preloadu
```

#### Kluczowe metody (static)

##### `PreloadDiscordIconsAsync()`

```csharp
public static async Task PreloadDiscordIconsAsync()
{
    if (_isPreloading || _preloadCompleted)
        return; // Nie rób ponownie
    
    _isPreloading = true;
    
    try
    {
        System.Diagnostics.Debug.WriteLine("[DiscordIconPreloader] Starting preload...");
        
        // 1. Wczytaj konfigurację (appsettings.json)
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        
        // 2. Pobierz listę serwerów z API
        using var discordService = new DiscordFavoritesService(configuration, diagnosticsOutput);
        var serverDataList = await discordService.GetDiscordFavoritesAsync();
        var discordServers = DiscordServerAdapter.FromServerDataList(serverDataList);
        
        // 3. Konwertuj na ViewModels i załaduj ikony
        var serverViewModels = discordServers.Select(server => new DiscordServerViewModel(server)).ToList();
        
        var loadTasks = serverViewModels.Select(async serverVM =>
        {
            await serverVM.LoadIconAsync(); // Async ładowanie ikony
            return serverVM;
        }).ToArray();
        
        _preloadedServers = (await Task.WhenAll(loadTasks)).ToList();
        
        System.Diagnostics.Debug.WriteLine($"[DiscordIconPreloader] Preloaded {_preloadedServers.Count} servers");
        _preloadCompleted = true;
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[DiscordIconPreloader] Error: {ex.Message}");
        _preloadedServers = null;
    }
    finally
    {
        _isPreloading = false;
    }
}
```

##### `GetPreloadedServers()`

```csharp
public static List<DiscordServerViewModel>? GetPreloadedServers()
{
    return _preloadedServers?.ToList(); // Zwróć kopię listy
}
```

##### `IsPreloadCompleted`

```csharp
public static bool IsPreloadCompleted => _preloadCompleted;
```

#### Użycie

**W `MainWindowViewModel` (konstruktor):**
```csharp
// Preload w tle (nie blokuje UI)
_ = Task.Run(async () =>
{
    await DiscordIconPreloader.PreloadDiscordIconsAsync();
});
```

**W `RecommendedDiscordsViewModel`:**
```csharp
private async Task LoadDiscordServersAsync()
{
    // Najpierw sprawdź cache
    var preloadedServers = DiscordIconPreloader.GetPreloadedServers();
    
    if (preloadedServers != null && preloadedServers.Count > 0)
    {
        // Użyj cache'owanych danych
        foreach (var server in preloadedServers)
        {
            DiscordServers.Add(server);
        }
    }
    else
    {
        // Fallback: pobierz z API bezpośrednio
        // ...
    }
}
```

**Zalety:**
- **Szybsze ładowanie** okna `RecommendedDiscordsWindow` (ikony już załadowane)
- **Nie blokuje UI** – preload w tle podczas inicjalizacji aplikacji
- **Cache** – dane przechowywane do końca sesji aplikacji

---

### 3. ConsoleLogger

**Plik:** `Services/ConsoleLogger.cs`  
**Odpowiedzialności:** Przechwytywanie logów debug i wyświetlanie w `ConsoleWindow`

#### Funkcjonalność

- Przechwytuje `Debug.WriteLine()` przez `Trace.Listeners`
- Przechwytuje `Console.WriteLine()` / `Console.Error` przez custom `TextWriter`
- Przekierowuje logi do okna `ConsoleWindow` (widoczne w trybie deweloperskim)

#### Właściwości (static)

```csharp
private static bool _isInitialized = false;
private static DebugTraceListener? _debugListener; // Listener dla Debug.WriteLine
private static ConsoleTextWriter? _consoleWriter; // Writer dla Console.WriteLine
```

#### Kluczowe metody (static)

##### `Initialize()`

```csharp
public static void Initialize()
{
    if (_isInitialized) return;
    
    // Przechwytuj Debug.WriteLine przez Trace.Listeners
    _debugListener = new DebugTraceListener();
    Trace.Listeners.Add(_debugListener);
    
    // Przechwytuj Console.WriteLine
    _consoleWriter = new ConsoleTextWriter();
    Console.SetOut(_consoleWriter);
    Console.SetError(_consoleWriter);
    
    _isInitialized = true;
}
```

**Wywołane w:** `App.axaml.cs` → `OnFrameworkInitializationCompleted()`

##### `Shutdown()`

```csharp
public static void Shutdown()
{
    if (_debugListener != null)
    {
        Trace.Listeners.Remove(_debugListener);
        _debugListener = null;
    }
    
    _consoleWriter = null;
    _isInitialized = false;
}
```

##### Metody pomocnicze

```csharp
public static void WriteLine(string message, LogLevel level = LogLevel.Info)
{
    Debug.WriteLine(message);
    ConsoleWindow.WriteLog(message, level);
}

public static void WriteInfo(string message) => WriteLine(message, LogLevel.Info);
public static void WriteWarning(string message) => WriteLine(message, LogLevel.Warning);
public static void WriteError(string message) => WriteLine(message, LogLevel.Error);
```

#### Wewnętrzne klasy

##### `DebugTraceListener` (nested class)

```csharp
private class DebugTraceListener : TraceListener
{
    public override void Write(string? message)
    {
        if (!string.IsNullOrEmpty(message))
        {
            ConsoleWindow.WriteLog(message, LogLevel.Info);
        }
    }

    public override void WriteLine(string? message)
    {
        if (!string.IsNullOrEmpty(message))
        {
            ConsoleWindow.WriteLog(message, LogLevel.Info);
        }
    }
}
```

##### `ConsoleTextWriter` (nested class)

```csharp
private class ConsoleTextWriter : TextWriter
{
    private StringBuilder _buffer = new();

    public override void Write(char value)
    {
        if (value == '\n')
        {
            Flush();
        }
        else if (value != '\r')
        {
            _buffer.Append(value);
        }
    }

    public override void Flush()
    {
        if (_buffer.Length > 0)
        {
            var message = _buffer.ToString();
            ConsoleWindow.WriteLog(message, LogLevel.Info);
            _buffer.Clear();
        }
    }

    public override Encoding Encoding => Encoding.UTF8;
}
```

#### Użycie

**Automatyczne przechwytywanie:**
```csharp
Debug.WriteLine("Test message"); // Automatycznie w ConsoleWindow
Console.WriteLine("Another message"); // Też automatycznie
```

**Bezpośrednie użycie:**
```csharp
ConsoleLogger.WriteInfo("Instalacja rozpoczęta");
ConsoleLogger.WriteWarning("Brak uprawnień");
ConsoleLogger.WriteError("Nie udało się pobrać pliku");
```

#### Integracja z ConsoleWindow

**`ConsoleWindow.axaml.cs`:**
```csharp
public static void WriteLog(string message, LogLevel level)
{
    // Dodaj wiadomość do ObservableCollection
    // UI automatycznie zaktualizuje ListBox
    
    Dispatcher.UIThread.Post(() =>
    {
        LogEntries.Add($"[{level}] {message}");
    });
}
```

---

### 4. InstallationSilentUserInteraction

**Plik:** `Services/InstallationSilentUserInteraction.cs`  
**Status:** ⚠️ **ZDUPLIKOWANY** (również w `MainWindowViewModel.cs` linia ~2980)

#### Odpowiedzialności

Implementacja `IUserInteraction` (z Core) dla instalacji w trybie cichym (silent) – bez dialogów użytkownika.

#### Implementacja

```csharp
public class InstallationSilentUserInteraction : IUserInteraction
{
    public Task<bool> AskRetryAsync(string title, string message)
    {
        return Task.FromResult(false); // Nigdy nie retry
    }

    public Task ShowErrorAsync(string title, string message)
    {
        return Task.CompletedTask; // Ignoruj błędy
    }

    public Task ShowInfoAsync(string title, string message)
    {
        return Task.CompletedTask; // Ignoruj informacje
    }
}
```

#### Użycie

**W `MainWindowViewModel` (update automatyczny):**
```csharp
private async Task UpdateModsSilentlyAsync(List<ModItem> modsToUpdate)
{
    var silentUserInteraction = new InstallationSilentUserInteraction();
    
    foreach (var mod in modsToUpdate)
    {
        await _modService.InstallModAsync(
            ModItemAdapter.ToConfig(mod),
            silentUserInteraction // Bez dialogów
        );
    }
}
```

**Problem:** Duplikat na końcu `MainWindowViewModel.cs` – zobacz [REFACTOR.md](REFACTOR.md).

---

## Utilities

### ViewLocator

**Plik:** `ViewLocator.cs` (w głównym folderze `SUSModder/`)  
**Odpowiedzialności:** Automatyczne mapowanie ViewModel → View (konwencja nazewnictwa)

#### Funkcjonalność

- Implementuje `IDataTemplate` (Avalonia)
- Dla `ViewModel` szuka odpowiadającej `View` (zamiana suffiksu `ViewModel` → `View`)
- Tworzy instancję `View` i wiąże z `ViewModel` (DataContext)

#### Implementacja

```csharp
public class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null)
            return new TextBlock { Text = "No data" };
        
        var name = data.GetType().FullName!.Replace("ViewModel", "View");
        var type = Type.GetType(name);
        
        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }
        
        return new TextBlock { Text = "Not Found: " + name };
    }
    
    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
```

#### Przykład działania

| ViewModel | Szukany View | Wynik |
|-----------|--------------|-------|
| `SUSModder.ViewModels.MainWindowViewModel` | `SUSModder.Views.MainWindowView` | ❌ Nie znaleziony (View nazywa się `MainWindow`, nie `MainWindowView`) |
| `SUSModder.ViewModels.AppSettingsViewModel` | `SUSModder.Views.AppSettingsView` | ❌ Nie znaleziony (Window nazywa się `AppSettingsWindow`) |

**W praktyce:** ViewLocator jest **rzadko używany** w SUSModder, ponieważ ViewModels są jawnie przypisywane do Views w code-behind:

```csharp
// W MainWindow.axaml.cs
public MainWindow()
{
    InitializeComponent();
    DataContext = new MainWindowViewModel(); // Jawne przypisanie
}
```

**Użycie ViewLocator:**
```xml
<ContentControl Content="{Binding MyViewModel}" />
<!-- ViewLocator automatycznie znajdzie MyView i utworzy instancję -->
```

---

## Wzorce i architektura

### 1. Service Locator (anty-pattern?)

Niektóre serwisy są tworzone bezpośrednio w ViewModels zamiast przez Dependency Injection:

```csharp
// W MainWindowViewModel
_touConfigService = new ToUConfigService();
_dllModificationService = new DllModificationService(configService, diagnosticsOutput);
```

**Zalety:**
- Prostota implementacji

**Wady:**
- Trudniejsze testowanie (mock serwisów)
- Tight coupling

**Rekomendacja:** Rozważ wprowadzenie DI Container (Microsoft.Extensions.DependencyInjection) w przyszłości.

---

### 2. Static Services (Singleton)

`DiscordIconPreloader` i `ConsoleLogger` są statyczne (static fields/methods):

```csharp
public static class ConsoleLogger
{
    private static bool _isInitialized = false;
    public static void Initialize() { /* ... */ }
}
```

**Zalety:**
- Łatwy dostęp z dowolnego miejsca (`ConsoleLogger.WriteInfo(...)`)
- Współdzielony stan (cache w `DiscordIconPreloader`)

**Wady:**
- Trudniejsze testowanie
- Globalny stan (może prowadzić do problemów w multi-threading)

---

### 3. IDisposable dla HttpClient

`RolesService` implementuje `IDisposable` i zwalnia `HttpClient`:

```csharp
public void Dispose()
{
    _httpClient?.Dispose();
}
```

**Best practice:** Zawsze wywołuj `Dispose()` lub użyj `using`:

```csharp
using var rolesService = new RolesService();
var roles = await rolesService.GetRolesAsync(configId);
```

---

### 4. Async preloading w tle

`DiscordIconPreloader` używa `Task.Run` do preloadu w tle (nie blokuje UI):

```csharp
_ = Task.Run(async () =>
{
    await DiscordIconPreloader.PreloadDiscordIconsAsync();
});
```

**Efekt:** Aplikacja startuje szybko, ikony ładują się w tle.

---

## Statystyki

| Serwis/Utility | Linie kodu | Typ | Główne funkcje |
|----------------|------------|-----|----------------|
| **RolesService** | ~60 | Service | Pobieranie ról z API |
| **DiscordIconPreloader** | ~80 | Service | Preload ikon Discord (cache) |
| **ConsoleLogger** | ~140 | Service | Przechwytywanie logów debug |
| **InstallationSilentUserInteraction** | ~25 | Service | Ciche instalacje (bez dialogów) |
| **ViewLocator** | ~30 | Utility | Automatyczne mapowanie ViewModel → View |

---

## Problemy do naprawy

Zobacz [REFACTOR.md](REFACTOR.md):

1. **Duplikat `InstallationSilentUserInteraction`** – w `Services/` i na końcu `MainWindowViewModel.cs` (linia ~2980) ⚠️
2. **ConsoleLogger** – małe użycie (tylko `Initialize()` w `App.cs`), rozważ rozszerzenie lub usunięcie

---

## Best practices

### ✅ DO:
- Używaj `IDisposable` dla zasobów (HttpClient, FileStream)
- Preloaduj dane w tle (`Task.Run`) dla lepszej responsywności UI
- Cache'uj wyniki kosztownych operacji (np. ikony Discord)
- Używaj async/await dla operacji I/O

### ❌ NIE:
- Nie twórz zbyt wielu static serwisów (trudne testowanie)
- Nie blokuj UI thread w serwisach (używaj async)
- Nie duplikuj kodu serwisów (DRY principle)

---

**Autor dokumentacji:** AI Assistant  
**Data utworzenia:** 2025-10-19  
**Status:** Wersja robocza – zakończenie dokumentacji frontendu
