# Steam Integration PoC - Analiza Techniczna

**Data**: 2025-11-07
**Status**: Proof of Concept
**Autor**: Research z Claude Code

## 1. Cel Dokumentu

Analiza techniczna metod legalnego pobierania gier ze Steam w kontekście zastąpienia obecnego systemu (własne repo łamiące EULA) rozwiązaniem zgodnym z warunkami użytkowania Steam.

**Główne cele**:
- ✅ Zgodność z EULA Steam
- ✅ User-friendly experience (brak/minimum interakcji użytkownika)
- ✅ Transparentność działania (wzorowane na `legendary.exe` dla Epic Games)
- ✅ Możliwość automatyzacji i progress reporting

---

## 2. Opcje Techniczne

### 2.1 SteamCMD

**Źródło**: https://developer.valvesoftware.com/wiki/SteamCMD

**Charakterystyka**:
- ✅ **Oficjalny tool Valve** - pełna zgodność z EULA
- ✅ Dedykowany do automatyzacji i serwerów dedykowanych
- ❌ Słabe progress reporting (flakey STDOUT na Windows)
- ❌ Gorsze UX - wymaga ręcznego wpisywania kodów Steam Guard z emaila

**Autentykacja**:
```bash
# Pierwszy raz - wymaga Steam Guard code z emaila
steamcmd +login <username>
# System prosi o hasło
# System prosi o Steam Guard code
# Zapisuje "sentry files" w ~/Steam/ssfn*

# Kolejne uruchomienia - brak interakcji
steamcmd +login <username> +quit
```

**Integracja C#**:
```csharp
// NuGet: koskit.SteamCmdFluentApi
using SteamCmdWrapper;

var steamCmd = new SteamCmd();
await steamCmd.Install();
await steamCmd.LoginAsync(username, password);
await steamCmd.UpdateAppAsync(appId, installDir);
```

**Zalety**:
- Oficjalne wsparcie Valve
- Zero ryzyka prawnego
- Sesja persystuje po pierwszym logowaniu

**Wady**:
- Progress reporting praktycznie niemożliwy do zaimplementowania
- UX gorszy niż konkurencja (email codes)
- Brak wsparcia dla web-based auth

---

### 2.2 DepotDownloader

**Źródło**: https://github.com/SteamRE/DepotDownloader

**Charakterystyka**:
- ⚠️ **Narzędzie community** - nie oficjalne, ale szeroko akceptowane
- ✅ QR code authentication - **znacznie lepsze UX**
- ✅ Doskonałe progress reporting (event-based)
- ✅ Łatwa integracja przez NuGet (`DepotDownloaderLib`)

**Autentykacja**:
```bash
# QR Code - ZERO TYPING REQUIRED
DepotDownloader -app 945360 -depot 945361 -qr -remember-password
# Pokazuje QR code
# User scanuje przez Steam mobile app
# Sesja zapisana, kolejne uruchomienia bez interakcji

# Alternatywa: tradycyjny Steam Guard
DepotDownloader -app 945360 -username user -password pass
# Pyta o 2FA code
```

**Integracja C#**:
```csharp
// NuGet: DepotDownloaderLib (v1.1.1, .NET 7)
using DepotDownloaderLib;

// Event-based progress reporting
DepotDownloaderLib.onConsoleOutput += (line) => {
    // "45% - Downloading file.dat"
    ParseProgress(line);
};

string[] args = { "-app", "945360", "-depot", "945361",
                  "-manifest", "xxx", "-dir", installPath };
await DepotDownloaderLib.StartDownload(args, useBackgroundWorker: true);
```

**Zalety**:
- QR code = modern UX (jak OAuth, ale bez browsera)
- Event-based progress - łatwe parsowanie
- Świetna dokumentacja i aktywne community
- Built on SteamKit2 (solidne podstawy)

**Wady**:
- Wolniejsze niż Steam client (10-15 Mbit/s vs 100 Mbit/s)
- Nieofficjalne (gray area prawnie)
- Wymaga zaufania do third-party tool

---

## 3. Porównanie z `legendary.exe` (Epic Games)

### Obecna integracja w `EpicVersionManager.cs`

**Autentykacja** (`EpicVersionManager.cs:409`):
```csharp
// Web-based OAuth flow
await RunLegendaryCommandAsync("auth --import");
// LUB
await RunLegendaryCommandAsync("auth");
```

**Flow użytkownika**:
1. Legendary otwiera browser/webview
2. User loguje się na stronie Epic Games
3. Kopiuje authorization code do terminala (lub importuje z Epic Games Launcher)
4. Sesja zapisana lokalnie
5. Wszystkie kolejne operacje bez interakcji

**Progress reporting** (`EpicVersionManager.cs:753-859`):
```csharp
private void ParseLegendaryOutput(string line)
{
    // Regex parsing STDOUT/STDERR
    var progressMatch = Regex.Match(line, @"Progress: (\d+\.\d+)%");
    if (progressMatch.Success)
    {
        double percentage = double.Parse(progressMatch.Groups[1].Value);
        UpdateProgress(percentage);
    }
}
```

### Porównanie funkcji

| Feature | Legendary (Epic) | SteamCMD | DepotDownloader |
|---------|------------------|----------|-----------------|
| **Web Browser Auth** | ✅ OAuth | ❌ Nie | ❌ Nie |
| **QR Code Auth** | ❌ Nie | ❌ Nie | ✅ **TAK** |
| **Import z launchera** | ✅ `--import` | ❌ Nie | ❌ Nie |
| **2FA Method** | Web-based | Email code | **QR / Mobile app** |
| **Progress Parsing** | ⭐⭐⭐⭐⭐ Regex | ⭐⭐ Unreliable | ⭐⭐⭐⭐ Events |
| **User Friendliness** | ⭐⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐⭐ |
| **Legal Status** | ✅ Oficjalne | ✅ Oficjalne | ⚠️ Community |

**Kluczowa różnica**:
- Legendary używa **web browser** → Epic OAuth
- DepotDownloader używa **QR code** → Steam mobile app
- SteamCMD używa **email** → ręczne wpisywanie

**Wniosek**: QR code to najbliższy odpowiednik web-based auth dla Steam (Steam nie ma public OAuth API).

---

## 4. Aspekty Prawne (EULA/ToS)

### SteamCMD
✅ **Pełna zgodność z EULA**
- Oficjalny tool Valve
- Dokumentowany na Valve Developer Community wiki
- Intended use case: dedicated servers, CI/CD

### DepotDownloader
⚠️ **Gray area**

**Steam Subscriber Agreement** (relevant quote):
> "You may not use the Content and Services for any purpose other than the permitted access to Steam and your Subscriptions, and to make personal, non-commercial use of your Subscriptions."

**Analiza**:
- ❌ Nie jest oficjalnie wspierany przez Valve
- ✅ Używa legalnego API (SteamKit2)
- ✅ Wymaga własnych credentials użytkownika (nie obchodzi security)
- ✅ Szeroko akceptowany przez community (dokumentowany w Steam Community guides, PCGamingWiki)
- ✅ Legitimate use case: dostęp do starych wersji gier (Valve zepsuło depot download w Steam client)

**Precedens**:
- Valve **toleruje** używanie DepotDownloader
- Nie ma przypadków banów za jego używanie
- Tool istnieje od lat i jest public

**Ryzyko**: Teoretyczne (ToS violation), praktyczne (żadne)

---

## 5. Rekomendacja: Hybrydowe Podejście

### Strategia

**Faza 1: Domyślnie SteamCMD (bezpieczny)**
- Pokazywanie jasnych instrukcji dla Steam Guard email code
- Progress: estimaty czasowe (brak real-time progress)
- Zero ryzyka prawnego

**Faza 2: DepotDownloader jako opt-in (lepsze UX)**
- Checkbox w ustawieniach: "Use experimental Steam downloader (community tool)"
- Legal disclaimer przy pierwszym włączeniu
- QR code authentication
- Real-time progress bar

### Uzasadnienie

**Dlaczego nie tylko SteamCMD?**
- Progress reporting jest krytyczny dla UX
- Email codes są irytujące dla userów
- Nie pasuje do standardu transparentności ustawionego przez legendary

**Dlaczego nie tylko DepotDownloader?**
- Legal gray area może odstraszyć niektórych userów
- Zawsze oferuj oficjalną opcję jako fallback
- Due diligence wobec Valve ToS

**Dlaczego hybrydowe?**
- ✅ Best of both worlds
- ✅ User choice (bezpieczeństwo vs convenience)
- ✅ Gradual rollout (start z SteamCMD, obserwuj feedback)
- ✅ Fallback jeśli Valve zmieni politykę

---

## 6. Plan Implementacji (High-Level)

### 6.1 Architektura

**Nowa klasa**: `SteamVersionManager.cs` (wzorowana na `EpicVersionManager.cs`)

```
SUSModder.Core/
└── Services/
    └── Steam/
        ├── SteamVersionManager.cs      (main orchestrator)
        ├── ISteamDownloader.cs         (interface)
        ├── SteamCmdDownloader.cs       (official implementation)
        └── DepotDownloaderImpl.cs      (community implementation)
```

**Interface pattern**:
```csharp
public interface ISteamDownloader
{
    Task AuthenticateAsync();
    Task<bool> IsAuthenticatedAsync();
    Task DownloadGameAsync(int appId, int depotId, string manifestId, string installPath);
    event Action<int, string>? ProgressChanged;
}
```

### 6.2 User Settings

**Dodać do `user-settings.json`**:
```json
{
  "steamDownloader": "steamcmd" | "depotdownloader",
  "steamCredentialsPath": "%APPDATA%\\SUSModder\\steam-auth"
}
```

**UI - nowy panel w `AppSettingsViewModel`**:
```
┌─────────────────────────────────────────┐
│ Steam Integration                        │
├─────────────────────────────────────────┤
│ ○ SteamCMD (Official, recommended)      │
│   • Email-based authentication           │
│   • Fully compliant with Steam ToS       │
│                                           │
│ ○ DepotDownloader (Experimental)        │
│   • QR code authentication (faster)      │
│   • Real-time progress                   │
│   • Community tool (use at your risk)   │
└─────────────────────────────────────────┘
```

### 6.3 Authentication Flow

**SteamCMD** (email-based):
```
1. User wybiera mod do instalacji
2. Jeśli brak auth → pokazuje dialog:
   "SUSModder needs Steam credentials to download Among Us files.

    You'll receive Steam Guard code via email.

    [Username: _______]
    [Password: _______]
    [Continue]"

3. SteamCmd prompt w embedded console
4. User wpisuje code z emaila
5. Sesja zapisana (kolejne instalacje bez promptu)
```

**DepotDownloader** (QR code):
```
1. User wybiera mod do instalacji
2. Jeśli brak auth → pokazuje dialog z QR code:
   "Scan this QR code with Steam mobile app

    █████████████████
    █████████████████
    █████████████████

    [Open Steam App] [Cancel]"

3. User scanuje → instant auth
4. Sesja zapisana
```

### 6.4 Progress Reporting

**SteamCMD** (limited):
```csharp
// Fallback: estimaty bez real-time progress
var estimatedTime = CalculateEstimatedTime(appSize);
ProgressChanged?.Invoke(0, "Starting download...");
// ... download w tle
ProgressChanged?.Invoke(100, "Download complete");
```

**DepotDownloader** (full):
```csharp
DepotDownloaderLib.onConsoleOutput += (line) => {
    // "Progress: 45.2% - Downloading Among Us.exe"
    var match = Regex.Match(line, @"(\d+\.\d+)%.*");
    if (match.Success) {
        int percentage = (int)double.Parse(match.Groups[1].Value);
        ProgressChanged?.Invoke(percentage, line);
    }
};
```

### 6.5 Dependency Management

**SteamCMD**:
```csharp
// Download on first use (jak legendary.exe w EpicVersionManager)
private async Task EnsureSteamCmdInstalledAsync()
{
    if (!File.Exists(steamCmdPath))
    {
        await DownloadSteamCmdAsync();
        await ExtractToToolsDirectory();
    }
}
```

**DepotDownloader**:
```xml
<!-- SUSModder.Core.csproj -->
<PackageReference Include="DepotDownloaderLib" Version="1.1.1" />
```

### 6.6 Migration Plan

**Existing users** (korzystali z własnego repo):
1. Wykryj first-time Steam integration
2. Pokaż one-time wizard:
   ```
   "SUSModder now downloads games directly from Steam!

    Choose your preferred method:
    • SteamCMD (Recommended) - Official Valve tool
    • DepotDownloader - Faster UX, community tool

    You can change this anytime in Settings."
   ```
3. Zapisz wybór → następne instalacje seamless

---

## 7. Trade-offs i Ryzyka

### Performance

| Metoda | Download Speed | Progress Accuracy | Startup Time |
|--------|----------------|-------------------|--------------|
| SteamCMD | ~100 Mbit/s | Brak | ~5s |
| DepotDownloader | ~10-15 Mbit/s | 100% | ~2s |
| Steam Client | ~100 Mbit/s | 100% | N/A |

**Implikacja**: DepotDownloader jest wolniejszy, ale dla Among Us (~500MB) różnica to ~30s vs ~5min - akceptowalne.

### Legal

| Metoda | EULA Risk | Account Ban Risk | Reputation Risk |
|--------|-----------|------------------|-----------------|
| SteamCMD | ✅ Zero | ✅ Zero | ✅ Zero |
| DepotDownloader | ⚠️ Gray area | ✅ Praktycznie zero | ⚠️ Teoretyczne |

**Mitigacja**:
- Legal disclaimer w UI
- Domyślnie SteamCMD (safe choice)
- Transparentność: "This is a community tool, not officially supported by Valve"

### User Experience

**SteamCMD Pain Points**:
- Email checking (interrupt flow)
- Ręczne wpisywanie code (typo risk)
- Brak progress feedback (anxiety)

**DepotDownloader Advantages**:
- QR scan = 5 sekund
- Real-time progress bar
- Feels modern

**Recommendation**: Pokazuj video tutorial dla obu metod w UI.

---

## 8. Przykłady Użycia (Code Sketches)

### 8.1 SteamVersionManager - Factory Pattern

```csharp
public class SteamVersionManager
{
    private readonly ISteamDownloader _downloader;
    private readonly string _credentialsPath;

    public SteamVersionManager(string downloaderType)
    {
        _credentialsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SUSModder", "steam-auth"
        );

        _downloader = downloaderType switch
        {
            "steamcmd" => new SteamCmdDownloader(_credentialsPath),
            "depotdownloader" => new DepotDownloaderImpl(_credentialsPath),
            _ => throw new ArgumentException($"Unknown downloader: {downloaderType}")
        };

        _downloader.ProgressChanged += OnProgressChanged;
    }

    public async Task DownloadAmongUsAsync(string version, string installPath)
    {
        // Ensure authenticated
        if (!await _downloader.IsAuthenticatedAsync())
        {
            await _downloader.AuthenticateAsync();
        }

        // Among Us App ID: 945360
        // Steam Depot: 945361
        string manifestId = await GetManifestIdForVersionAsync(version);

        await _downloader.DownloadGameAsync(
            appId: 945360,
            depotId: 945361,
            manifestId: manifestId,
            installPath: installPath
        );
    }

    private void OnProgressChanged(int percentage, string message)
    {
        // Forward to UI
        ProgressReporter?.Report(percentage, message);
    }
}
```

### 8.2 SteamCmdDownloader Implementation

```csharp
public class SteamCmdDownloader : ISteamDownloader
{
    private readonly string _steamCmdPath;
    private readonly string _credentialsPath;

    public event Action<int, string>? ProgressChanged;

    public async Task AuthenticateAsync()
    {
        // Show UI prompt for username/password
        var credentials = await UserInteraction.PromptForCredentialsAsync(
            "Steam Login Required",
            "You'll receive a Steam Guard code via email."
        );

        // Run SteamCMD login
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _steamCmdPath,
                Arguments = $"+login {credentials.Username}",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };

        process.Start();

        // Wait for password prompt
        await WaitForPromptAsync(process, "password:");
        await process.StandardInput.WriteLineAsync(credentials.Password);

        // Wait for Steam Guard prompt
        await WaitForPromptAsync(process, "Steam Guard code:");
        var guardCode = await UserInteraction.PromptForSteamGuardCodeAsync();
        await process.StandardInput.WriteLineAsync(guardCode);

        await process.WaitForExitAsync();

        // Verify sentry files created
        if (!Directory.Exists(Path.Combine(_credentialsPath, "ssfn*")))
        {
            throw new AuthenticationException("Steam Guard authentication failed");
        }
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        // Check for sentry files
        return Directory.GetFiles(_credentialsPath, "ssfn*").Length > 0;
    }

    public async Task DownloadGameAsync(int appId, int depotId,
                                       string manifestId, string installPath)
    {
        ProgressChanged?.Invoke(0, "Starting download...");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _steamCmdPath,
                Arguments = $"+login <username> " +
                          $"+download_depot {appId} {depotId} {manifestId} " +
                          $"+quit",
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };

        process.Start();

        // NOTE: Progress parsing unreliable, use estimaty
        await process.WaitForExitAsync();

        ProgressChanged?.Invoke(100, "Download complete");

        // Move files to installPath
        await MoveDepotFilesToDestinationAsync(installPath);
    }
}
```

### 8.3 DepotDownloaderImpl Implementation

```csharp
public class DepotDownloaderImpl : ISteamDownloader
{
    private readonly string _credentialsPath;
    public event Action<int, string>? ProgressChanged;

    public async Task AuthenticateAsync()
    {
        // Show QR code dialog
        var qrDialog = new QrCodeAuthDialog();
        qrDialog.Show();

        // Run DepotDownloader with QR flag
        string[] args = { "-qr", "-remember-password" };

        DepotDownloaderLib.onConsoleOutput += (line) => {
            // DepotDownloader prints QR code to console
            // Parse and display in dialog
            if (line.Contains("█"))
            {
                qrDialog.UpdateQrCode(line);
            }

            // Detect successful auth
            if (line.Contains("Login succeeded"))
            {
                qrDialog.Close();
            }
        };

        await DepotDownloaderLib.StartDownload(args);
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        // Check for AccountSettingsStore file
        var settingsFile = Path.Combine(_credentialsPath, "AccountSettingsStore");
        return File.Exists(settingsFile);
    }

    public async Task DownloadGameAsync(int appId, int depotId,
                                       string manifestId, string installPath)
    {
        string[] args = {
            "-app", appId.ToString(),
            "-depot", depotId.ToString(),
            "-manifest", manifestId,
            "-dir", installPath,
            "-validate"
        };

        DepotDownloaderLib.onConsoleOutput += (line) => {
            // "Progress: 45.2% - Downloading Among Us.exe"
            var match = Regex.Match(line, @"(\d+\.\d+)%");
            if (match.Success)
            {
                int percentage = (int)double.Parse(match.Groups[1].Value);
                ProgressChanged?.Invoke(percentage, line);
            }
        };

        await DepotDownloaderLib.StartDownload(args, useBackgroundWorker: true);
    }
}
```

### 8.4 UI Integration w MainWindowViewModel

```csharp
private async Task InstallSteamModAsync(ModConfiguration mod)
{
    var downloaderType = ConfigManager.GetSteamDownloaderType(); // "steamcmd" | "depotdownloader"
    var steamManager = new SteamVersionManager(downloaderType);

    steamManager.ProgressChanged += (percentage, message) => {
        DispatcherQueue.TryEnqueue(() => {
            InstallProgressPercentage = percentage;
            InstallProgressText = message;
        });
    };

    try
    {
        string vanillaPath = Path.Combine(PathSettings.ModsInstallPath, "Among Us (Vanilla)");
        await steamManager.DownloadAmongUsAsync(mod.RequiredGameVersion, vanillaPath);

        // Kontynuuj z instalacją moda (existing logic)
        await ModManager.ModifyAsync(mod, vanillaPath);
    }
    catch (AuthenticationException ex)
    {
        await ShowErrorDialogAsync("Steam Authentication Failed", ex.Message);
    }
    catch (Exception ex)
    {
        await ShowErrorDialogAsync("Download Failed", ex.Message);
    }
}
```

---

## 9. Następne Kroki

### Immediate (Dokumentacja - DONE)
✅ Ten dokument jako PoC

### Phase 1: Proof of Concept (1-2 dni)
- [ ] Scaffold `SteamVersionManager.cs` bez pełnej implementacji
- [ ] Prototyp `SteamCmdDownloader` (basic login flow)
- [ ] Manual testing: czy SteamCMD działa z Among Us
- [ ] Verify: manifest IDs dla różnych wersji Among Us

### Phase 2: MVP Implementation (3-5 dni)
- [ ] Pełna implementacja `SteamCmdDownloader`
- [ ] UI dla Steam login (username/password/guard code prompts)
- [ ] Integracja z `ModManager.ModifyAsync()`
- [ ] Error handling (network issues, invalid credentials, etc.)
- [ ] Testing z prawdziwymi Steam credentials

### Phase 3: DepotDownloader Integration (2-3 dni)
- [ ] Implementacja `DepotDownloaderImpl`
- [ ] QR code dialog w Avalonia
- [ ] Toggle w `AppSettingsViewModel`
- [ ] Legal disclaimer dialog
- [ ] Side-by-side testing obu metod

### Phase 4: Polish & Documentation (1-2 dni)
- [ ] Video tutorials dla obu metod auth
- [ ] Migration wizard dla existing users
- [ ] Update CLAUDE.md z nową architekturą
- [ ] User documentation (Polish & English)

**Total estimated time**: 7-12 dni roboczych

---

## 10. Open Questions

1. **Manifests**: Gdzie zdobyć manifest IDs dla różnych wersji Among Us?
   - Opcja A: Manual lookup przez DepotDownloader
   - Opcja B: Database na backendzie (podobnie jak Epic manifests w `whichtwix/Data`)

2. **Fallback**: Co jeśli user nie ma Steam mobile app (dla QR code)?
   - DepotDownloader ma flagę `-no-mobile` → manual 2FA code
   - Trzeba obsłużyć obie ścieżki w UI

3. **Credentials Security**: Jak bezpiecznie przechowywać Steam credentials?
   - Windows Credential Manager?
   - Encrypted local storage?
   - Current approach: rely na native credential storage (sentry files dla SteamCMD, AccountSettingsStore dla DD)

4. **Multi-account**: Czy supportować wielu userów Steam na jednym PC?
   - Probably overkill dla v1
   - Można dodać później jako advanced feature

5. **Epic → Steam Migration**: Jak obsłużyć userów którzy mają Epic, a teraz chcą Steam?
   - Detect obie platformy
   - Pozwól wybierać per mod installation?

---

## 11. Resources

### Documentation
- SteamCMD Wiki: https://developer.valvesoftware.com/wiki/SteamCMD
- DepotDownloader GitHub: https://github.com/SteamRE/DepotDownloader
- SteamKit2 (underlying library): https://github.com/SteamRE/SteamKit

### NuGet Packages
- `koskit.SteamCmdFluentApi` - SteamCMD wrapper
- `DepotDownloaderLib` v1.1.1 - DepotDownloader library (.NET 7)
- `SteamKit2` - If rolling custom solution

### Similar Projects
- Legendary (Epic Games CLI): https://github.com/derrod/legendary
- Heroic Games Launcher: https://github.com/Heroic-Games-Launcher/HeroicGamesLauncher (używa legendary)
- Lutris: https://github.com/lutris/lutris (multi-platform game manager)

---

## Podsumowanie

**Rekomendowane podejście**: **Hybrydowe (SteamCMD domyślnie + DepotDownloader opt-in)**

**Uzasadnienie**:
1. ✅ **Legal safety**: SteamCMD jako oficjalna opcja
2. ✅ **User choice**: Zaawansowani userzy mogą wybrać lepsze UX
3. ✅ **Gradual rollout**: Start z SteamCMD, obserwuj feedback
4. ✅ **Fallback**: Jeśli Valve zmieni politykę, mamy oficial option
5. ✅ **Best of both**: Kompromis między bezpieczeństwem a convenience

**Next action**: Review tego dokumentu → decyzja o rozpoczęciu Phase 1 (scaffold + PoC).
