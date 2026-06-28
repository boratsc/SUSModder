# POC: Znajdowanie lobby - trzy etapy

**Data:** 2026-05-27
**Status:** Analiza / POC (do akceptacji przed implementacją)
**Zrodla:** 10-lobby-code-sharing.md, 11-lobby-searcher.md, 12-voice-chat-integration.md, DOC/POC/Lobby-searcher/PoC.md
**Priorytet:** P2

---

## Kontekst

Serwery Among Us maja bugi - wyszukiwanie publicznych lobby czesto nie dziala. Spolecznosc jest rozproszona po wielu serwerach Discord. Obecny flow znalezienia gry z modem: otworzyc Discord, znalezc wlasciwy serwer, znalezc kanal z kodami, skopiowac, wkleic w grze. SUSModder moze skrocic to do dwoch klikniec.

Trzy etapy tworzace naturalny pipeline:

- Etap 1: Code Sharing + Chat (spolecznosciowe)
- Etap 2: Lobby Searcher (serwery AU skanowane automatycznie)
- Etap 3: BetterCrewLink (proximity voice w grze)

Etap 1 daje wartosc natychmiast. Etap 2 wymaga zakonczonego PoC protokolu AU. Etap 3 jest niezalezny od obu.

---

## Etap 1: Code Sharing + Chat

### Cel

Dwa powiazane feature'y w jednym widoku:

1. **Kody lobby** - publikacja kodu wlasnego lobby (TTL 20 min) + przegladanie aktywnych kodow innych graczy.
2. **Mini-chat / ogloszenia** - krotkie teksty (max 280 znakow) z mozliwoscia reklamowania serwera Discord, szukania graczy (TTL 4h). Nie real-time IRC - tablica ogloszen per mod z automatyczna moderacja.

### Decyzja: clair-api vs susmodder.app

Przy samych kodach lobby: clair-api jest szybsze (bot stoi, SignalR gotowy, ~2h pracy).
Przy dodaniu chatu z moderacja: susmodder.app jest wlasciwym wyborem.

Powody dla susmodder.app przy chacie:
- Clair jest zaprojektowany pod Discord guilds i Discord userow, nie pod anonimowych graczy z hardware hash. Adaptacja bylaby rownie kosztowna co budowa od zera.
- susmodder.app daje pelna kontrole nad schematem, moderacja, TTL i cyklem zycia danych.
- ILobbyBoardService w Core izoluje coupling - zmiana backendu = zmiana URL w appsettings.json.
- Redis i PostgreSQL sa juz na backendzie SUSModder (telemetria, konfiguracja).

**Wybor: susmodder.app, nowy endpoint /api/lobby-board.**
Jeden endpoint dla obu typow wpisow (type: "code" lub "message").

### Czym jest chat (zakres)

JEST:
- Tablica ogloszen per mod z TTL 4 godziny
- Krotkie teksty do 280 znakow: "szukam stalej grupy TOU", "discord.gg/XXXX", "rekrutujemy"
- Anonimowe - widoczny tylko czas wpisu, zero nickow powiazanych z tozsamoscia

NIE JEST:
- Real-time IRC / Twitch chat (zero live typing, zero @wzmianek, zero watkow)
- Forum z wątkami i odpowiedziami
- Miejscem na obrazki, pliki, dlugie posty
- Wymaga konta lub logowania
### Automatyczna moderacja chatu - architektura warstwowa

To najtrudniejsza czesc. Siedem warstw od najtanszej do najdrozszej, dzialajacych razem bez recznej interwencji przy typowych naduzuciach.

**Warstwa 0 - Walidacja formatu (klient + serwer, ~0ms)**

- max_length: 280 znakow
- min_length: 10 znakow (blokuje "asd", "test", puste tresci)
- Brak control chars (U+0000-U+001F, U+007F-U+009F)

**Warstwa 1 - Rate limiting per userHash (Redis, ~0ms)**

- Max 3 aktywne wpisy naraz (lacznie kody + wiadomosci) na jeden hash
- Cooldown miedzy wpisami: 5 minut
- Max wpisow dziennie: 20

Klucze Redis:
- lobby:ratelimit:{userHash} - TTL 5min, inkrementowany per POST
- lobby:daily:{userHash}:{YYYY-MM-DD} - TTL 24h, licznik dzienny

**Warstwa 2 - Duplicate / content hash detection (PostgreSQL, ~1ms)**

Kazda wiadomosc hashowana SHA256(normalize(content)).
normalize = lowercase + strip whitespace + strip interpunkcja.
Jesli ten sam hash od tego samego userHash w ciagu 30 minut -> DUPLICATE_MESSAGE.
Nie wysyla zadnego requestu - odrzucenie po stronie klienta.

**Warstwa 3 - Allowlist linkow (regex, ~0ms)**

Jedyne dozwolone URL-e: zaproszenia Discord (discord.gg/XXXX).
Wszystkie inne blokowane: bit.ly, tinyurl, http://, www. itp.
Max 1 link Discord per wiadomosc.
errorCode: DISALLOWED_URL lub TOO_MANY_LINKS

**Warstwa 4 - Word filter + blocklist (PostgreSQL, ~2ms)**

Tabela lobby_blocklist: wzorce literal i regex z kategoria (slur / spam / scam / competitor).
Wzorce ladowane przy starcie serwisu, cache w pamieci, odswiez co 5 minut.
Startowa lista: wulgaryzmy PL/EN, "nitro free", "steam gift", reklamy innych mod managerow.
Akcja per wzorzec: block (odrzucenie) lub shadow_ban (przyjety ale niewidoczny).

**Warstwa 5 - Heat system (Redis, ~1ms)**

Adaptacja mechanizmu z Clair security_config (heat_warn_threshold, heat_ban, heat_decay).
Kazda akcja uzytkownika generuje punkty ciepla:

- Wiadomosc zatwierdzona: +0
- Wiadomosc odrzucona przez filter: +5
- Wiadomosc zgloszona przez innych: +10
- Duplicate attempt: +3
- Rate limit hit: +8
- Decay: -1 per godzine

Progi:
- heat >= 15: moderationWarning: true w response (ale wpis przechodzi)
- heat >= 25: shadow ban (wpisy przyjete ale niewidoczne dla innych)
- heat >= 40: hard ban (429 przez 24h)

Shadow ban jest kluczowy: uzytkownik nie wie ze jest banowany - widzi swoje wpisy, inni nie.
Eliminuje probe "obejscia bana" przez restart aplikacji.

Redis keys:
- lobby:heat:{userHash}       TTL 48h
- lobby:shadowban:{userHash}  TTL 24h (ustawiany gdy heat >= 25)
- lobby:hardban:{userHash}    TTL 24h (ustawiany gdy heat >= 40)

**Warstwa 6 - Community reporting (PostgreSQL)**

Przycisk "Zglos" na kazdym wpisie.
Po 2 zgloszeniach z roznych hashey w ciagu 30 min: auto-soft-delete + +10 heat na autora.
Anti-weaponized-reporting: reporter ktory zglasza wpisy niebedace usuwane przez innych -> +2 heat na reportera.
Cron co minute sprawdza nowe raporty.

Odpowiedz na report jest zawsze 200 - nie informuj reportera o akcji (anti-stalking).

**Warstwa 7 - Admin panel (V2)**

Prosta tabela w susadmin (istniejacy PHP panel): lista ostatnich wpisow z flagami,
reczny ban userHash, zarzadzanie blocklist. Zero nowych technologii.
### Kontrakt API - susmodder.app /api/lobby-board

**GET /api/lobby-board**

  GET /api/lobby-board?modId=3&type=code&region=EU&limit=20
  Authorization: <SUSModder-token>

Response 200:
`json
{
  "success": true,
  "entries": [
    {
      "id": "uuid-xxxx",
      "type": "code",
      "modId": 3,
      "modName": "Town of Us",
      "code": "ABCDEF",
      "region": "EU",
      "maxPlayers": 15,
      "currentPlayers": 8,
      "publishedAt": "2026-05-27T14:17:00Z",
      "expiresAt": "2026-05-27T14:37:00Z",
      "ageSeconds": 180
    },
    {
      "id": "uuid-yyyy",
      "type": "message",
      "modId": 3,
      "modName": "Town of Us",
      "content": "Szukam stalej grupy do TOU, gram codziennie 20-23. discord.gg/XXXX",
      "publishedAt": "2026-05-27T14:10:00Z",
      "expiresAt": "2026-05-27T18:10:00Z",
      "ageSeconds": 420
    }
  ],
  "total": 2
}
`

Parametry: modId (opcjonalne), type (code | message | all), region (tylko dla code), limit (max 50).
Shadow-banned wpisy sa filtrowane po stronie backendu.
Wpisy autora sa zwracane nawet jesli shadow-banned (identyfikacja przez X-User-Hash header).

**POST /api/lobby-board**

  POST /api/lobby-board
  Authorization: <SUSModder-token>
  X-User-Hash: sha256-hardware-id-hex

Dla kodu:
`json
{ "type": "code", "modId": 3, "code": "ABCDEF", "region": "EU", "maxPlayers": 15, "currentPlayers": 8 }
`

Dla wiadomosci:
`json
{ "type": "message", "modId": 3, "content": "Szukam stalej grupy. discord.gg/XXXX" }
`

Response 201:
`json
{ "success": true, "id": "uuid-xxxx", "expiresAt": "...", "moderationWarning": false }
`

moderationWarning: true gdy heat >= 15 ale wpis przeszedl moderacje.

Kody bledow (Core mapuje na i18n key):

| errorCode            | Warstwa | i18n key                        |
|----------------------|---------|---------------------------------|
| INVALID_LOBBY_CODE   | 0       | Lobby.Code.InvalidFormat        |
| CONTENT_TOO_SHORT    | 0       | Lobby.Message.TooShort          |
| CONTENT_TOO_LONG     | 0       | Lobby.Message.TooLong           |
| RATE_LIMITED         | 1       | Lobby.Code.RateLimited          |
| DAILY_LIMIT_REACHED  | 1       | Lobby.Message.DailyLimitReached |
| DUPLICATE_MESSAGE    | 2       | Lobby.Message.Duplicate         |
| DISALLOWED_URL       | 3       | Lobby.Message.DisallowedUrl     |
| TOO_MANY_LINKS       | 3       | Lobby.Message.TooManyLinks      |
| CONTENT_BLOCKED      | 4       | Lobby.Message.ContentBlocked    |
| USER_BANNED          | 5 hard  | Lobby.Message.UserBanned        |

USER_BANNED zwracany tylko przy hard ban (heat >= 40).
Przy shadow ban (heat >= 25): 201 jak normalnie - uzytkownik nie wie.

**DELETE /api/lobby-board/{id}**

Naglowek X-User-Hash musi zgadzac sie z autorem. Zwraca 403 przy probie usuniecia cudzego wpisu.

**POST /api/lobby-board/{id}/report**

`json
{ "reason": "spam" }
`

X-User-Hash wymagany. Odpowiedz zawsze 200 niezaleznie od wyniku.

### Schema bazy danych (susmodder.app - PostgreSQL)

`sql
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
  region           VARCHAR(10),
  max_players      SMALLINT,
  current_players  SMALLINT,
  -- pola dla type='message'
  content          VARCHAR(290),
  CONSTRAINT chk_code_fields  CHECK (type != 'code'    OR (code IS NOT NULL AND region IS NOT NULL)),
  CONSTRAINT chk_msg_fields   CHECK (type != 'message' OR content IS NOT NULL)
);

CREATE TABLE lobby_blocklist (
  id       SERIAL       PRIMARY KEY,
  pattern  TEXT         NOT NULL,
  is_regex BOOLEAN      NOT NULL DEFAULT FALSE,
  category VARCHAR(20)  NOT NULL,  -- slur | spam | scam | competitor
  action   VARCHAR(12)  NOT NULL DEFAULT 'block',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
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
`

TTL:
- type='code':    expires_at = NOW() + INTERVAL '20 minutes'
- type='message': expires_at = NOW() + INTERVAL '4 hours'

Cleanup cron co 10 min: soft-delete gdzie expires_at < NOW().
Hard delete raz dziennie: usuwa wpisy starsze o 2h ponad TTL.
### SUSModder.Core - nowe komponenty

**ILobbyBoardService**

`csharp
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
        string? region = null, CancellationToken ct = default);

    Task<bool> DeleteOwnEntryAsync(string entryId, CancellationToken ct = default);
    Task<bool> ReportEntryAsync(string entryId, string reason, CancellationToken ct = default);
}

public enum LobbyEntryType { Code, Message, All }
`

**Modele**

`csharp
public record LobbyBoardEntry(
    string Id, LobbyEntryType Type, int ModId, string ModName,
    DateTimeOffset PublishedAt, DateTimeOffset ExpiresAt,
    // Code-specific (null dla Message)
    string? Code, string? Region, int? MaxPlayers, int? CurrentPlayers,
    // Message-specific (null dla Code)
    string? Content
);

public record PostEntryResult(
    bool Success, string? EntryId, DateTimeOffset? ExpiresAt,
    string? ErrorCode, string? LocalizedErrorKey,
    bool ModerationWarning  // true = wpis przeszedl ale zbliazasz sie do heat threshold
);
`

**Walidacja kliencka (przed round-tripem do API)**

`csharp
// Oba formaty kodow Among Us: stary 4 litery, nowy 6 liter/cyfr
private static readonly Regex LobbyCodeRegex =
    new(@"^[A-Z0-9]{4,6}$", RegexOptions.Compiled);

// Tylko discord.gg linki dozwolone
private static readonly Regex DiscordInviteRegex =
    new(@"discord\.gg/[a-zA-Z0-9]+", RegexOptions.Compiled);
private static readonly Regex AnyUrlRegex =
    new(@"https?://[^\s]+|www\.[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

// Duplicate detection - po stronie klienta (opcjonalne, prawdziwa walidacja na backendzie)
private static string NormalizeContent(string s) =>
    Regex.Replace(s.ToLowerInvariant().Trim(), @"[\s\p{P}]", "");
`

**Konfiguracja**

appsettings.json - nowy klucz (read-only jak inne endpointy):
  "LobbyBoardEndpoint": "/api/lobby-board"

**ModConfiguration - nowe pole**

`csharp
[JsonPropertyName("SupportsLobbySharing")]
public bool SupportsLobbySharing { get; set; } = false;
`

Panel lobby wyswietlany wylacznie dla modow z SupportsLobbySharing == true.
Ustawiany przez API config backendowy (backend oznacza kto wspiera feature).

**LobbyBridgeFileReader (V2 - DLL bridge)**

`csharp
// Nasluchuje na %APPDATA%/SUSModder/lobby-bridge.json
// FileSystemWatcher - OS-level push, zero pollingu
// Ignoruje wpisy starsze niz 90 sekund
// Emituje: event LobbyCodeDetected(string code, int modId)
public sealed class LobbyBridgeFileReader : IDisposable
{
    public event EventHandler<LobbyCodeDetectedEventArgs>? LobbyCodeDetected;
}
// Format pliku bridge (zapisywany przez DLL z procesu gry):
// { "code": "ABCDEF", "modId": 3, "region": "EU", "maxPlayers": 15,
//   "isPublic": true, "timestamp": "2026-05-27T14:17:00.000Z", "bridgeVersion": 1 }
`

### UI - LobbyBoardPanel

Umiejscowienie: sekcja expandable w prawym panelu moda, warunkowa na SupportsLobbySharing.

`
+--------------------------------------------------------------+
|  Lobby - Town of Us                          [Odswiez]       |
|                                                              |
|  [ Kody (3) ]  [ Ogloszenia (5) ]                            |
|                                                              |
|  --- zakladka Kody ---                                       |
|  +------------------------------------------------------+   |
|  |  EU  |  ABCDEF  |  8 / 15  |  3 min temu            |   |
|  |                        [Kopiuj]  [Zglos]             |   |
|  +------------------------------------------------------+   |
|                                                              |
|  Twoj kod: [________]  Region: [EU v]  Graczy: [8] / [15]   |
|  [Udostepnij kod]                                            |
|                                                              |
|  --- zakladka Ogloszenia ---                                 |
|  +------------------------------------------------------+   |
|  |  "Szukam stalej grupy TOU, gram 20-23.               |   |
|  |   discord.gg/XXXX"                    15 min temu    |   |
|  |                                     [Zglos]          |   |
|  +------------------------------------------------------+   |
|                                                              |
|  [______________________________] 245/280 znakow             |
|  [Opublikuj ogloszenie]                                      |
+--------------------------------------------------------------+
`

Licznik znakow (280 - length) przy polu wiadomosci.
Brak nickow - czas jest jedynym atrybutem autora widocznym dla innych.
Przycisk "Zglos" otwiera dropdown z powodami (spam / nieodpowiednia tresc / oszustwo).

**LobbyBoardPanelViewModel**

`csharp
public class LobbyBoardPanelViewModel : ReactiveObject
{
    public ObservableCollection<LobbyBoardItemViewModel> ActiveCodes { get; }
    public ObservableCollection<LobbyBoardItemViewModel> ActiveMessages { get; }

    [Reactive] public string CodeInput { get; set; } = string.Empty;
    [Reactive] public string MessageInput { get; set; } = string.Empty;
    [Reactive] public string SelectedRegion { get; set; } = "EU";
    [Reactive] public int CurrentPlayers { get; set; } = 8;
    [Reactive] public int MaxPlayers { get; set; } = 15;
    [Reactive] public bool IsLoading { get; private set; }
    [Reactive] public string? StatusMessage { get; private set; }
    public int MessageCharCount => 280 - MessageInput.Length;

    public ReactiveCommand<Unit, Unit> PublishCodeCommand { get; }
    public ReactiveCommand<Unit, Unit> PublishMessageCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<LobbyBoardItemViewModel, Unit> CopyCodeCommand { get; }
    public ReactiveCommand<LobbyBoardItemViewModel, Unit> ReportCommand { get; }
    public ReactiveCommand<LobbyBoardItemViewModel, Unit> DeleteOwnCommand { get; }
}
`

Auto-refresh: Observable.Interval(30s) gdy panel widoczny.

### DLL Bridge - auto-wykrywanie kodu (V2)

Dlaczego plik JSON zamiast named pipe / localhost HTTP:
Among Us (Unity IL2CPP) - AnonymousPipeServerStream rzuca NotImplementedException (BepInEx #1165).
Plik %APPDATA%/SUSModder/lobby-bridge.json: dostepny z obu procesow, atomiczny przez File.WriteAllText,
zero portow, zero IL2CPP quirks. FileSystemWatcher w SUSModder = OS-level push.

Format pliku bridge:
  { "code": "ABCDEF", "modId": 3, "region": "EU", "maxPlayers": 15,
    "isPublic": true, "timestamp": "2026-05-27T14:17:00.000Z", "bridgeVersion": 1 }

Ignorowany jesli timestamp starszy niz 90 sekund.

TOU-Mira: Mira API events (do weryfikacji czy istnieje LobbyCreatedEvent).
Fallback - Harmony patch na GameStartManager.Update (identyczny dla ToU-R / syzyfowego moda, inny modId).
Dystrybucja: osobne repo SUSModder.Bridge (inny toolchain Unity/IL2CPP).
Instalowany automatycznie przez DllModificationService jako dll mod.

---

## Etap 2: Wyszukiwarka lobby

Cel: aktywne skanowanie publicznych lobby z serwerow AU bez wklejania kodow przez graczy.
Uzupelnienie Etapu 1 - zakladka "Znalezione" obok "Kody" i "Ogloszenia".

Stan obecny: DOC/POC/Lobby-searcher/PoC.md - koncepcja i Python prototype. Brak C#.
Brak potwierdzenia ze protokol da sie odwrocic niezawodnie.

Wyzwania: protokol AU niepubliczny (reverse-engineering UDP), identyfikacja moda (heurystyka),
ryzyko bana IP przy zbyt czestym skanowaniu.

Architektura C# (jesli PoC sie powiedzie):
  SUSModder.Core/Lobby/ - RegionLoader, LobbyClient, LobbyParser, LobbyInfo, ModFingerprinter
  SUSModder/ - LobbySearcherViewModel, zakladka "Znalezione" w widoku lobby

ETAP 2 NIE STARTUJE dopoki Python PoC nie potwierdzi ze protokol jest odwracalny
i nie grozi banem IP. Decyzje: kontynuac Python czy C#? Min. interwal 60s? Heurystyka moda?

---

## Etap 3: Voice Chat - BetterCrewLink

Dlaczego BCL a nie wlasny serwer Discord:
Wlasny serwer bylby kolejna spolecznoscia konkurujaca z istniejacymi (np. "Wspolnicy" 55k userow).
BCL to narzedzie techniczne - nie rywalizuje z nikim, dziala niezaleznie od Discorda graczy.

Co to jest: Electron/Node.js app z WebRTC proximity audio, hook na pozycje graczy w pamieci AU.
Repo: github.com/OhMyGuus/BetterCrewLink. Open source, ugruntowana reputacja w spolecznosci AU.

V1 (~1-2 dni): SUSModder pobiera BCL z GitHub releases, rozpakowuje do %APPDATA%\SUSModder\Tools\,
uruchamia jako oddzielny proces. IBetterCrewLinkService: IsInstalled, Install, Launch, Stop, IsRunning.
Panel UI warunkowy na SupportsVoiceChat w ModConfiguration.

V2 (~2-3 dni, po V1): status przez localhost API BCL (localhost:1337 - do weryfikacji).
V3 (~5-7 dni, opcjonalne): fork BCL, interface w SUSModder (Avalonia). Bardzo ambitne.

Rekomendacja: V1 dla MVP Etapu 3.

Bezpieczenstwo: BCL hookuje pamiec AU po pozycje graczy (ryzyko AV istnieje).
SUSModder uruchamia go jako oddzielny proces - nie wstrzykuje nic bezposrednio.

---

## i18n - nowe klucze (PL + EN)

Etap 1 - Code Sharing + Chat
  Lobby.Panel.Title         | Lobby | Lobby
  Lobby.Panel.TabCodes      | Kody ({0}) | Codes ({0})
  Lobby.Panel.TabMessages   | Ogloszenia ({0}) | Announcements ({0})
  Lobby.Panel.Refresh       | Odswiez | Refresh
  Lobby.Panel.NoCodes       | Brak aktywnych kodow | No active codes
  Lobby.Panel.NoMessages    | Brak ogloszen | No announcements
  Lobby.Panel.Loading       | Ladowanie... | Loading...
  Lobby.Panel.ServiceUnavailable | Usluga niedostepna | Service unavailable
  Lobby.Code.CopyButton     | Kopiuj | Copy
  Lobby.Code.PublishButton  | Udostepnij kod | Share code
  Lobby.Code.DeleteButton   | Usun moj kod | Remove my code
  Lobby.Code.MinutesAgo     | {0} min temu | {0} min ago
  Lobby.Code.JustNow        | Przed chwila | Just now
  Lobby.Code.InvalidFormat  | Nieprawidlowy format kodu lobby | Invalid lobby code format
  Lobby.Code.RateLimited    | Poczekaj przed kolejnym udostepnieniem | Wait before sharing again
  Lobby.Code.PublishSuccess | Kod udostepniony! | Code shared!
  Lobby.Code.Copied         | Skopiowano! | Copied!
  Lobby.Message.Placeholder | Szukam graczy, reklamuje serwer... (max 280) | Looking for players... (max 280)
  Lobby.Message.PublishButton    | Opublikuj ogloszenie | Post announcement
  Lobby.Message.DeleteButton     | Usun moje ogloszenie | Remove my announcement
  Lobby.Message.TooShort         | Wiadomosc za krotka | Message too short
  Lobby.Message.TooLong          | Wiadomosc za dluga (max 280) | Message too long (max 280)
  Lobby.Message.DisallowedUrl    | Dozwolone tylko discord.gg/... | Only discord.gg/... links allowed
  Lobby.Message.TooManyLinks     | Maksymalnie 1 link Discord | Maximum 1 Discord link
  Lobby.Message.ContentBlocked   | Tresc nie przeszla moderacji | Content blocked by moderation
  Lobby.Message.Duplicate        | Identyczna tresc juz istnieje | Identical content already exists
  Lobby.Message.DailyLimitReached | Osiagnieto dzienny limit | Daily limit reached
  Lobby.Message.UserBanned       | Konto tymczasowo zablokowane | Account temporarily blocked
  Lobby.Message.PublishSuccess   | Ogloszenie opublikowane! | Announcement posted!
  Lobby.Message.ModerationWarning | Uwaga: kolejne naruszenia zablokuja konto | Warning: further violations will block the account
  Lobby.Report.Button       | Zglos | Report
  Lobby.Report.Spam         | Spam | Spam
  Lobby.Report.Inappropriate | Nieodpowiednia tresc | Inappropriate content
  Lobby.Report.Scam         | Oszustwo | Scam
  Lobby.Report.Done         | Zgloszono | Reported
  Lobby.Region.EU / NA / AS / SA / AU / Other - standardowe nazwy PL i EN

Etap 2
  Lobby.Panel.TabFound   | Znalezione ({0}) | Found ({0})
  LobbySearch.Scanning   | Skanowanie serwerow... | Scanning servers...
  LobbySearch.NoResults  | Brak lobby dla tego moda | No lobbies found
  LobbySearch.JoinButton | Dolacz | Join

Etap 3
  BCL.Panel.Title        | Voice Chat (BetterCrewLink) | Voice Chat (BetterCrewLink)
  BCL.Install            | Zainstaluj | Install
  BCL.Launch             | Uruchom | Launch
  BCL.Stop               | Zatrzymaj | Stop
  BCL.Status.Running     | Dziala - {0} graczy polaczonych | Running - {0} players connected
  BCL.Status.Stopped     | Zatrzymany | Stopped
  BCL.Status.NotInstalled | Niezainstalowany | Not installed
  BCL.Installing         | Instalowanie BetterCrewLink... | Installing BetterCrewLink...

---

## Bezpieczenstwo i prywatnosc

| Ryzyko | Etap | Ocena | Mitygacja |
|---|---|---|---|
| Spam kodow / wiadomosci | 1 | Srednie | Rate limit W1 + daily cap |
| Flood identyczna trescia | 1 | Niskie | Content hash dedup W2 |
| Phishing / reklamy | 1 | Srednie | URL allowlist discord.gg W3 |
| Wulgaryzmy | 1 | Niskie-Srednie | Word filter + blocklist W4 |
| Celowe naduzycia jednego gracza | 1 | Srednie | Heat + shadow ban W5 (uzytkownik nie wie ze jest banowany) |
| Weaponized reporting | 1 | Niskie | Anti-reporter heat W6 |
| Ujawnienie tozsamosci | 1 | Brak | Zero nickow; userHash niewidoczny dla innych |
| Reklama konkurencji | 1 | Niskie | Blocklist kategoria competitor W4 |
| Ban IP od operatora AU | 2 | Srednie | Cooldown min 60s; skanowanie tylko na zadanie |
| BCL hookuje pamiec AU | 3 | Niskie | Ugruntowana reputacja; SUSModder uruchamia jako oddzielny proces |

---

## Decyzje wymagajace odpowiedzi wlasciciela

| # | Pytanie | Opcje | Rekomendacja |
|---|---|---|---|
| D1 | Backend Etapu 1? | susmodder.app / clair-api | susmodder.app jesli chat; clair-api jesli tylko kody |
| D2 | Czy chat wchodzi do scope Etapu 1? | Tak / Nie / Moze V2 | <- wlasciciel |
| D3 | TTL wiadomosci chatowych? | 2h / 4h / 8h | 4h |
| D4 | TTL kodu lobby? | 15 / 20 min | 20 min |
| D5 | DLL bridge w MVP czy V2? | MVP / V2 | V2 |
| D6 | Dostep do kodu syzyfowego moda? | Tak / Nie | <- wlasciciel |
| D7 | Python PoC Etapu 2 kontynuowac czy C#? | Python / C# | Python najpierw |
| D8 | Etap 2: zakladka czy osobne okno? | Zakladka / Okno | Zakladka w widoku lobby |
| D9 | BCL V1 czy V2? | V1 / V2 | V1 |
| D10 | Admin panel moderacji (susadmin) w MVP czy V2? | MVP / V2 | V2 - warstwy 0-6 wystarczaja na start |

---

## Sugerowana kolejnosc implementacji

ETAP 1 - Code Sharing + Chat (po odpowiedziach na D1-D6)

  E1-F0 (pol dnia) - Weryfikacja
    Potwierdzenie D1-D6, ustalenie mod_id dla TOU-Mira i ToU-R,
    weryfikacja Mira API: czy istnieje zdarzenie tworzenia lobby?

  E1-F1 (1-1.5 dnia) - Backend susmodder.app
    Tabela lobby_board + lobby_blocklist + lobby_reports
    POST /api/lobby-board (7 warstw moderacji, TTL)
    GET /api/lobby-board (filtrowanie, shadow ban filter)
    DELETE + report endpoint
    Redis: rate limit + heat system
    Cleanup cron

  E1-F2 (pol dnia) - SUSModder.Core
    ILobbyBoardService + implementacja HTTP
    LobbyBoardEntry + PostEntryResult modele
    Walidacja kliencka (regex, URL, content hash)
    LobbyBoardEndpoint w appsettings.json
    ModConfiguration.SupportsLobbySharing

  E1-F3 (1 dzien) - UI
    LobbyBoardPanelViewModel (zakladki Kody/Ogloszenia, auto-refresh 30s)
    LobbyCodePanel.axaml + LobbyMessagePanel.axaml + LobbyBoardItemView
    Licznik znakow na polu wiadomosci
    Integracja w widoku moda (warunkowa na SupportsLobbySharing)
    i18n PL + EN (Etap 1)

  E1-F4 (2-3 dni) - DLL Bridge TOU-Mira [V2]
    Nowe repo SUSModder.Bridge.TouMira, Harmony patch, LobbyBridgeFileReader.cs

  E1-F5 (1-2 dni) - DLL Bridge ToU-R / syzyfowy mod [V2, warunek D6]

  E1-F6 (pol dnia) - SignalR real-time [V2, opcjonalne]

ETAP 2 - Lobby Searcher (warunek: Python PoC potwierdzony)
  E2-F1: SUSModder.Core/Lobby/ (RegionLoader, LobbyClient, LobbyParser, ModFingerprinter)
  E2-F2: zakladka "Znalezione" w widoku lobby

ETAP 3 - BetterCrewLink (niezalezny od Etapu 1 i 2)
  E3-V1 (1-2 dni): IBetterCrewLinkService + panel UI + i18n
  E3-V2 (2-3 dni): status przez localhost API BCL