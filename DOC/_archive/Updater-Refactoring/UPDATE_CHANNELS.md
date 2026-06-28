# System Kanałów Aktualizacji (v2.2.0)

## Przegląd

SUSModder v2.2.0 wprowadza system dwóch kanałów aktualizacji, pozwalający użytkownikom wybierać między stabilnymi wydaniami a wersjami testowymi.

## Kanały

### Release (Stabilne)
- **Kod kanału**: `release`
- **Opis**: Stabilne, przetestowane wydania produkcyjne
- **Dla kogo**: Wszyscy użytkownicy końcowi
- **Domyślny**: Tak

### Beta (Testowe)
- **Kod kanału**: `beta`
- **Opis**: Wersje testowe z nowymi funkcjami i poprawkami
- **Dla kogo**: Testerzy, zaawansowani użytkownicy
- **Wersja**: Automatycznie dodawany sufiks `-beta` (np. `2.2.0-beta`)

## Architektura

### 1. Warstwa danych
**Plik**: `SUSModder.Core/Configuration/UserSettings.cs`

```csharp
public class UserSettings
{
    [JsonPropertyName("updateChannel")]
    public string UpdateChannel { get; set; } = "release";
}
```

Kanał przechowywany w `%APPDATA%\SUSModder\user-settings.json`, NIE jest nadpisywany podczas aktualizacji.

### 2. Warstwa serwisowa
**Plik**: `SUSModder.Core/Services/VelopackUpdateService.cs`

```csharp
public class VelopackUpdateService
{
    private string GetUpdateChannel()
    {
        var userSettings = _userSettingsService.LoadUserSettings();
        var channel = userSettings.UpdateChannel;

        // Walidacja: tylko "release" lub "beta"
        if (channel != "release" && channel != "beta")
            return "release";

        return channel;
    }
}
```

Podczas inicjalizacji `UpdateManager`:
```csharp
var updateOptions = new UpdateOptions
{
    ExplicitChannel = updateChannel
};
_updateManager = new UpdateManager(_apiSource, updateOptions);
```

### 3. Warstwa UI
**Plik**: `SUSModder/ViewModels/AppSettingsViewModel.cs`

```csharp
public class UpdateChannelOption
{
    public string Code { get; set; }
    public string DisplayName { get; set; }
}

private static readonly List<UpdateChannelOption> _availableUpdateChannels = new()
{
    new UpdateChannelOption { Code = "release", DisplayName = "Release (Stabilne wydania)" },
    new UpdateChannelOption { Code = "beta", DisplayName = "Beta (Wersje testowe)" }
};
```

**Lokalizacja w UI**: Ustawienia → Zaawansowane → Kanał aktualizacji (pod opcją Tryb dewelopera)

### 4. Warstwa API
**Endpoint**: `https://susmodder.app/api/releases?channel={release|beta}`

Odpowiedź:
```json
{
  "success": true,
  "channel": "release",
  "latestVersion": "2.2.0",
  "manifest": {
    "LatestVersion": "2.2.0",
    "Releases": [{
      "Version": "2.2.0",
      "File": "SUSModder-2.2.0-release-full.nupkg",
      "SHA256": "...",
      "Channel": "release"
    }]
  },
  "downloadBaseUrl": "https://susmodder.app/releases/release"
}
```

## Proces budowania

### Skrypt: `build-dual-channel.ps1`

#### Podstawowe użycie
```powershell
# Zbuduj oba kanały
.\build-dual-channel.ps1 -Version 2.2.0
```

#### Zaawansowane opcje
```powershell
# Tylko release
.\build-dual-channel.ps1 -Version 2.2.0 -SkipBeta

# Tylko beta
.\build-dual-channel.ps1 -Version 2.2.0 -SkipRelease

# Niestandardowy sufiks beta
.\build-dual-channel.ps1 -Version 2.2.0 -BetaSuffix "rc1"
```

### Wyjście skryptu
```
releases-release/
├── SUSModder-2.2.0-release-full.nupkg
├── RELEASES
└── releases.release.json

releases-beta/
├── SUSModder-2.2.0-beta-beta-full.nupkg
├── RELEASES
└── releases.beta.json
```

### Proces wydawania

1. **Zbuduj oba kanały**
   ```powershell
   .\build-dual-channel.ps1 -Version 2.2.0
   ```

2. **Wgraj pliki na serwer**
   - Release: `releases-release/*` → `https://susmodder.app/releases/release/`
   - Beta: `releases-beta/*` → `https://susmodder.app/releases/beta/`

3. **Zaktualizuj API**
   Backend musi zwracać właściwy manifest w zależności od parametru `channel`:
   - `?channel=release` → manifest release
   - `?channel=beta` → manifest beta

4. **Testowanie**
   - Przetestuj aktualizację z kanału release
   - Przetestuj aktualizację z kanału beta
   - Sprawdź przełączanie między kanałami

## Zmiana kanału przez użytkownika

### Krok po kroku
1. Użytkownik otwiera **Ustawienia**
2. Przechodzi do sekcji **Zaawansowane**
3. Wybiera kanał z rozwijanej listy **Kanał aktualizacji**
4. Kliknij **Zapisz**
5. Przy następnym sprawdzaniu aktualizacji zostanie użyty nowy kanał

### Implementacja
```csharp
// Zapisanie wyboru
_userSettingsService.UpdateUserSetting(settings =>
{
    settings.UpdateChannel = UpdateChannel;
});

// UpdateManager jest resetowany
_apiSource?.Dispose();
_updateManager = null;

// Przy następnym CheckForUpdateAsync zostanie utworzony nowy UpdateManager z nowym kanałem
```

## Migracja z poprzednich wersji

### Użytkownicy z wersji < 2.2.0
- Domyślnie ustawiony kanał: `release`
- Brak wymaganej akcji ze strony użytkownika
- Mogą zmienić kanał w każdej chwili w ustawieniach

### Backend
Backend musi obsługiwać zarówno:
- Stary format: `?channel=win` (backward compatibility)
- Nowy format: `?channel=release` lub `?channel=beta`

Sugerowana implementacja:
```javascript
function normalizeChannel(channel) {
  // Backward compatibility
  if (channel === 'win') return 'release';

  // Walidacja
  if (channel === 'release' || channel === 'beta') return channel;

  // Domyślny
  return 'release';
}
```

## Testowanie

### Test 1: Instalacja z release
1. Zainstaluj aplikację z kanału release
2. Sprawdź `user-settings.json` → `updateChannel: "release"`
3. Sprawdź aktualizacje → powinien znaleźć najnowszą wersję release

### Test 2: Zmiana na beta
1. Otwórz ustawienia
2. Zmień kanał na beta
3. Sprawdź aktualizacje → powinien znaleźć najnowszą wersję beta
4. Zaktualizuj → aplikacja powinna zaktualizować się do wersji beta

### Test 3: Powrót na release
1. W wersji beta zmień kanał na release
2. Sprawdź aktualizacje
3. Jeśli wersja release jest nowsza (lub równa), powinien zaproponować "aktualizację"

### Test 4: Ta sama wersja w obu kanałach
1. Release: 2.2.0
2. Beta: 2.2.0-beta
3. Zmiana z release na beta → zaproponuje 2.2.0-beta
4. Zmiana z beta na release → zaproponuje 2.2.0

## Znane ograniczenia

1. **Downgrade**: Przejście z beta na release może wyglądać jak "downgrade" (np. 2.2.0-beta → 2.2.0)
   - Velopack obsługuje to poprawnie
   - Użytkownik otrzyma komunikat o dostępnej "aktualizacji"

2. **Numeracja wersji**: Beta zawsze ma sufiks `-beta`
   - Nie można mieć beta bez sufiksu
   - Sufiks jest dodawany automatycznie przez skrypt

3. **API cache**: Backend może cachować odpowiedzi
   - Należy upewnić się, że cache jest osobny dla każdego kanału
   - Sugerowany klucz cache: `releases:{channel}`

## FAQ

**Q: Czy mogę mieć tę samą wersję w obu kanałach?**
A: Tak, ale wersja beta będzie miała sufiks (np. 2.2.0 vs 2.2.0-beta).

**Q: Co się stanie jeśli usunę kanał beta?**
A: Użytkownicy na kanale beta nie otrzymają aktualizacji. Powinni zmienić kanał na release.

**Q: Czy mogę mieć więcej niż 2 kanały?**
A: Tak, wystarczy dodać nowe opcje do `_availableUpdateChannels` i rozszerzyć walidację w `GetUpdateChannel()`.

**Q: Jak cofnąć użytkownika z beta na release?**
A: Użytkownik sam musi zmienić kanał w ustawieniach. Nie ma automatycznego rollbacku.

**Q: Czy delta updates działają między kanałami?**
A: Nie. Delta updates działają tylko w obrębie tego samego kanału. Zmiana kanału wymaga pobrania full package.

## Struktura plików

```
SUSModder/
├── build-dual-channel.ps1                          # Skrypt budowania
├── CLAUDE.md                                       # Główna dokumentacja (zaktualizowana)
├── DOC/
│   └── Updater-Refactoring/
│       └── UPDATE_CHANNELS.md                      # Ten dokument
├── SUSModder/
│   ├── ViewModels/
│   │   └── AppSettingsViewModel.cs                 # UI dla wyboru kanału
│   └── Views/
│       └── AppSettingsView.axaml                   # Widok ustawień
└── SUSModder.Core/
    ├── Configuration/
    │   └── UserSettings.cs                         # Model z polem UpdateChannel
    └── Services/
        ├── UserSettingsService.cs                  # Serwis do zarządzania ustawieniami
        ├── VelopackUpdateService.cs                # Logika aktualizacji z obsługą kanałów
        └── VelopackApiSource.cs                    # Źródło danych dla Velopack (przekazuje channel)
```

## Changelog

### v2.2.0 (2025-11-06)
- ✨ Dodano system dwóch kanałów aktualizacji (release/beta)
- ✨ Dodano UI do wyboru kanału w ustawieniach
- ✨ Dodano skrypt `build-dual-channel.ps1` do budowania obu kanałów
- 🔧 Zaktualizowano `VelopackUpdateService` aby używał kanału z user settings
- 📝 Zaktualizowano dokumentację
