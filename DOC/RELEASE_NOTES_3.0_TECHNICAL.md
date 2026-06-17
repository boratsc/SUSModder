# SUSModder 3.x — changelog techniczny

**Wersja:** `3.0.0-beta1`  
**Branch:** `susmodder-3.0`  
**Zakres:** zmiany względem linii 2.x  
**Status Beta 1:** gotowe do publikacji testowej; bez blockerów i must-fixów w review Beta 1.

---

## Najważniejsze zmiany architektoniczne

### API v2 i centralny klient HTTP

- Dodano centralny `SUSModderApiClient` i modele DTO dla katalogu, kompatybilności, modpacków, changelogów oraz supportu.
- Klient korzysta z nowych endpointów API v2 / CDN, przy zachowaniu kompatybilności z legacy zachowaniami tam, gdzie jest to potrzebne.
- Dodano sync katalogu i kompatybilności z cache po stronie klienta.
- Dodano smoke testy API v2 oraz testy kontraktowe dla istotnych przepływów.

### SQLite jako podstawowa warstwa danych

- Runtime config przeniesiony z plików JSON do SQLite.
- Dodano repozytoria dla modów, ustawień użytkownika, konfiguracji ToU, instancji modpacków, cache kompatybilności, stanów sync i danych Discord/SUStats.
- Dodano migracje schematu oraz import danych z legacy JSON.
- `ConfigManager` i `UserSettingsService` działają jako fasady/fallbacki, ale preferują repozytoria SQLite.

### Nowa struktura UI 3.0

- Przebudowano główny ekran na układ Browser + Inspector.
- Dodano większe karty modów, osobne tryby przeglądania katalogu, dodatków DLL i modpacków.
- Dodano bulk operations, kolejkę instalacji i UI do zaznaczania wielu pozycji.
- Dodano lokalne instancje modpacków i widoki „Moje zestawy”.
- Dodano nowy motyw szklany/Aero Liquid Glass oraz fallbacki dla mniej wspieranych środowisk.

---

## Funkcje użytkowe / produktowe

### Modpacki

- Dodano pełny system modpacków:
  - tworzenie zestawów,
  - import przez kod/link/deep link,
  - historia modpacków,
  - lokalne instancje,
  - instalacja jako nowa instancja,
  - podgląd zmian przed instalacją.
- Dodano obsługę custom content:
  - zewnętrzne DLL,
  - deklaracje GitHub DLL/FULL,
  - walidacje bezpieczeństwa,
  - statusy VirusTotal / `vtStatus`.
- Dodano zabezpieczenia instalatora modpacków: walidacja kodów, ochrona przed path traversal i limity rozmiaru.

### Changelogi modów

- Dodano pobieranie changelogów modów z API.
- Dodano `ModChangelogService` z cache TTL i obsługą ETag/304.
- Dodano modal changeloga z renderowaniem Markdown.
- Changelog jest dostępny z panelu szczegółów moda, dialogu sukcesu instalacji i podsumowania aktualizacji.

### Lobby Board / lobby code sharing

- Dodano tablicę lobby: publikacja i przeglądanie kodów lobby/ogłoszeń.
- Dodano walidację wpisów, TTL, integrację z hardware hash i lokalizacje PL/EN.
- Dodano bridge do autodetekcji kodu lobby z gry/DLL, gdzie mod to wspiera.

### Discord OAuth2 / SUStats / Clair

- Dodano Discord OAuth2 PKCE.
- Dodano integrację SUStats/Clair i repozytoria auth.
- Naprawiono anti-CSRF `state` w OAuth.
- Dodano czyszczenie/odświeżanie credentials przy błędach autoryzacji.

### Diagnostyka uruchamiania i support

- Dodano wspólny launch supervisor i klasyfikację problemów uruchamiania.
- Dodano analizę logów BepInEx.
- Dodano diagnostykę Defender/Firewall/AV oraz UX naprawy tam, gdzie jest to bezpieczne dla użytkownika.
- Dodano fundament AI Support: DTO, klient support API i builder kontekstu diagnostycznego.
- Pełny LLM assistant oraz automatyczne akcje administracyjne pozostają post-beta.

### Steam / Epic / instalacje

- Steam flow został przesunięty w stronę DepotDownloader zamiast ręcznych/starych ścieżek.
- Ulepszono obsługę Epic/legendary i dialogi błędów.
- Dodano centralizację ścieżek aplikacji przez `ApplicationPaths`.
- Dodano defensive DB bootstrap i dodatkowe guardy chroniące dane instalacji.

---

## Bezpieczeństwo i prywatność

- Dodano testy bezpieczeństwa dla modpacków i instalatora.
- Dodano SHA-256 verification helpers oraz integrację statusów skanowania dla custom DLL.
- OAuth używa PKCE i `state` anti-CSRF.
- Support diagnostics są projektowane pod minimalizację danych: kontekst diagnostyczny zamiast automatycznego wysyłania pełnych logów.
- Znane ryzyko: `Secrets.cs` nadal zawiera sekrety zakodowane w binarce; hardening jest świadomie post-beta.

---

## Testy i jakość

- Dodano projekt `SUSModder.Core.Tests`.
- Dodano testy m.in. dla:
  - modpacków,
  - deep linków,
  - walidacji kodów,
  - instalatora modpacków,
  - sync katalogu,
  - repozytoriów SQLite,
  - OAuth PKCE,
  - analizy logów BepInEx,
  - warstw bezpieczeństwa.
- Ostatni status Beta 1 review:
  - `dotnet build SUSModder.sln -c Release` — 0 błędów, 0 ostrzeżeń,
  - `dotnet test SUSModder.Core.Tests -c Release` — 188/188,
  - API smoke — 24 OK, 0 FAIL, 1 EXPECTED,
  - manual E2E — PASS.

---

## i18n / UX copy

- PL i EN traktowane jako podstawowe języki.
- Dodano/uzupełniono klucze i18n dla nowych przepływów: modpacki, changelogi, update success, post-install success, lobby board, OAuth i diagnostyka.
- Beta 1 review potwierdził kompletność kluczy lokalizacyjnych w zakresie wydania.

---

## Build / release / repo cleanup

- Wersja aplikacji ustawiona na `3.0.0-beta1`.
- Release flow pozostaje Velopack-first.
- Buildy są obecnie unsigned — spodziewane ostrzeżenia SmartScreen/AV są znanym ograniczeniem.
- Repo cleanup: `.opencode`, `.vscode` i wygenerowane wyniki testów API nie powinny być już trackowane.
- Zarchiwizowano zamknięte plany, POC-e i frontend ideas związane z zakresem Beta 1.

---

## Znane ograniczenia / post-beta

- Unsigned builds / SmartScreen.
- Client-side secrets hardening.
- Pełny AI/LLM support assistant.
- Automatyczne akcje administracyjne Defender/Firewall.
- Pełny lobby searcher przez skanowanie serwerów Among Us.
- Linux/UMU/Steam runtime.
- Dalszy refactor rozproszonego `HttpClient`, legacy `ConfigRepository` i części ViewModeli bez DI.
