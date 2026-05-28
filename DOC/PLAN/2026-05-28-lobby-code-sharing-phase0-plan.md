# Plan Wdrożenia: Etap 1 - Code Sharing + Chat (Lobby Board)

**Data:** 2026-05-28
**Status:** Faza 0 ✅ Zakończona — plan zaakceptowany po triple review (security + architecture + UI) + aktualizacja regionów i currentPlayers. Gotowy do Fazy 1.
**Aktualizacje:** 
- 2026-05-28: Regiony — tylko modowane (Modded EU/NA/Asia), vanilla usunięte
- 2026-05-28: currentPlayers — live lookup przez REST API modowanego regionu (PoC: `lookup_lobby.py`). Klient queryuje serwer regionu bezpośrednio, server susmodder.app tylko cache'uje snapshot.
- 2026-05-28: Dodany PATCH /api/lobby-board/{id} do cache'owania wyników lookupu
**Źródła:** DOC/POC/2026-05-27-lobby-code-sharing.md, DOC/2026-05-25 - frontend-ideas/10-lobby-code-sharing.md, DOC/POC/Lobby-searcher/lookup_lobby.py
**Priorytet:** P2

---

## Decyzje do potwierdzenia (właściciel)

| # | Pytanie | Odpowiedź |
|---|---------|-----------|
| D1 | Backend Etapu 1? | susmodder.app |
| D2 | Chat w scope Etapu 1? | Tak |
| D3 | TTL wiadomości chatowych? | 4h |
| D4 | TTL kodu lobby? | 20 min |
| D5 | DLL bridge w MVP? | V2 (po MVP) |
| D6 | Dostęp do kodu syzyfowego moda? | Do ustalenia |
| D10 | Admin panel moderacji w MVP? | V2 |
| D11 | Regiony lobby? | Tylko modowane: "Modded EU", "Modded NA", "Modded Asia" |
| D12 | currentPlayers — skąd live? | **Klient queryuje REST API modowanego regionu** (PoC: `lookup_lobby.py`). Flow: kod → gameId → `GET /api/games/{gameId}` → JSON z player_count. Każdy klient SUSModder może to zrobić. Server susmodder.app tylko cache'uje ostatni snapshot przez PATCH. **Obciążenie po stronie klienta, nie serwera.** |
| D13 | Auth do region servera? | Klient potrzebuje `idToken` + `PUID` z Among Us. W MVP: użytkownik podaje w ustawieniach (lub auto-detekcja z plików gry). V2: DLL Bridge wyciąga automatycznie. |

---

## Część 1: Kontrakty API (susmodder-backend)

### 1.1 Endpointy

#### GET /api/lobby-board
Pobiera listę wpisów z lobby board.

**Autoryzacja:** `Authorization: <SUSModder-token>` (taki sam jak istniejące endpointy)

**Parametry query:**
| Parametr | Typ | Wymagany | Opis |
|----------|-----|----------|------|
| modId | int | nie | Filtruj po ID moda |
| type | "code" / "message" / "all" | nie | Typ wpisu (default: "all") |
| region | "Modded EU" / "Modded NA" / "Modded Asia" | nie | Tylko dla type=code. Tylko serwery modowane — vanilla regiony (EU/NA/AS/SA/AU/Other) usunięte. |
| limit | int | nie | Max wpisów (default: 20, max: 50) |
| before | ISO 8601 | nie | Paginacja: wpisy przed timestampem |

**Nagłówek opcjonalny:** `X-User-Hash: sha256-hex` - identyfikacja własnych wpisów (zwraca nawet shadow-banned)

**Response 200:**
```json
{
  "success": true,
  "entries": [
    {
      "id": "uuid-xxxx",
      "type": "code",
      "modId": 3,
      "modName": "Town of Us",
      "code": "ABCDEF",
      "region": "Modded EU",
      "maxPlayers": 15,
      "currentPlayers": null,
      "publishedAt": "2026-05-27T14:17:00Z",
      "expiresAt": "2026-05-27T14:37:00Z",
      "ageSeconds": 180
    },
    {
      "id": "uuid-yyyy",
      "type": "message",
      "modId": 3,
      "modName": "Town of Us",
      "content": "Szukam stałej grupy do TOU. discord.gg/XXXX",
      "publishedAt": "2026-05-27T14:10:00Z",
      "expiresAt": "2026-05-27T18:10:00Z",
      "ageSeconds": 420
    }
  ],
  "total": 2
}
```

#### POST /api/lobby-board
Tworzy nowy wpis (kod lobby lub wiadomość).

**Autoryzacja:** `Authorization: <SUSModder-token>`
**Nagłówek wymagany:** `X-User-Hash: sha256-hex`

**Request body - Kod lobby:**
```json
{
  "type": "code",
  "modId": 3,
  "code": "ABCDEF",
  "region": "Modded EU",
  "maxPlayers": 15,
  "currentPlayers": 8       // opcjonalne — jeśli autor chce podać snapshot
}
```

**Request body - Wiadomość:**
```json
{
  "type": "message",
  "modId": 3,
  "content": "Szukam stałej grupy. discord.gg/XXXX"
}
```

**Response 201:**
```json
{
  "success": true,
  "id": "uuid-xxxx",
  "expiresAt": "2026-05-27T14:37:00Z",
  "moderationWarning": false
}
```

#### DELETE /api/lobby-board/{id}
Usuwa własny wpis.

**Nagłówek wymagany:** `X-User-Hash: sha256-hex` (musi zgadzać się z autorem)

**Response 200:**
```json
{ "success": true }
```

**Response 403:** Gdy X-User-Hash nie pasuje do autora wpisu.

#### PATCH /api/lobby-board/{id}
Aktualizuje własny kod lobby (np. liczbę graczy) — **opcjonalny cache.** Live dane NIE idą przez ten endpoint (zob. sekcja 1.6).

**Nagłówek wymagany:** `X-User-Hash: sha256-hex` (musi zgadzać się z autorem)
**Tylko dla type=code.** Wiadomości nie mają edycji.

**Request body:**
```json
{
  "currentPlayers": 10,
  "maxPlayers": 15
}
```

Wszystkie pola opcjonalne — wysyłasz tylko to co chcesz zmienić.

**Response 200:**
```json
{ "success": true }
```

**Response 403:** Gdy X-User-Hash nie pasuje do autora wpisu.

#### POST /api/lobby-board/{id}/report
Zgłasza wpis.

**Nagłówek wymagany:** `X-User-Hash: sha256-hex`

**Request body:**
```json
{ "reason": "spam" }
```
Dozwolone reason: "spam", "inappropriate", "scam"

**Response 200:** Zawsze 200, niezależnie od wyniku (anti-stalking).

### 1.2 Kody błędów (zwracane w response body)

| errorCode | HTTP Status | Warstwa | Znaczenie |
|-----------|-------------|---------|-----------|
| INVALID_LOBBY_CODE | 400 | 0 | Nieprawidłowy format kodu (4-6 znaków A-Z0-9) |
| CONTENT_TOO_SHORT | 400 | 0 | Wiadomość < 10 znaków |
| CONTENT_TOO_LONG | 400 | 0 | Wiadomość > 280 znaków |
| RATE_LIMITED | 429 | 1 | Cooldown 5 min między wpisami |
| DAILY_LIMIT_REACHED | 429 | 1 | Przekroczono 20 wpisów dziennie |
| DUPLICATE_MESSAGE | 409 | 2 | Identyczna treść od tego samego userHash w ciągu 30 min |
| DISALLOWED_URL | 400 | 3 | Link inny niż discord.gg |
| TOO_MANY_LINKS | 400 | 3 | Więcej niż 1 link Discord |
| CONTENT_BLOCKED | 400 | 4 | Treść zablokowana przez word filter |
| USER_BANNED | 403 | 5 | Hard ban (heat >= 40) |

### 1.3 Live lookup — NIE przez susmodder.app ⚠️

**To nie jest endpoint susmodder.app.** Live dane o grze (player_count, mapa) są pobierane bezpośrednio z serwera modowanego regionu Among Us przez REST API, które te serwery wystawiają (PoC: `lookup_lobby.py`).

```
SUSModder klient ──GET /api/lobby-board──▶ susmodder.app  (lista kodów)
SUSModder klient ──GET /api/games/{id}──▶ https://au-eu.duikbo.at  (LIVE player_count!)
                                        ❌ NIE przez susmodder.app
```

**Flow:**
1. Klient pobiera listę kodów z `GET /api/lobby-board` (susmodder.app)
2. Dla każdego kodu: `game_name_to_int(code)` → gameId (algorytm znany, portowany do C#)
3. `POST https://{region-url}/api/user` z `AmongUsAuth` → region token
4. `GET https://{region-url}/api/games/{gameId}` → JSON z `player_count`, `max_players`, `map`
5. UI wyświetla live dane (bez pośrednictwa susmodder.app!)

**PATCH /api/lobby-board/{id} (susmodder.app) to tylko opcjonalny cache** — klient może wysłać snapshot, żeby inni użytkownicy bez auth do regionu widzieli przybliżoną wartość. Nie jest wymagany do działania.

**Auth do region serwera:** klient potrzebuje `idToken` + `PUID` + `Username` + `ClientVersion` (dane z Among Us / Innersloth). W MVP użytkownik podaje ręcznie w ustawieniach.

**Rate limiting po stronie klienta:** max 1 lookup/30s na unikalny kod (cache w pamięci).

### 1.4 Schemat bazy danych (PostgreSQL)

```sql
CREATE TABLE lobby_board (
  id               UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
  type             VARCHAR(8)   NOT NULL CHECK (type IN ('code', 'message')),
  mod_id           INTEGER      NOT NULL,
  user_hash        CHAR(64)     NOT NULL,
  published_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
  expires_at       TIMESTAMPTZ  NOT NULL,
  is_deleted       BOOLEAN      NOT NULL DEFAULT FALSE,
  is_shadow_banned BOOLEAN      NOT NULL DEFAULT FALSE,
  content_hash     CHAR(64)     NOT NULL,
  -- pola dla type='code'
  code             VARCHAR(8),
  region           VARCHAR(12)   CHECK (region IN ('Modded EU', 'Modded NA', 'Modded Asia')),
  max_players      SMALLINT      NOT NULL DEFAULT 15,
  current_players  SMALLINT,     -- NULL = nie podano, opcjonalny snapshot od autora
  -- pola dla type='message'
  content          VARCHAR(290),
  CONSTRAINT chk_code_fields  CHECK (type != 'code'    OR (code IS NOT NULL AND region IS NOT NULL AND max_players IS NOT NULL)),
  CONSTRAINT chk_msg_fields   CHECK (type != 'message' OR content IS NOT NULL)
);

CREATE TABLE lobby_blocklist (
  id         SERIAL       PRIMARY KEY,
  pattern    TEXT         NOT NULL,
  is_regex   BOOLEAN      NOT NULL DEFAULT FALSE,
  category   VARCHAR(20)  NOT NULL,  -- slur | spam | scam | competitor
  action     VARCHAR(12)  NOT NULL DEFAULT 'block',
  created_at TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE TABLE lobby_reports (
  id            SERIAL      PRIMARY KEY,
  entry_id      UUID        NOT NULL REFERENCES lobby_board(id) ON DELETE CASCADE,
  reporter_hash CHAR(64)    NOT NULL,
  reason        VARCHAR(20) NOT NULL,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(entry_id, reporter_hash)
);

CREATE INDEX idx_lobby_board_active
  ON lobby_board(mod_id, type, expires_at)
  WHERE is_deleted = FALSE AND is_shadow_banned = FALSE;

CREATE INDEX idx_lobby_board_by_hash
  ON lobby_board(user_hash, published_at DESC);

CREATE INDEX idx_lobby_reports_entry
  ON lobby_reports(entry_id, created_at);
```

### 1.5 Redis - klucze rate limiting i heat system

| Klucz | TTL | Opis |
|-------|-----|------|
| lobby:ratelimit:{userHash} | 5 min | Cooldown między wpisami |
| lobby:daily:{userHash}:{YYYY-MM-DD} | 24h | Licznik dzienny |
| lobby:heat:{userHash} | 48h | Punkty ciepła |
| lobby:shadowban:{userHash} | 24h | Shadow ban flag |
| lobby:hardban:{userHash} | 24h | Hard ban flag |

### 1.6 Automoderacja - przepływ POST

```
POST /api/lobby-board
  → W0: Walidacja formatu (code regex / content length)
  → W1: Rate limiting (Redis)
  → W2: Duplicate detection (SHA256 content hash, PostgreSQL)
  → W3: URL allowlist (tylko discord.gg)
  → W4: Word filter + blocklist (PostgreSQL, cache w pamięci)
  → W5: Heat system (obliczenie heat, shadow/hard ban check)
  → Zapis do lobby_board
  → Response 201 (ew. moderationWarning: true)
```

---

## Review Findings (Faza 0)

Plan przeszedł triple review 2026-05-28:

| # | Reviewer | Problem | Severity | Status |
|---|----------|---------|----------|--------|
| S1 | Security Auditor | Token Base64-obfuscated — wyekstrahowalny z .exe, współdzielony między userami | HIGH | ✅ Zaakceptowane ryzyko (istniejąca architektura SUSModder). Backend powinien weryfikować token↔userHash consistency. |
| Q1 | Quality Reviewer | `HardwareIdProvider` jest klasą statyczną — brak interfejsu do DI | HIGH | 🔧 Dodano Fazę 2.0: `IHardwareIdProvider` + `WindowsHardwareIdProvider` + rejestracja DI |
| Q2 | Quality Reviewer | Niespójność HttpClient (static vs instance) w istniejącym kodzie | MEDIUM | ℹ️ `LobbyBoardService` użyje `static readonly HttpClient` (nowszy wzorzec) |
| U1 | UI Reviewer | `MessageCharCount` jako computed property — potrzebuje `WhenAnyValue` lub `OAPH` | MEDIUM | 🔧 Poprawiono: `MessageCharCount` jako `[ObservableAsProperty]` z `ObservableAsPropertyHelper<int>` |

---

## Część 2: SUSModder.Core - nowe komponenty

### 2.0 (PREREKWIZYT) IHardwareIdProvider

Lokalizacja: `SUSModder.Core/Utilities/IHardwareIdProvider.cs`

```csharp
public interface IHardwareIdProvider
{
    string GetAnonymousUserHash();
}
```

Implementacja: `SUSModder.Core/Utilities/WindowsHardwareIdProvider.cs` — opakowuje istniejącą statyczną logikę `HardwareIdProvider`.

Rejestracja w DI (`App.axaml.cs`):
```csharp
services.AddSingleton<IHardwareIdProvider, WindowsHardwareIdProvider>();
```

### 2.1 Nowy endpoint w appsettings.json

```json
"LobbyBoardEndpoint": "/api/lobby-board"
```

Plik: `SUSModder/appsettings.json` — tylko do odczytu (nie modyfikowany runtime).

### 2.2 Interfejs ILobbyBoardService

Lokalizacja: `SUSModder.Core/Services/ILobbyBoardService.cs`

```csharp
public interface ILobbyBoardService
{
    Task<PostEntryResult> PublishCodeAsync(
        string code, int modId, string region,
        int maxPlayers, int currentPlayers,
        CancellationToken ct = default);

    Task<PostEntryResult> PublishMessageAsync(
        string content, int modId,
        CancellationToken ct = default);

    Task<IReadOnlyList<LobbyBoardEntry>> GetEntriesAsync(
        int? modId = null, LobbyEntryType? type = null,
        string? region = null, int limit = 20,
        CancellationToken ct = default);

    Task<bool> DeleteOwnEntryAsync(string entryId, CancellationToken ct = default);
    Task<bool> UpdateCodeEntryAsync(string entryId, int? currentPlayers, int? maxPlayers, CancellationToken ct = default);
    Task<LobbyLookupResult?> LookupLobbyStateAsync(string code, string regionBaseUrl, AmongUsAuth auth, CancellationToken ct = default);
    Task<bool> ReportEntryAsync(string entryId, string reason, CancellationToken ct = default);
}

public enum LobbyEntryType { Code, Message, All }

public record AmongUsAuth(string IdToken, string Puid, string Username, int ClientVersion);

public record LobbyLookupResult(
    int PlayerCount,
    int MaxPlayers,
    string? Map,
    DateTimeOffset QueriedAt
);
```

### 2.3 Implementacja LobbyBoardService

Lokalizacja: `SUSModder.Core/Services/LobbyBoardService.cs`

**Zależności:**
- `IConfiguration` — odczyt BaseUrl i LobbyBoardEndpoint
- `IDiagnosticsOutput` — logowanie
- `IHardwareIdProvider` — generowanie userHash (wstrzykiwany przez DI, zob. Faza 2.0)
- Własny `static readonly HttpClient` z timeout 15s (wzorzec z DllModificationService)

**Szczegóły techniczne:**
- Token autoryzacyjny z `SecretProvider.GetDownloadToken()` (dla endpointów susmodder.app)
- Base URL z `IConfiguration["Configuration:BaseUrl"]`
- Endpoint z `IConfiguration["Configuration:LobbyBoardEndpoint"]`
- UserHash jako SHA256 hardware ID (identyczny jak w telemetrii)
- Timeout HTTP: 15 sekund (susmodder.app), 10 sekund (region server)
- Wszystkie błędy HTTP/JSON zwracają `PostEntryResult` z `Success = false`

**LookupLobbyStateAsync:** używa OSOBNEGO HttpClient (nie autoryzowanego tokenem SUSModder!)
- Flow: kod → `game_name_to_int(code)` → gameId (algorytm z `lookup_lobby.py`, portowany do C#)
- `POST {regionBaseUrl}/api/user` z `AmongUsAuth` → region token
- `GET {regionBaseUrl}/api/games/{gameId}` z region token + `Client-Mods` header
- Parsuje JSON: `player_count`, `max_players`, `map`
- Rate limiting: max 1 request/30s per unikalny kod (cache w pamięci)

### 2.4 Modele

Lokalizacja: `SUSModder.Core/Models/LobbyBoardModels.cs`

```csharp
public record LobbyBoardEntry(
    string Id,
    LobbyEntryType Type,
    int ModId,
    string ModName,
    DateTimeOffset PublishedAt,
    DateTimeOffset ExpiresAt,
    int AgeSeconds,
    // Code-specific (null dla Message)
    string? Code,
    string? Region,
    int? MaxPlayers,
    int? CurrentPlayers,
    // Message-specific (null dla Code)
    string? Content
);

public record PostEntryResult(
    bool Success,
    string? EntryId,
    DateTimeOffset? ExpiresAt,
    string? ErrorCode,
    bool ModerationWarning
);
```

### 2.5 Walidacja kliencka

Lokalizacja: `SUSModder.Core/Validators/LobbyEntryValidator.cs`

```csharp
public static class LobbyEntryValidator
{
    private static readonly Regex LobbyCodeRegex = new(@"^[A-Z0-9]{4,6}$", RegexOptions.Compiled);
    private static readonly Regex DiscordInviteRegex = new(@"discord\.gg/[a-zA-Z0-9]+", RegexOptions.Compiled);
    private static readonly Regex AnyUrlRegex = new(@"https?://[^\s]+|www\.[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static (bool IsValid, string? ErrorCode) ValidateCode(string code);
    public static (bool IsValid, string? ErrorCode) ValidateMessage(string content);
    public static string NormalizeContent(string s);
}
```

Walidacja odbywa się PO STRONIE KLIENTA przed wysłaniem requestu, jako pierwsza linia obrony. Backend i tak weryfikuje niezależnie.

### 2.6 Nowe pole w ModConfiguration

W pliku `SUSModder.Core/Configuration/ModConfig.cs`:

```csharp
[JsonPropertyName("SupportsLobbySharing")]
public bool SupportsLobbySharing { get; set; } = false;

[JsonPropertyName("LobbyRegionBaseUrl")]
public string? LobbyRegionBaseUrl { get; set; } = null;  // np. "https://au-eu.duikbo.at"
```

Pole ustawiane przez API backendowy (backend oznacza, który mod wspiera feature).

### 2.7 LobbyBridgeFileReader (V2)

Lokalizacja: `SUSModder.Core/Lobby/LobbyBridgeFileReader.cs`

```csharp
public sealed class LobbyBridgeFileReader : IDisposable
{
    public event EventHandler<LobbyCodeDetectedEventArgs>? LobbyCodeDetected;

    public LobbyBridgeFileReader(string bridgeFilePath);
    public void Start();
    public void Stop();
    public void Dispose();
}

public class LobbyCodeDetectedEventArgs : EventArgs
{
    public string Code { get; init; }
    public int ModId { get; init; }
    public string Region { get; init; }
    public int MaxPlayers { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}
```

**Implementacja:** FileSystemWatcher na `%APPDATA%/SUSModder/lobby-bridge.json`. Ignoruje wpisy starsze niż 90 sekund. Wymaga osobnego repo `SUSModder.Bridge` dla DLL wstrzykiwanej do Among Us.

**UWAGA:** LobbyBridgeFileReader = V2. NIE wchodzi do MVP Etapu 1.

---

## Część 3: UI (SUSModder)

### 3.1 LobbyBoardPanel (Avalonia View)

Lokalizacja: `SUSModder/Views/LobbyBoardPanel.axaml`

Umiejscowienie: expandable panel w prawym panelu szczegółów moda, widoczny tylko gdy `ModConfiguration.SupportsLobbySharing == true`.

**Struktura:**
```
+--------------------------------------------------------------+
|  Lobby - {ModName}                           [Odśwież]       |
|                                                              |
|  [ Kody ({count}) ]  [ Ogłoszenia ({count}) ]                |
|                                                              |
|  --- Zakładka Kody ---                                       |
|  Lista kodów (ItemsControl + DataTemplate)                   |
|  Każdy item: region | code | players | czas | [Kopiuj] [Zgłoś]|
|                                                              |
|  Formularz dodawania kodu:                                   |
|  Kod: [______]  Region: [Modded EU v]  Graczy: [_] / [15]     |
|  [Udostępnij kod]                                            |
|                                                              |
|  --- Zakładka Ogłoszenia ---                                 |
|  Lista ogłoszeń (ItemsControl + DataTemplate)                |
|  Każdy item: treść | czas | [Zgłoś]                          |
|                                                              |
|  Formularz dodawania ogłoszenia:                             |
|  [______________________________] {n}/280 znaków             |
|  [Opublikuj ogłoszenie]                                      |
+--------------------------------------------------------------+
```

### 3.2 LobbyBoardPanelViewModel

Lokalizacja: `SUSModder/ViewModels/LobbyBoardPanelViewModel.cs`

```csharp
public class LobbyBoardPanelViewModel : ReactiveObject
{
    // Wstrzykiwane
    private readonly ILobbyBoardService _lobbyService;
    private readonly ILocalizationService _loc;
    private readonly ModConfiguration _mod;

    // Kolekcje
    public ObservableCollection<LobbyBoardItemViewModel> ActiveCodes { get; }
    public ObservableCollection<LobbyBoardItemViewModel> ActiveMessages { get; }

    // Inputy
    [Reactive] public string CodeInput { get; set; } = "";
    [Reactive] public string MessageInput { get; set; } = "";
    [Reactive] public string SelectedRegion { get; set; } = "Modded EU";
    [Reactive] public int? CurrentPlayers { get; set; } = null;
    [Reactive] public int MaxPlayers { get; set; } = 15;
    [Reactive] public int SelectedTab { get; set; } = 0;

    // Stan
    [Reactive] public bool IsLoading { get; private set; }
    [Reactive] public string? StatusMessage { get; private set; }
    [Reactive] public bool IsStatusError { get; private set; }
    // Używa ObservableAsPropertyHelper — automatycznie aktualizowane przy zmianie MessageInput
private readonly ObservableAsPropertyHelper<int> _messageCharCount;
public int MessageCharCount => _messageCharCount.Value;
// W konstruktorze:
// _messageCharCount = this.WhenAnyValue(x => x.MessageInput, input => 280 - (input?.Length ?? 0))
//     .ToProperty(this, x => x.MessageCharCount);

    // Komendy
    public ReactiveCommand<Unit, Unit> PublishCodeCommand { get; }
    public ReactiveCommand<LobbyBoardItemViewModel, Unit> UpdatePlayerCountCommand { get; }  // PATCH — tylko własne kody
    public ReactiveCommand<Unit, Unit> PublishMessageCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<LobbyBoardItemViewModel, Unit> CopyCodeCommand { get; }
    public ReactiveCommand<LobbyBoardItemViewModel, Unit> ReportCommand { get; }
    public ReactiveCommand<LobbyBoardItemViewModel, Unit> DeleteOwnCommand { get; }
}
```

### 3.3 LobbyBoardItemViewModel

Lokalizacja: `SUSModder/ViewModels/LobbyBoardItemViewModel.cs`

```csharp
public class LobbyBoardItemViewModel : ReactiveObject
{
    public string Id { get; set; }
    public LobbyEntryType Type { get; }

    // Code-specific
    public string? Code { get; }
    public string? Region { get; }
    public int? MaxPlayers { get; }
    public int? CurrentPlayers { get; }
    public string PlayerCountDisplay { get; }

    // Message-specific
    public string? Content { get; }

    // Wspólne
    public string TimeAgoDisplay { get; }
    public bool IsOwnEntry { get; }

    // Komenda kopiowania (tylko dla code)
    public ReactiveCommand<Unit, Unit> CopyCodeCommand { get; }
}
```

### 3.4 Auto-refresh

- `Observable.Interval(TimeSpan.FromSeconds(30))` gdy panel jest widoczny (IsVisible)
- Zatrzymywany gdy panel jest zwinięty
- Ręczny refresh zawsze dostępny przez przycisk "Odśwież"

### 3.5 i18n - nowe klucze

Nowa sekcja `Lobby` w `pl.json` i `en.json`:

```
Lobby.Panel.Title
Lobby.Panel.TabCodes          (+ count placeholder)
Lobby.Panel.TabMessages       (+ count placeholder)
Lobby.Panel.Refresh
Lobby.Panel.NoCodes
Lobby.Panel.NoMessages
Lobby.Panel.Loading
Lobby.Panel.ServiceUnavailable
Lobby.Code.CopyButton
Lobby.Code.PublishButton
Lobby.Code.DeleteButton
Lobby.Code.MinutesAgo
Lobby.Code.JustNow
Lobby.Code.InvalidFormat
Lobby.Code.RateLimited
Lobby.Code.PublishSuccess
Lobby.Code.Copied
Lobby.Message.Placeholder
Lobby.Message.PublishButton
Lobby.Message.DeleteButton
Lobby.Message.TooShort
Lobby.Message.TooLong
Lobby.Message.DisallowedUrl
Lobby.Message.TooManyLinks
Lobby.Message.ContentBlocked
Lobby.Message.Duplicate
Lobby.Message.DailyLimitReached
Lobby.Message.UserBanned
Lobby.Message.PublishSuccess
Lobby.Message.ModerationWarning
Lobby.Report.Button
Lobby.Report.Spam
Lobby.Report.Inappropriate
Lobby.Report.Scam
Lobby.Report.Done
Lobby.Region.ModdedEU      | Modded EU | Modded EU
Lobby.Region.ModdedNA      | Modded NA | Modded NA
Lobby.Region.ModdedAsia    | Modded Asia | Modded Asia
Lobby.Region.Unknown       | Nieznany | Unknown
```

### 3.6 Integracja w MainWindowViewModel

- W metodzie wczytującej szczegóły moda: warunkowe tworzenie `LobbyBoardPanelViewModel` gdy `SupportsLobbySharing == true`
- Subskrypcja na event zmiany widoczności panelu (start/stop auto-refresh)
- Dodanie nowego expandable section w prawym panelu

---

## Część 4: Kolejność implementacji (krok po kroku)

### Faza 0 - Przygotowanie (obecna) ✅
0.1. Potwierdzenie decyzji D1-D10 przez właściciela
0.2. Kontrakty API (ten dokument)
0.3. Review: security, architecture, UI
0.4. Finalny plan zaakceptowany

### Faza 1 - Backend (1-1.5 dnia)
1.1. Tabela `lobby_board` + indeksy (region CHECK z nowymi wartościami)
1.2. Tabela `lobby_blocklist` z seed data
1.3. Tabela `lobby_reports`
1.4. Implementacja `POST /api/lobby-board`:
     - Warstwy W0-W5 moderacji
     - Generowanie content_hash (SHA256)
     - Ustawianie expires_at (20 min code / 4h message)
     - Heat system + shadow/hard ban
     - `currentPlayers` opcjonalny (nullable)
1.5. Implementacja `GET /api/lobby-board`:
     - Filtrowanie po modId, type, region (tylko "Modded EU"/"Modded NA"/"Modded Asia")
     - Shadow ban filter (chyba że X-User-Hash autora)
     - Paginacja
     - Obliczanie ageSeconds
1.6. Implementacja `DELETE /api/lobby-board/{id}`
1.7. Implementacja `PATCH /api/lobby-board/{id}` (aktualizacja currentPlayers/maxPlayers — tylko autor)
1.8. Implementacja `POST /api/lobby-board/{id}/report`
1.9. Redis: klucze rate limiting + heat system
1.10. Cleanup cron (soft-delete co 10 min, hard delete co 24h)
1.11. Testy API (curl / Postman)

### Faza 2 - Core (0.5-1 dnia)
2.1. Dodanie `LobbyBoardEndpoint` do appsettings.json
2.2. Modele: `LobbyBoardEntry`, `PostEntryResult`, `LobbyEntryType`, `AmongUsAuth`, `LobbyLookupResult`
2.3. Lobby code ↔ gameId konwerter (port `game_name_to_int` / `int_to_game_name` z `lookup_lobby.py` do C#)
2.4. Interfejs `ILobbyBoardService`
2.5. Implementacja `LobbyBoardService`:
     - HTTP client dla susmodder.app (static readonly, autoryzacja SecretProvider)
     - Osobny HTTP client dla region serverów (bez auth SUSModder, z `AmongUsAuth`)
     - `LookupLobbyStateAsync`: code→gameId→POST /api/user→GET /api/games/{id}→parse JSON
     - Rate limiting: cache 30s per kod
     - X-User-Hash przez IHardwareIdProvider
     - Serializacja/deserializacja JSON (System.Text.Json)
2.6. Walidacja kliencka: `LobbyEntryValidator`
2.7. Nowe pola `SupportsLobbySharing` + `LobbyRegionBaseUrl` w `ModConfiguration`
2.8. Rejestracja w DI (App.axaml.cs)

### Faza 3 - UI (1 dzień)
3.1. `LobbyBoardItemViewModel`
3.2. `LobbyBoardPanelViewModel`:
     - Zakładki Kody/Ogłoszenia
     - Formularze publikacji
     - Auto-refresh (30s)
     - Obsługa błędów z i18n mapping
3.3. `LobbyBoardPanel.axaml`:
     - DataTemplates dla code i message
     - Licznik znaków
     - Region dropdown
     - Kopiowanie kodu do schowka
     - Dialog zgłoszenia (ComboBox z reason)
3.4. Integracja w MainWindow (prawy panel, expandable section)
3.5. i18n: dodanie wszystkich kluczy do pl.json i en.json
3.6. Testy ręczne: publikacja kodu, publikacja wiadomości, refresh, zgłaszanie

### Faza 4 - DLL Bridge (2-3 dni, V2)
4.1. Nowe repo `SUSModder.Bridge`
4.2. Harmony patch dla TOU-Mira
4.3. `LobbyBridgeFileReader` w Core
4.4. Integracja auto-wykrywania w UI
4.5. Dystrybucja przez DllModificationService

### Faza 5 - Testy i wdrożenie (0.5 dnia)
5.1. Testy end-to-end (publikacja → odczyt → usunięcie)
5.2. Testy moderacji (rate limit, duplicate, URL block, word filter)
5.3. Testy shadow/hard ban
5.4. Deployment backendu na susmodder.app
5.5. Build i release klienta

---

## Część 5: Ryzyka i mitygacje

| Ryzyko | Prawdopodobieństwo | Wpływ | Mitygacja |
|--------|-------------------|-------|-----------|
| Ryzyko | Prawdopodobieństwo | Wpływ | Mitygacja |
|--------|-------------------|-------|-----------|
| Token autoryzacyjny wyekstrahowany z .exe (Base64) | Wysokie | Średni | Akceptowane ryzyko (istniejąca architektura). Warstwy W1-W5 (rate limit + heat + shadow ban) ograniczają szkody. Backend powinien weryfikować token↔userHash. |
| Brak IHardwareIdProvider w Core | Niskie (po Fazie 2.0) | Średni | 🔧 Dodany jako prerekwizyt Fazy 2 (IHardwareIdProvider + WindowsHardwareIdProvider). |
| currentPlayers nieaktualny (autor wyszedł z gry) | Średnie | Niskie | Wartość to snapshot — TTL 20 min na kod rozwiązuje problem. UI pokazuje "?/15" gdy currentPlayers=null. |
| Backend niedostępny | Średnie | Niskie | StatusMessage "Usługa niedostępna", graceful degradation |
| Spam linkami discord.gg | Średnie | Średnie | Warstwa 3 (URL allowlist) + max 1 link |
| Token autoryzacyjny wyciek | Niskie | Wysokie | Token jest w Secrets.cs, nie zmienia się |

---

## Podsumowanie

**Szacowany czas MVP (Fazy 1-3): ~3.5-4 dni**
- Backend: 1-1.5 dnia
- Core: 1 dzień (w tym port game_name_to_int z Pythona do C#)
- UI: 1-1.5 dnia (w tym integracja live lookup)

**Poza MVP (Faza 4): +2-3 dni**
- DLL Bridge dla auto-wykrywania kodu

**Poza scope (Etap 2 i 3): osobne wdrożenia**
