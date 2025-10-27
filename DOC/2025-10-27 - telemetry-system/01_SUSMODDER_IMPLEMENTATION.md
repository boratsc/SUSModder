# SUSModder - Implementacja Telemetrii

## 📋 Przegląd

Dokumentacja implementacji systemu telemetrii w aplikacji SUSModder (C# / .NET 8 / Avalonia).

## 🏗️ Architektura

### Komponenty:
1. **HardwareIdProvider** - Generowanie anonimowego hash z Hardware ID
2. **TelemetryService** - Wysyłanie heartbeat do API
3. **SessionTracker** - Śledzenie czasu sesji
4. **Settings UI** - Opt-out checkbox

### Lokalizacja plików:
```
SUSModder.Core/
├── Services/
│   ├── TelemetryService.cs          (główna logika)
│   └── SessionTracker.cs            (tracking czasu sesji)
└── Utilities/
    └── HardwareIdProvider.cs        (generowanie hash)

SUSModder/
├── App.axaml.cs                     (inicjalizacja + shutdown)
├── ViewModels/
│   └── SettingsViewModel.cs         (opt-out toggle)
└── Views/
    └── SettingsView.axaml           (UI opt-out)

SUSModder/appsettings.json           (+TelemetryEnabled, +UserHash)
```

## 📄 Kod źródłowy

### 1. HardwareIdProvider.cs

```csharp
using System;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace SUSModder.Core.Utilities
{
    /// <summary>
    /// Generuje anonimowy hash użytkownika na podstawie Hardware ID
    /// </summary>
    public static class HardwareIdProvider
    {
        private static string? _cachedHash;

        /// <summary>
        /// Pobiera anonimowy hash użytkownika (SHA256 z Hardware ID)
        /// </summary>
        /// <returns>64-znakowy hex string (SHA256)</returns>
        public static string GetAnonymousUserHash()
        {
            if (!string.IsNullOrEmpty(_cachedHash))
                return _cachedHash;

            try
            {
                // Zbierz unikalne identyfikatory sprzętowe
                var hardwareId = GetHardwareIdentifier();
                
                // Zahashuj SHA256 (jednostronnie - nie da się odtworzyć oryginalnych danych)
                _cachedHash = ComputeSha256Hash(hardwareId);
                
                return _cachedHash;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to generate hardware hash: {ex.Message}");
                
                // Fallback - losowy GUID (będzie się zmieniał przy każdym uruchomieniu)
                // Lepszy niż brak telemetrii, ale nie idealny
                _cachedHash = Guid.NewGuid().ToString("N");
                return _cachedHash;
            }
        }

        /// <summary>
        /// Pobiera unikalne identyfikatory sprzętowe (CPU + Motherboard + BIOS)
        /// </summary>
        private static string GetHardwareIdentifier()
        {
            var sb = new StringBuilder();

            try
            {
                // CPU ID
                var cpuId = GetWmiProperty("Win32_Processor", "ProcessorId");
                sb.Append(cpuId ?? "UNKNOWN_CPU");

                // Motherboard Serial
                var mbSerial = GetWmiProperty("Win32_BaseBoard", "SerialNumber");
                sb.Append(mbSerial ?? "UNKNOWN_MB");

                // BIOS Serial
                var biosSerial = GetWmiProperty("Win32_BIOS", "SerialNumber");
                sb.Append(biosSerial ?? "UNKNOWN_BIOS");

                // Machine GUID (Windows Registry)
                var machineGuid = GetMachineGuid();
                sb.Append(machineGuid ?? "UNKNOWN_GUID");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting hardware ID: {ex.Message}");
                // Jeśli nie można pobrać - użyj timestamp jako salt
                sb.Append(Environment.MachineName);
                sb.Append(Environment.UserName);
                sb.Append(DateTime.UtcNow.Ticks.ToString());
            }

            return sb.ToString();
        }

        /// <summary>
        /// Pobiera właściwość WMI
        /// </summary>
        private static string? GetWmiProperty(string wmiClass, string property)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
                using var collection = searcher.Get();
                
                foreach (var obj in collection)
                {
                    var value = obj[property]?.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
            }
            catch
            {
                // Ignore WMI errors
            }

            return null;
        }

        /// <summary>
        /// Pobiera Machine GUID z Windows Registry
        /// </summary>
        private static string? GetMachineGuid()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                return key?.GetValue("MachineGuid")?.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Oblicza SHA256 hash
        /// </summary>
        private static string ComputeSha256Hash(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = sha256.ComputeHash(bytes);
            
            // Konwersja do hex string
            var sb = new StringBuilder();
            foreach (var b in hashBytes)
            {
                sb.Append(b.ToString("x2"));
            }
            
            return sb.ToString();
        }
    }
}
```

### 2. SessionTracker.cs

```csharp
using System;
using System.Diagnostics;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Śledzi czas trwania sesji użytkownika
    /// </summary>
    public class SessionTracker
    {
        private readonly Stopwatch _stopwatch;
        private DateTime _sessionStartTime;

        public SessionTracker()
        {
            _stopwatch = new Stopwatch();
            _sessionStartTime = DateTime.UtcNow;
            _stopwatch.Start();
        }

        /// <summary>
        /// Pobiera czas sesji w sekundach
        /// </summary>
        public int GetSessionTimeSeconds()
        {
            return (int)_stopwatch.Elapsed.TotalSeconds;
        }

        /// <summary>
        /// Pobiera czas rozpoczęcia sesji
        /// </summary>
        public DateTime GetSessionStartTime()
        {
            return _sessionStartTime;
        }

        /// <summary>
        /// Resetuje licznik sesji (np. po wysłaniu heartbeat)
        /// </summary>
        public void Reset()
        {
            _stopwatch.Restart();
            _sessionStartTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Zatrzymuje tracking (przy zamykaniu aplikacji)
        /// </summary>
        public void Stop()
        {
            _stopwatch.Stop();
        }
    }
}
```

### 3. TelemetryService.cs

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Configuration;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Serwis telemetrii - zbieranie anonimowych statystyk użytkowania
    /// </summary>
    public class TelemetryService : IDisposable
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly SessionTracker _sessionTracker;
        private readonly string _userHash;
        private bool _isEnabled;

        public TelemetryService(IConfiguration configuration)
        {
            _configuration = configuration;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(2) // Krótki timeout - nie blokujemy UI
            };
            _sessionTracker = new SessionTracker();
            
            // Generuj/wczytaj anonimowy hash użytkownika
            _userHash = HardwareIdProvider.GetAnonymousUserHash();
            
            // Sprawdź czy telemetria jest włączona
            _isEnabled = _configuration.GetValue<bool>("Configuration:TelemetryEnabled", true);
        }

        /// <summary>
        /// Wysyła heartbeat do API (fire-and-forget)
        /// </summary>
        public async Task SendHeartbeatAsync()
        {
            // Jeśli telemetria wyłączona - nic nie rób
            if (!_isEnabled)
            {
                System.Diagnostics.Debug.WriteLine("Telemetry disabled - skipping heartbeat");
                return;
            }

            try
            {
                var baseUrl = _configuration["Configuration:BaseUrl"]?.TrimEnd('/');
                if (string.IsNullOrEmpty(baseUrl))
                {
                    System.Diagnostics.Debug.WriteLine("BaseUrl not configured - skipping telemetry");
                    return;
                }

                var telemetryUrl = $"{baseUrl}/api/telemetry/heartbeat";

                // Zbierz dane do wysłania
                var data = new
                {
                    userHash = _userHash,
                    appVersion = _configuration["Configuration:CurrentVersion"] ?? "unknown",
                    platform = _configuration["Configuration:Mode"] ?? "unknown",
                    language = _configuration["Configuration:Language"] ?? "pl",
                    installedModIds = GetInstalledModIds(),
                    sessionTimeSeconds = _sessionTracker.GetSessionTimeSeconds(),
                    timestamp = DateTime.UtcNow.ToString("O") // ISO 8601
                };

                var json = JsonSerializer.Serialize(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                System.Diagnostics.Debug.WriteLine($"Sending telemetry heartbeat: {json}");

                // Fire-and-forget - nie czekamy na odpowiedź
                _ = _httpClient.PostAsync(telemetryUrl, content).ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        System.Diagnostics.Debug.WriteLine($"Telemetry heartbeat failed: {task.Exception?.GetBaseException().Message}");
                    }
                    else if (task.IsCompletedSuccessfully)
                    {
                        System.Diagnostics.Debug.WriteLine("Telemetry heartbeat sent successfully");
                    }
                });

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                // Ignoruj błędy telemetrii - nie przerywamy działania aplikacji
                System.Diagnostics.Debug.WriteLine($"Telemetry error: {ex.Message}");
            }
        }

        /// <summary>
        /// Wysyła końcowy heartbeat przy zamykaniu aplikacji
        /// </summary>
        public async Task SendShutdownHeartbeatAsync()
        {
            _sessionTracker.Stop();
            await SendHeartbeatAsync();
        }

        /// <summary>
        /// Pobiera listę ID zainstalowanych modów
        /// </summary>
        private List<int> GetInstalledModIds()
        {
            try
            {
                // Wczytaj konfigurację modów
                var configService = new ConfigService(_configuration);
                var configs = configService.LoadConfig();

                // Zwróć tylko ID modów, które są zainstalowane
                return configs
                    .Where(m => m.IsInstalled)
                    .Select(m => m.Id)
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get installed mod IDs: {ex.Message}");
                return new List<int>();
            }
        }

        /// <summary>
        /// Włącza lub wyłącza telemetrię
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            _isEnabled = enabled;
            
            // Zapisz do appsettings.json
            SaveTelemetryEnabledToConfig(enabled);
        }

        /// <summary>
        /// Zapisuje ustawienie telemetrii do appsettings.json
        /// </summary>
        private void SaveTelemetryEnabledToConfig(bool enabled)
        {
            try
            {
                var exeDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
                var appSettingsPath = System.IO.Path.Combine(exeDir, "appsettings.json");

                if (!System.IO.File.Exists(appSettingsPath))
                    return;

                var json = System.IO.File.ReadAllText(appSettingsPath);
                var settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

                if (settings == null)
                    return;

                // Zaktualizuj wartość TelemetryEnabled
                if (settings.TryGetValue("Configuration", out var configObj) && configObj is JsonElement configElement)
                {
                    var configDict = JsonSerializer.Deserialize<Dictionary<string, object>>(configElement.GetRawText());
                    if (configDict != null)
                    {
                        configDict["TelemetryEnabled"] = enabled;
                        settings["Configuration"] = configDict;
                    }
                }

                // Zapisz z powrotem
                var updatedJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(appSettingsPath, updatedJson);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save telemetry setting: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
```

## 🔧 Integracja w aplikacji

### 1. Aktualizacja appsettings.json

```json
{
    "Configuration": {
        "UpdateServerUrl": "https://susmodder.app/api/susmodder-config",
        "CurrentVersion": "2.0.0",
        "BaseUrl": "https://susmodder.app/",
        "Mode": "steam",
        "lastLaunchId": 0,
        "Theme": "dark",
        "Language": "",
        "TelemetryEnabled": true
    },
    "AppSettings": {
        "ModsInstallPath": "",
        "DefaultModsPath": "%APPDATA%\\Among Us - Mody",
        "DeveloperMode": false
    }
}
```

### 2. App.axaml.cs - Inicjalizacja

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Services;
using SUSModder.ViewModels;
using SUSModder.Views;
using System;
using System.IO;

namespace SUSModder;

public partial class App : Application
{
    private TelemetryService? _telemetryService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Inicjalizuj telemetrię
            InitializeTelemetry();

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };

            // Hook do zamknięcia aplikacji - wyślij końcowy heartbeat
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void InitializeTelemetry()
    {
        try
        {
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
            var configuration = new ConfigurationBuilder()
                .SetBasePath(exeDir)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            _telemetryService = new TelemetryService(configuration);

            // Wyślij heartbeat przy starcie (async, fire-and-forget)
            _ = _telemetryService.SendHeartbeatAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize telemetry: {ex.Message}");
        }
    }

    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // Wyślij końcowy heartbeat przed zamknięciem
        if (_telemetryService != null)
        {
            await _telemetryService.SendShutdownHeartbeatAsync();
            _telemetryService.Dispose();
        }
    }
}
```

### 3. SettingsViewModel.cs - Opt-out Toggle

```csharp
// Dodaj do istniejącego SettingsViewModel.cs

using ReactiveUI;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Services;

namespace SUSModder.ViewModels
{
    public partial class SettingsViewModel : ViewModelBase
    {
        private bool _telemetryEnabled;
        private TelemetryService? _telemetryService;

        public bool TelemetryEnabled
        {
            get => _telemetryEnabled;
            set
            {
                this.RaiseAndSetIfChanged(ref _telemetryEnabled, value);
                OnTelemetryEnabledChanged(value);
            }
        }

        public SettingsViewModel()
        {
            // Wczytaj aktualną wartość z appsettings.json
            LoadTelemetrySettings();
        }

        private void LoadTelemetrySettings()
        {
            try
            {
                var exeDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(exeDir)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .Build();

                _telemetryEnabled = configuration.GetValue<bool>("Configuration:TelemetryEnabled", true);
                _telemetryService = new TelemetryService(configuration);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load telemetry settings: {ex.Message}");
                _telemetryEnabled = true; // Domyślnie włączone
            }
        }

        private void OnTelemetryEnabledChanged(bool enabled)
        {
            _telemetryService?.SetEnabled(enabled);
            System.Diagnostics.Debug.WriteLine($"Telemetry {(enabled ? "enabled" : "disabled")}");
        }
    }
}
```

### 4. SettingsView.axaml - UI Opt-out

```xml
<!-- Dodaj do istniejącego SettingsView.axaml -->

<StackPanel Spacing="10" Margin="0,20,0,0">
    <TextBlock Text="Prywatność" FontSize="16" FontWeight="Bold" />
    
    <CheckBox IsChecked="{Binding TelemetryEnabled}"
              Content="Wysyłaj anonimowe statystyki użytkowania" />
    
    <TextBlock Text="Pomaga nam rozwijać aplikację. Żadne dane osobowe nie są zbierane."
               FontSize="12"
               Foreground="#808080"
               TextWrapping="Wrap"
               Margin="25,0,0,0" />
    
    <TextBlock Text="Zbieramy: wersję aplikacji, platformę, język, listę zainstalowanych modów (tylko ID) oraz czas sesji."
               FontSize="11"
               Foreground="#606060"
               TextWrapping="Wrap"
               Margin="25,5,0,0" />
</StackPanel>
```

## 📦 Zależności NuGet

Dodaj do `SUSModder.Core.csproj`:

```xml
<ItemGroup>
    <PackageReference Include="System.Management" Version="8.0.0" />
</ItemGroup>
```

## ✅ Checklist Implementacji

- [ ] Dodać `HardwareIdProvider.cs` do `SUSModder.Core/Utilities/`
- [ ] Dodać `SessionTracker.cs` do `SUSModder.Core/Services/`
- [ ] Dodać `TelemetryService.cs` do `SUSModder.Core/Services/`
- [ ] Zmodyfikować `App.axaml.cs` (inicjalizacja + shutdown hook)
- [ ] Dodać `TelemetryEnabled` do `appsettings.json`
- [ ] Zmodyfikować `SettingsViewModel.cs` (opt-out toggle)
- [ ] Zmodyfikować `SettingsView.axaml` (UI checkbox)
- [ ] Dodać NuGet `System.Management`
- [ ] Przetestować lokalne generowanie hash
- [ ] Przetestować wysyłanie heartbeat (mock endpoint)

## 🧪 Testowanie

### Test 1: Generowanie Hash

```csharp
var hash1 = HardwareIdProvider.GetAnonymousUserHash();
var hash2 = HardwareIdProvider.GetAnonymousUserHash();

Assert.Equal(hash1, hash2); // Ten sam hash przy ponownym wywołaniu
Assert.Equal(64, hash1.Length); // SHA256 = 64 hex chars
```

### Test 2: Heartbeat Payload

```json
{
  "userHash": "a1b2c3d4e5f6...",
  "appVersion": "2.0.0",
  "platform": "steam",
  "language": "pl",
  "installedModIds": [1, 3, 7, 12],
  "sessionTimeSeconds": 123,
  "timestamp": "2025-10-27T12:34:56.789Z"
}
```

### Test 3: Opt-out

1. Odpal aplikację z `TelemetryEnabled: true`
2. Sprawdź logi - powinien wysłać heartbeat
3. Wyłącz w Settings checkbox
4. Restart aplikacji
5. Sprawdź logi - nie powinien wysyłać heartbeat

## 📝 Notatki implementacyjne

### Performance:
- `HardwareIdProvider` cachuje hash (1x na sesję)
- `SessionTracker` używa `Stopwatch` (zero overhead)
- HTTP request z timeout 2s (nie blokuje UI)

### Error Handling:
- Wszystkie wyjątki są łapane i logowane (Debug.WriteLine)
- Brak telemetrii nie przerywa działania aplikacji
- Fallback na random GUID jeśli WMI nie działa

### Privacy:
- Hash generowany lokalnie (server nigdy nie widzi raw data)
- Opt-out przechowywany w `appsettings.json`
- Można zweryfikować payload w Fiddler/Wireshark

---

**Status:** ✅ Ready for Implementation  
**Estimated time:** 4-6 godzin (z testami)
