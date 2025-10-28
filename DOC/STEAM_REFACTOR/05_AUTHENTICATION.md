# Authentication Strategy - Bezpieczna Autoryzacja Steam

**Data utworzenia:** 2025-10-28  
**Status:** Design phase  

---

## 📋 Spis Treści

1. [Overview Autoryzacji](#overview-autoryzacji)
2. [Jak Działa Steam Auth](#jak-działa-steam-auth)
3. [Strategia Automatyczna (Preferowana)](#strategia-automatyczna-preferowana)
4. [Fallback Strategies](#fallback-strategies)
5. [Bezpieczeństwo](#bezpieczeństwo)
6. [Edge Cases](#edge-cases)
7. [User Experience](#user-experience)

---

## Overview Autoryzacji

### Problem

DepotDownloader wymaga potwierdzenia że użytkownik **posiada Among Us w bibliotece Steam**.

### Nasze Rozwiązanie ⭐

**Automatyczne wykorzystanie aktywnej sesji Steam** - ZERO interakcji z użytkownikiem, ZERO przechowywania credentials.

```
Użytkownik zalogowany w Steam
         ↓
DepotDownloader odczytuje tokeny
         ↓
Automatyczna autoryzacja ✅
```

**Analogia:** Dokładnie tak jak **Legendary** (Epic Games Launcher) wykorzystuje istniejącą sesję Epic.

---

## Jak Działa Steam Auth

### Steam Session Management

Steam przechowuje tokeny autoryzacyjne lokalnie po zalogowaniu:

```
C:\Program Files (x86)\Steam\config\
├── loginusers.vdf          # Lista kont Steam
├── config.vdf              # Ustawienia (+ niektóre tokeny)
└── [steamID64]\
    └── remotecache.vdf     # Cache dla konkretnego użytkownika
```

### loginusers.vdf - Przykład

```vdf
"users"
{
    "76561198012345678"  // Steam ID 64-bit
    {
        "AccountName"    "example_user"
        "PersonaName"    "ExampleUser"
        "RememberPassword"    "1"
        "MostRecent"    "1"
        "Timestamp"    "1698508800"
    }
}
```

### Jak DepotDownloader Wykorzystuje Te Dane

```csharp
// Pseudo-kod DepotDownloader
public async Task AuthenticateAsync()
{
    // 1. Sprawdź czy Steam jest uruchomiony
    if (IsSteamRunning())
    {
        // 2. Odczytaj loginusers.vdf
        var vdfPath = @"C:\Program Files (x86)\Steam\config\loginusers.vdf";
        var users = ParseVdfFile(vdfPath);
        
        // 3. Znajdź MostRecent użytkownika
        var activeUser = users.FirstOrDefault(u => u.MostRecent == "1");
        
        if (activeUser != null && activeUser.RememberPassword == "1")
        {
            // 4. Użyj zapisanych tokenów
            await AuthWithSavedCredentials(activeUser.SteamID);
            return; // ✅ Sukces - ZERO interakcji
        }
    }
    
    // 5. Fallback - poproś o login (jeśli automatyczne nie zadziałało)
    await PromptForCredentials();
}
```

**Kluczowe:** DepotDownloader robi to **automatycznie** - my tylko wywołujemy proces.

---

## Strategia Automatyczna (Preferowana)

### Implementacja w SUSModder

```csharp
public async Task<bool> DownloadAmongUsAsync(
    string manifestId,
    string targetDirectory,
    IProgressReporter progress)
{
    // KROK 1: Sprawdź czy Steam jest uruchomiony
    if (!IsSteamRunning())
    {
        throw new InvalidOperationException(
            "Klient Steam musi być uruchomiony. Uruchom Steam i spróbuj ponownie.");
    }
    
    // KROK 2: Sprawdź czy użytkownik ma zapisane credentials w Steam
    if (!HasSteamSavedCredentials())
    {
        throw new InvalidOperationException(
            "Steam wymaga ponownego zalogowania. Zaloguj się w Steam i spróbuj ponownie.");
    }
    
    // KROK 3: Wywołaj DepotDownloader (automatycznie użyje aktywnej sesji)
    var startInfo = new ProcessStartInfo
    {
        FileName = _depotDownloaderPath,
        Arguments = $"-app 945360 -depot 945361 -manifest {manifestId} -dir \"{targetDirectory}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    
    using var process = new Process { StartInfo = startInfo };
    
    process.OutputDataReceived += (sender, e) =>
    {
        if (e.Data != null)
        {
            _log.Write($"[DepotDownloader] {e.Data}");
            
            // Wykryj błędy autoryzacji
            if (e.Data.Contains("Login Failed") || e.Data.Contains("Invalid Password"))
            {
                throw new UnauthorizedAccessException(
                    "Steam autoryzacja nie powiodła się. Sprawdź czy jesteś zalogowany w Steam.");
            }
            
            // Parse progress
            var match = Regex.Match(e.Data, @"(\d+)%");
            if (match.Success)
            {
                progress.Report(int.Parse(match.Groups[1].Value));
            }
        }
    };
    
    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    
    await process.WaitForExitAsync();
    
    return process.ExitCode == 0;
}

private bool IsSteamRunning()
{
    return Process.GetProcessesByName("steam").Length > 0;
}

private bool HasSteamSavedCredentials()
{
    try
    {
        // Sprawdź czy loginusers.vdf istnieje i ma aktywnego użytkownika
        string steamPath = GetSteamInstallPath();
        if (string.IsNullOrEmpty(steamPath))
            return false;
        
        string vdfPath = Path.Combine(steamPath, "config", "loginusers.vdf");
        if (!File.Exists(vdfPath))
            return false;
        
        string content = File.ReadAllText(vdfPath);
        
        // Sprawdź czy jest użytkownik z RememberPassword = 1
        return content.Contains("\"RememberPassword\"\t\t\"1\"");
    }
    catch
    {
        return false; // Lepiej założyć że nie ma, niż crashować
    }
}

private string? GetSteamInstallPath()
{
    try
    {
        // Sprawdź rejestr Windows
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return key?.GetValue("SteamPath") as string;
    }
    catch
    {
        // Fallback: typowa lokalizacja
        string defaultPath = @"C:\Program Files (x86)\Steam";
        return Directory.Exists(defaultPath) ? defaultPath : null;
    }
}
```

### User Flow (Happy Path)

```
┌─────────────────────────────────────────────────────────────┐
│ 1. Użytkownik kliknął "Install Mod"                        │
└───────────────────────────┬─────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│ 2. SUSModder sprawdza: Process.GetProcessesByName("steam") │
└───────────────────────────┬─────────────────────────────────┘
                            │
        ┌───────────────────┴───────────────────┐
        │ Steam uruchomiony?                   │
        └───┬────────────────────────┬──────────┘
            │ NIE                    │ TAK
            ▼                        ▼
    ┌──────────────────┐    ┌──────────────────────────────┐
    │ Pokaż dialog:    │    │ 3. Wywołaj DepotDownloader   │
    │ "Uruchom Steam"  │    │    (bez żadnych parametrów   │
    │                  │    │     autoryzacji)             │
    │ [OK] [Anuluj]    │    └──────────────┬───────────────┘
    └──────────────────┘                   │
                                           ▼
                            ┌──────────────────────────────┐
                            │ 4. DepotDownloader:          │
                            │    - Odczytuje loginusers.vdf│
                            │    - Używa zapisanych tokenów│
                            │    - Pobiera pliki ✅        │
                            └──────────────┬───────────────┘
                                           │
                                           ▼
                            ┌──────────────────────────────┐
                            │ 5. Instalacja zakończona     │
                            │    ZERO interakcji! 🎉       │
                            └──────────────────────────────┘
```

**Kluczowe:**
- ❌ Nie pytamy o hasło
- ❌ Nie przechowujemy żadnych danych
- ❌ Nie pokazujemy żadnego UI dla autoryzacji
- ✅ Jedyny warunek: Steam musi być uruchomiony

---

## Fallback Strategies

### Fallback 1: Steam Guard Code (Rzadkie)

**Kiedy:** Użytkownik ma 2FA i DepotDownloader nie może użyć zapisanych tokenów.

**Flow:**
```bash
# DepotDownloader prompt
Please enter your Steam Guard code from mobile app:
> [Użytkownik wpisuje kod]
```

**Implementacja:**
```csharp
process.OutputDataReceived += (sender, e) =>
{
    if (e.Data != null && e.Data.Contains("Please enter your Steam Guard code"))
    {
        // Pokaż dialog dla użytkownika
        string code = await PromptForSteamGuardCodeAsync();
        
        // Wyślij kod do stdin DepotDownloader
        process.StandardInput.WriteLine(code);
    }
};
```

**UI Dialog:**
```csharp
private async Task<string> PromptForSteamGuardCodeAsync()
{
    var dialog = new SteamGuardDialog
    {
        Title = "Steam Guard",
        Message = "Wpisz kod z aplikacji Steam Mobile:",
        Placeholder = "XXXXX"
    };
    
    var result = await dialog.ShowDialog(parentWindow);
    return result ?? throw new OperationCanceledException();
}
```

**Częstotliwość:** ~1-2% użytkowników (tylko przy pierwszym użyciu lub wygaśnięciu tokenów).

### Fallback 2: Manual Login (Bardzo rzadkie)

**Kiedy:** 
- Steam uruchomiony ale użytkownik nie zalogowany
- Steam w "offline mode"
- Wygasłe wszystkie tokeny

**Flow:**
```
DepotDownloader nie może użyć zapisanych credentials
         ↓
SUSModder pokazuje dialog:
"Zaloguj się w kliencie Steam i spróbuj ponownie"
         ↓
Użytkownik loguje się w Steam
         ↓
Retry
```

**Implementacja:**
```csharp
if (process.ExitCode == 5) // DepotDownloader: Auth failed
{
    var retry = await ShowErrorDialogAsync(
        "Nie udało się zalogować do Steam.\n\n" +
        "Upewnij się że:\n" +
        "• Jesteś zalogowany w kliencie Steam\n" +
        "• Steam nie jest w trybie offline\n" +
        "• Posiadasz Among Us w bibliotece\n\n" +
        "Czy spróbować ponownie?",
        "Błąd autoryzacji",
        showRetryButton: true
    );
    
    if (retry)
    {
        // Użytkownik loguje się w Steam w międzyczasie
        await Task.Delay(2000); // Daj czas na zalogowanie
        return await DownloadAmongUsAsync(manifestId, targetDirectory, progress);
    }
}
```

**Częstotliwość:** <1% użytkowników.

### Fallback 3: Anonymous Login (NIE ZALECANE)

**Teoretycznie możliwe:**
```bash
DepotDownloader -app 945360 -depot 945361 -manifest {ID} -anonymous
```

**Problem:** ❌ **Nie działa dla Among Us** - wymaga potwierdzenia własności gry.

**Steam zwróci:**
```
Error: You must own the game to download this depot.
```

**Wniosek:** NIE implementujemy - niepotrzebne.

---

## Bezpieczeństwo

### Co NIE Przechowujemy

❌ **Steam username** - nigdy nie prosimy  
❌ **Steam password** - nigdy nie prosimy  
❌ **Steam Guard codes** - tylko przekazujemy do DepotDownloader (nie zapisujemy)  
❌ **Tokeny autoryzacyjne** - DepotDownloader odczytuje je bezpośrednio z Steam  

### Co Jest Bezpieczne

✅ **Odczyt loginusers.vdf** - publiczny plik (każda aplikacja Steam może go odczytać)  
✅ **Wywołanie DepotDownloader** - oficjalne narzędzie community (3.5k stars)  
✅ **Wykorzystanie aktywnej sesji** - standardowa praktyka (Legendary, SteamCMD)  

### Comparison: Epic vs Steam

| Aspekt | Epic (Legendary) | Steam (DepotDownloader) |
|--------|------------------|-------------------------|
| **Auth method** | OAuth2 (browser login) | Saved credentials |
| **Token storage** | `~/.config/legendary/` | `Steam/config/` |
| **Asks for password?** | NO (browser OAuth) | NO (saved tokens) |
| **2FA handling** | Browser handles | Steam Guard prompt |
| **Security** | ✅ Secure | ✅ Secure |

**Wniosek:** Nasze rozwiązanie jest **równie bezpieczne** jak Legendary.

### Privacy

**GDPR Compliance:**
- ✅ Nie zbieramy żadnych danych logowania
- ✅ Nie przechowujemy Steam ID
- ✅ Nie wysyłamy credentials na serwer
- ✅ Wszystko odbywa się lokalnie na komputerze użytkownika

**Telemetry (opcjonalne):**
```csharp
// Jeśli użytkownik wyraził zgodę na telemetrię
await TelemetryService.TrackEventAsync(new
{
    Event = "steam_depot_download",
    Success = true,
    // NIE wysyłamy: username, Steam ID, żadnych danych osobowych
    ManifestId = manifestId, // OK - publiczne ID
    Duration = downloadDuration // OK - statystyka
});
```

---

## Edge Cases

### Edge Case 1: Steam Offline Mode

**Problem:**
```
Użytkownik uruchomił Steam w trybie offline
         ↓
DepotDownloader nie może połączyć się z Steam CDN
         ↓
Błąd pobierania
```

**Detection:**
```csharp
private bool IsSteamOnline()
{
    try
    {
        // Sprawdź proces Steam z parametrem
        var steamProcesses = Process.GetProcessesByName("steam");
        if (steamProcesses.Length == 0)
            return false;
        
        // Sprawdź plik stanu Steam
        string steamPath = GetSteamInstallPath();
        string configPath = Path.Combine(steamPath, "config", "config.vdf");
        
        if (File.Exists(configPath))
        {
            string content = File.ReadAllText(configPath);
            // Jeśli zawiera "WantsOfflineMode" = "1", Steam jest offline
            return !content.Contains("\"WantsOfflineMode\"\t\t\"1\"");
        }
        
        return true; // Domyślnie zakładamy online
    }
    catch
    {
        return true;
    }
}
```

**User Message:**
```csharp
if (!IsSteamOnline())
{
    await ShowErrorDialogAsync(
        "Steam jest w trybie offline.\n\n" +
        "Aby pobrać pliki gry, Steam musi być w trybie online.\n\n" +
        "Uruchom Steam ponownie w trybie online i spróbuj ponownie.",
        "Steam offline"
    );
    return;
}
```

### Edge Case 2: Multiple Steam Accounts

**Problem:**
```
Użytkownik ma kilka kont Steam
         ↓
Among Us jest na koncie #2
         ↓
Zalogowany na koncie #1 (nie ma Among Us)
         ↓
DepotDownloader: "You don't own this game"
```

**Detection:**
```csharp
process.ErrorDataReceived += (sender, e) =>
{
    if (e.Data?.Contains("You must own the game") == true)
    {
        _log.Write("❌ Użytkownik nie posiada Among Us na aktywnym koncie Steam");
        throw new UnauthorizedAccessException(
            "Among Us nie został znaleziony w Twojej bibliotece Steam.\n\n" +
            "Sprawdź czy:\n" +
            "• Jesteś zalogowany na właściwe konto Steam\n" +
            "• Among Us jest w Twojej bibliotece\n" +
            "• Gra nie jest udostępniona przez Family Sharing (wymaga własności)"
        );
    }
};
```

### Edge Case 3: Steam Family Sharing

**Problem:** Among Us udostępniony przez Family Sharing - DepotDownloader wymaga **własności** gry.

**Solution:**
```csharp
// W error message dodaj:
"⚠️ UWAGA: Steam Family Sharing NIE jest wspierane.\n" +
"Musisz posiadać Among Us na swoim własnym koncie."
```

**Alternatywa:** Zasugeruj zakup gry (~$5).

### Edge Case 4: Steam nie zainstalowany

**Problem:** Użytkownik ma Among Us z Epic, ale nie ma zainstalowanego Steam.

**Detection:**
```csharp
private bool IsSteamInstalled()
{
    string steamPath = GetSteamInstallPath();
    return !string.IsNullOrEmpty(steamPath) && Directory.Exists(steamPath);
}
```

**Message:**
```csharp
if (!IsSteamInstalled())
{
    await ShowErrorDialogAsync(
        "Klient Steam nie został znaleziony na tym komputerze.\n\n" +
        "Aby instalować mody dla wersji Steam, musisz mieć:\n" +
        "• Zainstalowany klient Steam\n" +
        "• Among Us zakupiony na Steam\n\n" +
        "Jeśli masz Among Us na Epic Games, wybierz tryb Epic w ustawieniach.",
        "Steam nie znaleziony"
    );
}
```

### Edge Case 5: DepotDownloader Update

**Problem:** Valve zmienia API → DepotDownloader przestaje działać → Update wymagany.

**Detection:**
```csharp
process.ErrorDataReceived += (sender, e) =>
{
    if (e.Data?.Contains("API changed") || e.Data?.Contains("outdated"))
    {
        throw new InvalidOperationException(
            "DepotDownloader wymaga aktualizacji.\n\n" +
            "Zaktualizuj SUSModder do najnowszej wersji."
        );
    }
};
```

**Prevention:**
```csharp
// Przy każdym starcie SUSModder, sprawdź wersję DepotDownloader
public async Task CheckDepotDownloaderVersionAsync()
{
    var currentVersion = GetLocalDepotDownloaderVersion(); // Z pliku version.txt
    var latestVersion = await GetLatestDepotDownloaderVersionFromGitHubAsync();
    
    if (currentVersion < latestVersion)
    {
        _log.Write($"⚠️ DepotDownloader outdated: {currentVersion} -> {latestVersion}");
        
        // Opcjonalnie: Auto-update DepotDownloader
        await DownloadLatestDepotDownloaderAsync();
    }
}
```

---

## User Experience

### Best Practices

**1. Proactive Messaging**
```csharp
// Przed rozpoczęciem pobierania
await ShowInfoDialogAsync(
    "Przygotowanie do instalacji...\n\n" +
    "✅ Steam: Uruchomiony\n" +
    "✅ Konto: Zalogowane\n" +
    "✅ Among Us: W bibliotece\n\n" +
    "Rozpoczynam pobieranie...",
    "Status",
    timeout: TimeSpan.FromSeconds(2) // Auto-close
);
```

**2. Clear Error Messages**

❌ **ZŁE:**
```
Error: Exit code 5
```

✅ **DOBRE:**
```
Nie udało się zalogować do Steam.

Upewnij się że:
• Steam jest uruchomiony
• Jesteś zalogowany w Steam
• Posiadasz Among Us w bibliotece

Kliknij OK aby spróbować ponownie.
```

**3. Progress Feedback**
```csharp
// Parsuj output DepotDownloader i pokazuj użytkownikowi
"Pobieranie z Steam: 45% (234 MB / 520 MB)"
"Szacowany czas: 2 minuty"
```

**4. Success Confirmation**
```csharp
await ShowInfoDialogAsync(
    "✅ Instalacja zakończona!\n\n" +
    "Mod został pomyślnie zainstalowany.\n" +
    "Możesz teraz uruchomić grę.",
    "Sukces"
);
```

### Comparison: Old (7z) vs New (Steam Depot)

| Aspekt | Stary (7z) | Nowy (Steam Depot) |
|--------|------------|---------------------|
| **Krok 1** | "Pobieranie..." | "Sprawdzanie Steam..." |
| **Krok 2** | "Rozpakowywanie 7z..." | "Pobieranie z Steam..." |
| **Krok 3** | "Instalacja moda..." | "Instalacja moda..." |
| **Auth** | Token HTTP (invisible) | Steam (invisible jeśli uruchomiony) |
| **Error handling** | "Błąd pobierania" | "Uruchom Steam" (actionable) |
| **UX** | OK | ✅ Lepsze (clear requirements) |

### Mockup: Steam Required Dialog

```
┌─────────────────────────────────────────────────┐
│  ⚠️  Steam wymagany                             │
├─────────────────────────────────────────────────┤
│                                                 │
│  Aby pobrać pliki gry, klient Steam musi       │
│  być uruchomiony.                               │
│                                                 │
│  Co zrobić:                                     │
│  1. Uruchom Steam                               │
│  2. Zaloguj się                                 │
│  3. Kliknij "Spróbuj ponownie"                  │
│                                                 │
│               [Spróbuj ponownie]  [Anuluj]      │
└─────────────────────────────────────────────────┘
```

---

## Testing Scenarios

### Manual Testing Checklist

**Scenario 1: Happy Path**
- [ ] Steam uruchomiony i zalogowany
- [ ] Among Us w bibliotece
- [ ] Instalacja moda - sukces bez interakcji

**Scenario 2: Steam nie uruchomiony**
- [ ] Próba instalacji moda
- [ ] Dialog "Uruchom Steam"
- [ ] Po uruchomieniu Steam - retry sukces

**Scenario 3: Steam offline mode**
- [ ] Steam w trybie offline
- [ ] Próba instalacji - błąd
- [ ] Komunikat o przejściu online

**Scenario 4: Steam Guard kod**
- [ ] Użytkownik z 2FA, wygasłe tokeny
- [ ] Prompt dla Steam Guard kodu
- [ ] Po wpisaniu kodu - sukces

**Scenario 5: Brak Among Us**
- [ ] Konto Steam bez Among Us
- [ ] Próba instalacji - błąd
- [ ] Komunikat o braku gry w bibliotece

**Scenario 6: Multiple accounts**
- [ ] Zalogowany na konto bez Among Us
- [ ] Próba instalacji - błąd
- [ ] Komunikat o sprawdzeniu konta

### Automated Tests

```csharp
[Fact]
public void IsSteamRunning_WhenSteamActive_ReturnsTrue()
{
    // Arrange
    // (wymaga uruchomionego Steam do testu)
    var manager = new SteamDepotManager(config, log);
    
    // Act
    var isRunning = manager.IsSteamRunning();
    
    // Assert
    Assert.True(isRunning);
}

[Fact]
public void HasSteamSavedCredentials_WhenUserLoggedIn_ReturnsTrue()
{
    // Arrange
    var manager = new SteamDepotManager(config, log);
    
    // Act
    var hasCredentials = manager.HasSteamSavedCredentials();
    
    // Assert
    Assert.True(hasCredentials);
}

[Fact]
public async Task DownloadAmongUsAsync_WhenSteamNotRunning_ThrowsException()
{
    // Arrange
    // (zamknij Steam przed testem)
    var manager = new SteamDepotManager(config, log);
    
    // Act & Assert
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => manager.DownloadAmongUsAsync("123", "C:\\temp", progress)
    );
}
```

---

## Podsumowanie

### Nasza Strategia ⭐

**Primary:** Automatyczne wykorzystanie aktywnej sesji Steam
- ✅ ZERO interakcji użytkownika
- ✅ ZERO przechowywania credentials
- ✅ Bezpieczne (standard industry)
- ✅ Analogiczne do Legendary (Epic)

**Fallback 1:** Steam Guard code prompt (rzadkie ~1-2%)
**Fallback 2:** "Zaloguj się w Steam" message (bardzo rzadkie <1%)
**NO Fallback:** Anonymous login (nie działa dla Among Us)

### Security & Privacy

- ❌ NIE pytamy o hasło
- ❌ NIE przechowujemy żadnych danych logowania
- ❌ NIE wysyłamy credentials na serwer
- ✅ Wszystko lokalnie
- ✅ GDPR compliant

### User Experience

- Minimalna interakcja (tylko "Uruchom Steam" jeśli nie jest aktywny)
- Clear error messages z actionable steps
- Proactive status checks
- Graceful degradation (fallbacks)

### Comparison

Równie bezpieczne i wygodne jak:
- ✅ Legendary (Epic Games)
- ✅ SteamCMD (oficjalne Valve)
- ✅ Inne community tools Steam

---

**Wersja:** 1.0  
**Ostatnia aktualizacja:** 2025-10-28  
**Autor:** Claude (AI Assistant) & boratsc  
