# Discord OAuth2 — Checklista integracji po implementacji agentów

## Krok 1: Weryfikacja plików utworzonych przez agentów

### SUSModder.Core/Data/
- [ ] `DatabaseService.cs` — migracja v2 dodana
- [ ] `IDiscordAuthRepository.cs` — interfejs
- [ ] `DiscordAuthRepository.cs` — implementacja
- [ ] `ISustatsCredentialsRepository.cs` — interfejs
- [ ] `SustatsCredentialsRepository.cs` — implementacja
- [ ] `IUserSettingsRepository.cs` — rozszerzony o `UpdateSingleField`
- [ ] `UserSettingsRepository.cs` — rozszerzony o `UpdateSingleField`

### SUSModder.Core/Models/
- [ ] `DiscordTokenInfo.cs`
- [ ] `SustatsCredentials.cs`
- [ ] `DiscordGuildInfo.cs`
- [ ] `ClairOAuthConfig.cs`

### SUSModder.Core/Configuration/
- [ ] `UserSettings.cs` — +`ActiveSustatsGuildId`
- [ ] `SUStatsService.cs` — refactor

### SUSModder.Core/Services/Discord/
- [ ] `CredentialProtector.cs`
- [ ] `IDiscordOAuthService.cs`
- [ ] `DiscordOAuthService.cs`
- [ ] `IClairDiscordService.cs`
- [ ] `ClairDiscordService.cs`

### SUSModder/Services/
- [ ] `OAuthLoopbackListener.cs`

### SUSModder/
- [ ] `appsettings.json` — +`ClairApiBaseUrl`, +`ClairApiSusmodderEndpoint`
- [ ] `App.axaml.cs` — DI rejestracje

### SUSModder/ViewModels/
- [ ] `SUStatsConfigViewModel.cs` — przebudowany

### SUSModder/Views/
- [ ] `SUStatsConfigView.axaml` — przebudowany
- [ ] `SUStatsConfigView.axaml.cs` — zaktualizowany

### SUSModder/Localization/
- [ ] `pl.json` — +sekcja DiscordAuth
- [ ] `en.json` — +sekcja DiscordAuth

## Krok 2: Build i testy

### Core
```bash
dotnet build SUSModder.Core/SUSModder.Core.csproj
```
- [ ] Kompilacja bez błędów i warningów

### UI
```bash
dotnet build SUSModder/SUSModder.csproj
```
- [ ] Kompilacja bez błędów i warningów

### Full solution
```bash
dotnet build SUSModder.sln
```
- [ ] Kompilacja bez błędów

### Testy
```bash
dotnet test SUSModder.sln
```
- [ ] Wszystkie testy przechodzą

## Krok 3: Review findings

### sus-quality-reviewer
- [ ] Raport przeczytany
- [ ] Issues naprawione

### sus-senior-quality-reviewer
- [ ] Raport przeczytany
- [ ] Blokery naprawione
- [ ] Rekomendacje rozważone

### sus-security-auditor
- [ ] Raport przeczytany
- [ ] Znalezione problemy naprawione
- [ ] Compliance potwierdzony

### sus-i18n-copy-checker
- [ ] Wszystkie klucze w pl.json i en.json
- [ ] Placeholder parity potwierdzona

## Krok 4: Finalna integracja

- [ ] Połączenie App.axaml.cs DI rejestracji z faktycznymi serwisami
- [ ] Sprawdzenie czy OAuthLoopbackListener jest rejestrowany jako transient
- [ ] Sprawdzenie czy SUStatsConfigViewModel dostaje wszystkie zależności przez DI
- [ ] Sprawdzenie czy stary flow (manualne hasło) nadal działa
- [ ] `dotnet build` ponownie po integracji

## Krok 5: Ręczne testowanie (dev)

- [ ] Aplikacja uruchamia się bez crasha
- [ ] SUStatsConfigView pokazuje nowy panel logowania
- [ ] Kliknięcie "Zaloguj przez Discord" otwiera przeglądarkę
- [ ] Callback z Discord jest odebrany
- [ ] Lista guild jest pobrana i wyświetlona
- [ ] Wybór guildy zapisuje credentials
- [ ] Logowanie działa
