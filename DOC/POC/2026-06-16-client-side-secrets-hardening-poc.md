# POC: Usunięcie sekretów z klienta desktopowego

**Data:** 2026-06-16  
**Status:** POC / plan do decyzji przed implementacją  
**Priorytet:** P1 bezpieczeństwo + P2 porządek architektury API  
**Zakres:** SUSModder desktop, `SUSModder.Core`, backend `susmodder.app` / API v2, Steam/Epic download flows  
**Powiązane:** `SUSModder.Core/Secrets.cs`, `SUSModder.Core/Secrets.cs.example`, `DOC/POC/API v2/README.md`, `DOC/POC/API v2/consumer-susmodder-3x.md`, `DOC/POC/STEAM_REFACTOR/2026-06-01-depotdownloader-migration-poc.md`, `DOC/PLAN/security-audit-discord-oauth2.md`

---

## 1. Decyzja kierunkowa

`Secrets.cs` nie powinien być docelowym miejscem przechowywania żadnych realnych sekretów aplikacji. Base64, obfuskacja albo lokalne szyfrowanie w pliku wykonywalnym nie rozwiązują problemu, bo aplikacja desktopowa musi mieć komplet danych potrzebnych do odszyfrowania i użycia sekretu. Każdy sekret w paczce Velopack / `.exe` należy traktować jako możliwy do odzyskania przez użytkownika.

Docelowy model:

1. **Desktop nie zawiera sekretów backendowych, adminowych ani globalnych download tokenów.**
2. **Publiczne odczyty API są naprawdę publiczne** albo chronione tylko rate limitingiem / abuse detection po stronie backendu.
3. **Pobieranie plików jest autoryzowane przez backend**, jeśli trzeba, przy pomocy krótkotrwałych URL-i / ticketów scoped do konkretnego pliku.
4. **Sekrety produkcyjne żyją po stronie backendu, CI/CD albo operatora**, nie w aplikacji użytkownika.
5. **Hasło 7z nie jest traktowane jako zabezpieczenie**; to co najwyżej legacy fallback do czasu migracji na DepotDownloader / manifest pinning / SHA256.

---

## 2. Cel i nie-cele

### 2.1 Cel

- Usunąć potrzebę trzymania `GetDownloadToken()` i `Get7zPassword()` w `SUSModder.Core/Secrets.cs` dla standardowego klienta desktopowego.
- Rozdzielić typy poświadczeń:
  - publiczne parametry klienta,
  - użytkownikowe tokeny sesyjne/OAuth,
  - backend/admin secrets,
  - krótkotrwałe download tickets.
- Dopasować klienta do API v2, gdzie katalog, kompatybilność, role i downloady modów są opisane jako publiczne lub rate-limited, a nie jako zależne od globalnego sekretu desktopowego.
- Zmniejszyć skutki wycieku: nawet jeśli ktoś odczyta binarkę, nie dostaje trwałego tokena do backendu.

### 2.2 Nie-cele

- Nie próbujemy „ukrywać lepiej” sekretów w `.exe` przez własne szyfrowanie, DPAPI, XOR, zasoby binarne, split stringi ani obfuskację jako główne zabezpieczenie.
- Nie przenosimy adminowego dostępu do desktopa.
- Nie robimy pełnej przebudowy systemu kont użytkowników.
- Nie usuwamy od razu wszystkich legacy endpointów v1, jeśli są potrzebne dla kompatybilności 2.x.
- Nie rozwiązujemy tu całego tematu legalności redystrybucji vanilli Steam; ten temat jest opisany osobno w POC DepotDownloader.

---

## 3. Stan obecny

### 3.1 `Secrets.cs`

Obecnie lokalny `SUSModder.Core/Secrets.cs` zawiera statyczny provider:

- `GetDownloadToken()` — base64 → string, używany jako `Authorization`,
- `Get7zPassword()` — base64 → string, używany do rozpakowania paczki vanilla 7z.

`Secrets.cs.example` sugeruje wstawianie wartości w base64. Sam plik `Secrets.cs` jest ignorowany w `SUSModder.Core/.gitignore`, ale po zbudowaniu aplikacji wartości trafiają do dystrybuowanej paczki.

### 3.2 Miejsca użycia `SecretProvider`

| Obszar | Pliki | Obecne użycie | Docelowo |
|---|---|---|---|
| Pobieranie modów/DLL | `ModManager.cs`, `SteamVanillaProvider.cs` | `Authorization: <downloadToken>` przy downloadach | Brak globalnego tokena; publiczny endpoint albo krótkotrwały ticket/signed URL |
| Config/API legacy | `ModConfigHandler.cs`, `SUSModderApiClient.cs` | token przy wybranych requestach | API client bez domyślnego sekretu; auth tylko dla endpointów, które faktycznie go wymagają |
| SUStats / Among tokens | `SUStatsService.cs` | token przy pobieraniu/walidacji danych | Osobny model auth: Discord OAuth/PKCE albo server-side token, nie globalny desktop secret |
| Vanilla 7z | `SteamVanillaProvider.cs` | `Get7zPassword()` do ekstrakcji | DepotDownloader/manifest pinning jako primary; fallback 7z traktowany jako legacy |

### 3.3 Istniejące dokumenty wskazujące kierunek

- `DOC/POC/API v2/README.md` opisuje `/catalog`, `/versions`, `/compatibility`, `/roles`, `/online`, `/downloads/mod/:id/:version` jako publiczne/rate-limited, a admin auth jako osobną warstwę.
- `DOC/POC/API v2/consumer-susmodder-3x.md` mówi, że auth jest wymagany dla `/lobby`, a katalog/downloady są publiczne.
- `DOC/PLAN/security-audit-discord-oauth2.md` wskazuje, że PKCE unika sekretu w binarce, a tokeny użytkownika powinny być przechowywane lokalnie z ochroną typu DPAPI.
- `DOC/POC/STEAM_REFACTOR/2026-06-01-depotdownloader-migration-poc.md` wskazuje, że hasło 7z staje się niepotrzebne przy DepotDownloader, ale fallback nadal go używa.

---

## 4. Model zagrożeń

### 4.1 Co zakładamy

- Użytkownik ma pełny dostęp do własnej maszyny i może:
  - odczytać pliki aplikacji,
  - odpalić debugger,
  - zdekompilować IL,
  - podejrzeć ruch HTTPS po zainstalowaniu własnego certyfikatu root,
  - przechwycić stringi w pamięci procesu.
- Aplikacja jest dystrybuowana publicznie przez Velopack / stronę SUSModder.
- Nie mamy gwarancji, że sekret w kliencie pozostanie prywatny dłużej niż do pierwszej publikacji release.

### 4.2 Czego bronimy

- Backend przed nadużyciem trwałego globalnego tokena.
- Endpointy adminowe/write przed wywołaniem przez zwykły desktop.
- CDN/storage przed masowym scrapowaniem poza rate limitingiem i signed URL TTL.
- Użytkowników przed instalacją plików o niezgodnym SHA256.
- Logi i telemetrię przed przypadkowym wyciekiem tokenów.

### 4.3 Czego nie da się obronić po stronie klienta

- Hasła 7z potrzebnego do lokalnego rozpakowania.
- Trwałego bearer tokena wbudowanego w exe.
- Dowolnego „sekretu”, którego desktop musi użyć bez kontaktu z backendem.

---

## 5. Proponowana architektura

### 5.1 Klasyfikacja wartości konfiguracyjnych

| Typ | Przykłady | Gdzie trzymać | Uwagi |
|---|---|---|---|
| Public config | base URL, endpointy, public app id, public key do weryfikacji podpisu | `appsettings.json` read-only albo zasoby aplikacji | Może być w repo i paczce |
| User settings | język, motyw, update channel, telemetry opt-in | SQLite `user_settings` przez `UserSettingsService` | Bez sekretów backendowych |
| User tokens | Discord OAuth access/refresh token, jeśli potrzebny | SQLite + DPAPI/OS store | Token użytkownika, nie globalny sekret aplikacji |
| Backend secrets | `HTTP_TOKEN`, admin API secret, ClairBot secret, storage signing key | backend env / secret manager / CI secrets | Nigdy w desktopie |
| Build secrets | certyfikaty, deploy keys, upload credentials | GitHub Actions secrets / lokalny password manager / SOPS+age | Tylko pipeline/operator |
| Download authorization | ticket/signed URL per plik, TTL 5–15 min | generowany przez backend | Scope: plik + wersja + krótki czas |

### 5.2 Download flow dla modów

Wariant preferowany dla API v2:

```text
SUSModder -> GET /api/v2/catalog/:id
SUSModder <- variant metadata: version, sha256, download endpoint

SUSModder -> GET /api/v2/downloads/mod/:id/:version
backend -> rate limit + wybór wariantu + ewentualny signed URL
backend <- 302 redirect albo stream
SUSModder -> pobiera plik
SUSModder -> liczy SHA256 i porównuje z katalogiem / headerem
```

Jeśli storage/CDN wymaga prywatnego dostępu, sekret signing key zostaje wyłącznie w backendzie:

```text
SUSModder -> GET /api/v2/downloads/mod/:id/:version
backend -> generuje signed URL ważny np. 10 minut
backend -> 302 Location: https://cdn/...signature=...
```

Desktop nie zna żadnego storage secretu ani globalnego bearer tokena.

### 5.3 Config/catalog flow

- `GET /susmodder-config` v1 może pozostać kompatybilny dla 2.x, ale klient 3.x powinien preferować API v2.
- API v2: `GET /catalog`, `/catalog/:id`, `/catalog-meta`, `/compatibility` — publiczne z ETag/304 i rate limitingiem.
- Desktop nie powinien doklejać `Authorization` do publicznych requestów.
- Admin/write endpointy powinny być niedostępne z desktopa bez osobnego operator toolingu.

### 5.4 SUStats / Among tokens

Nie mieszać globalnego `GetDownloadToken()` z autoryzacją użytkownika lub serwera Discord.

Preferowany kierunek:

- user-facing flow: Discord OAuth2 PKCE zgodnie z istniejącym POC,
- server-to-server sync: ClairBot secret po stronie ClairBot/backendu,
- desktop: używa tylko tokenu użytkownika albo publicznego odczytu przypisanego do sesji, bez globalnego sekretu aplikacji.

### 5.5 Vanilla Steam / hasło 7z

Docelowo:

1. DepotDownloader + manifest pinning jako primary source dla Steam vanilla.
2. SHA256/manifest validation dla każdego pobranego artefaktu.
3. 7z fallback tylko jako legacy / awaryjna ścieżka.
4. Hasło 7z nie jest zabezpieczeniem; jeśli fallback zostaje, trzeba założyć, że hasło jest publiczne.

Jeśli fallback 7z musi zostać przez pewien czas:

- ograniczyć dostęp do archiwów po stronie backend/CDN,
- signed URL/TTL zamiast globalnego tokena,
- traktować hasło jako warstwę kompatybilności, nie jako DRM/security.

---

## 6. User workflow

### 6.1 Standardowy użytkownik

1. Uruchamia SUSModder.
2. Aplikacja pobiera katalog/publiczne metadane bez sekretu.
3. Użytkownik wybiera mod.
4. Aplikacja pobiera paczkę moda przez publiczny/rate-limited endpoint lub signed redirect.
5. Aplikacja weryfikuje SHA256.
6. Instalacja przebiega jak dotychczas.

Brak nowych promptów bezpieczeństwa dla zwykłego flow.

### 6.2 Użytkownik SUStats / Discord

1. Użytkownik klika logowanie/połączenie z Discordem.
2. OAuth2 PKCE przez przeglądarkę/system flow.
3. Token użytkownika trafia do lokalnego storage z ochroną OS, nie do `Secrets.cs`.
4. Wylogowanie usuwa token i cache danych konta.

### 6.3 Operator/admin

- Admin/write operacje są wykonywane przez backend panel, CLI albo CI/CD.
- Desktop publiczny nie zawiera admin tokena.
- Secrets operatora są w password managerze, CI secrets, `.env` na serwerze albo secret managerze.

---

## 7. Core business logic responsibilities

- Wprowadzić warstwę typu `IApiAuthProvider` / `IDownloadAuthorizationProvider`, ale z domyślną implementacją „no global client secret”.
- Usunąć bezpośrednie zależności Core od statycznego `SecretProvider` w ścieżkach runtime.
- `SUSModderApiClient` powinien:
  - nie dodawać `Authorization` domyślnie,
  - dodawać auth tylko jawnie dla user-token flows,
  - redagować tokeny w logach,
  - nie mieć fallbacku HTTPS → HTTP przy requestach z poświadczeniami.
- `ModManager` / download helpers powinny:
  - pobierać URL/ticket przez backend albo korzystać z publicznego endpointu,
  - weryfikować SHA256 po pobraniu,
  - rozróżniać błędy: `DOWNLOAD_UNAUTHORIZED`, `DOWNLOAD_RATE_LIMITED`, `DOWNLOAD_HASH_MISMATCH`, `DOWNLOAD_EXPIRED_TICKET`.
- `SteamVanillaProvider` powinien traktować hasło 7z jako legacy adapter do usunięcia po migracji DepotDownloader.

---

## 8. UI/Avalonia responsibilities

- Nie pokazywać użytkownikowi pojęcia „download token”.
- Dodać lokalizowane komunikaty dla nowych stanów:
  - link/ticket wygasł — spróbuj ponownie,
  - limit pobrań — poczekaj chwilę,
  - plik nie przeszedł weryfikacji integralności,
  - usługa chwilowo niedostępna.
- Utrzymać retry flow w instalatorze modów, ale retry ma odświeżać ticket/signed URL, nie ponownie używać globalnego tokena.
- Dla OAuth/SUStats: jasny status zalogowania, wylogowanie, usunięcie lokalnych danych.

---

## 9. Language / i18n impact

Wpływ umiarkowany, bo główna zmiana jest architektoniczna, ale pojawią się nowe user-facing błędy.

Wymagania:

- Wszystkie nowe komunikaty w PL i EN jako klucze i18n.
- Fallback locale: `pl`.
- Nie hardcodować komunikatów w `Core`; Core powinien zwracać stabilne error codes + techniczny fallback.
- Placeholdery muszą mieć parytet PL/EN, np. `{retryAfterSeconds}`, `{modName}`.
- Jeśli pokazujemy liczby prób / sekund, użyć ICU MessageFormat albo unikać pluralizacji w MVP.
- Telemetria może wysyłać tylko kanoniczny język aplikacji (`pl`/`en`), bez tokenów i bez surowego system locale.
- Dodanie przyszłego języka nie może wymagać zmiany logiki download/auth.

Przykładowe klucze:

| Key | PL | EN |
|---|---|---|
| `download.ticketExpired` | Link pobierania wygasł. Spróbujemy pobrać nowy. | The download link expired. We will request a new one. |
| `download.rateLimited` | Zbyt wiele prób pobierania. Spróbuj ponownie za chwilę. | Too many download attempts. Try again shortly. |
| `download.hashMismatch` | Pobrany plik nie przeszedł weryfikacji integralności. Instalacja została przerwana. | The downloaded file failed integrity verification. Installation was stopped. |
| `auth.sessionExpired` | Sesja wygasła. Zaloguj się ponownie. | The session expired. Sign in again. |

---

## 10. Config i migracja

- `appsettings.json` pozostaje read-only i zawiera tylko publiczne endpointy/domysły.
- Nie dodawać sekretów do SQLite `user_settings`.
- Jeśli istnieją user-tokeny OAuth, przechowywać je w osobnej tabeli / repozytorium z ochroną OS, zgodnie z POC Discord OAuth2.
- Nie migrować `Secrets.cs` do SQLite — to byłoby tylko przeniesienie problemu.
- `Secrets.cs.example` docelowo powinien zniknąć albo zostać zastąpiony neutralnym opisem: „desktop nie wymaga sekretów; sekrety backendowe konfiguruj na serwerze”.
- Po usunięciu runtime zależności od `SecretProvider`, build nie powinien wymagać lokalnego `Secrets.cs`.

---

## 11. Platform, packaging, updater, telemetry, privacy, AV

### Platform

- Windows desktop: nie używać DPAPI do ochrony globalnych sekretów aplikacji, bo to nie rozwiązuje dystrybucji sekretu. DPAPI ma sens dla tokenów użytkownika zapisanych po zalogowaniu.
- Linux/Steam Deck przyszłościowo: nie zakładać DPAPI; user-token storage wymaga osobnego adaptera OS/keyring albo jawnego fallbacku.

### Packaging / Velopack

- Release package nie może zawierać `Secrets.cs`-derived strings.
- Weryfikować paczkę po buildzie przez grep/ILSpy/string scan na znane tokeny i nazwy kluczy.
- Brak sekretów w paczce poprawia story AV/reputation: mniej podejrzanej obfuskacji, mniej wrażliwych stringów.

### Updater

- Velopack manifesty i release checks pozostają publiczne/rate-limited.
- Podpisy/hashy manifestów są integralnością, nie sekretem.
- Update channel (`release`/`beta`) nadal w user settings, bez wpływu na auth.

### Telemetry / privacy

- Nie logować i nie wysyłać tokenów, signed URL-i, query stringów z podpisami ani sekretów.
- W logach redagować `Authorization`, `token`, `secret`, `signature`, `X-Admin-API-Secret`.
- Telemetria błędów download może zawierać error code, HTTP status bucket, endpoint category, app version, locale; nie pełny URL z podpisem.

### AV / abuse

- Rate limiting i abuse detection po stronie backendu zamiast ukrytego tokena w kliencie.
- Signed URL z krótkim TTL ogranicza masowe udostępnianie bez łamania normalnego UX.
- SHA256 blokuje instalację plików podmienionych przez mirror/proxy.

---

## 12. Backend implications

Backend `susmodder.app` powinien rozdzielić endpointy:

| Grupa | Auth | Przykłady |
|---|---|---|
| Public read | brak auth + rate limit + cache | catalog, roles, compatibility, releases, online |
| Public download | brak auth albo signed redirect generowany server-side | `/api/v2/downloads/mod/:id/:version` |
| Soft identity | `X-User-Hash` lub user session | lobby ownership/rate limits |
| User auth | OAuth/session token | SUStats/user-specific data |
| Admin/write | backend-only secret / admin login / CI secret | admin catalog, uploads, sync, config write |
| Server-to-server | ClairBot secret, CI deploy secret | ClairBot sync, release upload |

Ważne: jeśli v1 nadal wymaga `HTTP_TOKEN`, to token v1 nie może dawać uprawnień admin/write używanych przez desktop. Najlepiej zrobić go opcjonalnym dla legacy read albo zastąpić neutralnym public endpointem.

---

## 13. Verification plan

### 13.1 Repo/code checks

- `grep`/LSP: brak runtime użyć `SecretProvider` w desktop/Core download flows.
- Brak `Secrets.cs` jako wymaganego pliku do lokalnego buildu.
- Brak `Authorization` na publicznych endpointach katalogu/downloadów.
- Brak HTTPS → HTTP fallbacku dla requestów z poświadczeniami.
- Log redaction test dla headerów i query stringów.

### 13.2 Build/package checks

- `dotnet build SUSModder.sln` bez lokalnego `Secrets.cs`.
- Velopack package scan: brak znanych tokenów, 7z password, `HTTP_TOKEN`, admin secret.
- Smoke test aktualizacji: release/beta channels bez auth regressions.

### 13.3 API tests

- Public catalog/download działa bez `Authorization`.
- Admin/write endpointy odrzucają brak admin auth.
- Signed URL/ticket wygasa po TTL.
- Rate limiting zwraca stabilny error code i `Retry-After`, jeśli dotyczy.
- Download bez SHA256 w DB zwraca `VARIANT_NOT_FOUND` / nie pozwala instalować.

### 13.4 Manual QA

- Instalacja full moda Steam/Epic bez lokalnego `Secrets.cs`.
- Instalacja DLL moda bez lokalnego `Secrets.cs`.
- Offline/timeout/retry flow.
- Hash mismatch symulowany lokalnie — plik odrzucony.
- Ticket expired — aplikacja pobiera nowy ticket i retry działa.
- PL/EN komunikaty dla błędów download/auth.

---

## 14. Suggested implementation order

### Faza 0 — decyzja i rotacja

1. Przyjąć zasadę: desktop nie zawiera globalnych sekretów.
2. Sprawdzić, jakie uprawnienia ma obecny `GetDownloadToken()` po stronie backendu.
3. Jeśli token był w release — traktować jako ujawniony i zaplanować rotację.
4. Rozdzielić backendowe tokeny: read/download vs admin/write.

### Faza 1 — backend/API compatibility

1. Upewnić się, że API v2 public read/download nie wymaga desktop secretu.
2. Dodać/zweryfikować rate limiting i ETag/304.
3. Jeśli potrzebny prywatny storage: dodać signed redirect/ticket server-side.
4. Upewnić się, że admin/write endpointy nie akceptują tokena desktopowego.

### Faza 2 — Core download layer

1. Dodać centralny download client bez `SecretProvider`.
2. Przenieść SHA256 verification do jednej ścieżki.
3. Zamienić bezpośrednie `HttpClient + Authorization` w `ModManager` i `SteamVanillaProvider`.
4. Dodać stable error codes dla UI.

### Faza 3 — config/API client cleanup

1. `SUSModderApiClient`: `IncludeAuthToken` zastąpić jawnie nazwanymi auth modes albo usunąć dla publicznych endpointów.
2. `ModConfigHandler`: usunąć legacy HTTPS→HTTP fallback przy tokenach.
3. SUStats: rozdzielić Discord/user auth od globalnego desktop tokena.

### Faza 4 — 7z fallback reduction

1. Wdrożyć DepotDownloader primary path zgodnie z POC Steam refactor.
2. Ograniczyć 7z do awaryjnego fallbacku lub usunąć po stabilizacji.
3. Jeśli 7z zostaje: password traktować jako publiczne legacy, a dostęp do archiwum chronić backend/signed URL.

### Faza 5 — usunięcie `Secrets.cs`

1. Build bez `Secrets.cs`.
2. Usunąć albo zdeprecjonować `Secrets.cs.example`.
3. Dodać CI/package scan na sekretowe stringi.
4. Zaktualizować dokumentację developerską.

### Zadania równoległe

- Backend: endpointy public/download/admin separation, signed URLs, rate limits.
- Core: centralizacja download clienta i hash verification.
- UI/i18n: nowe komunikaty PL/EN.
- Security/QA: log redaction, package scanning, token rotation checklist.
- Steam: DepotDownloader/manifest pinning i redukcja 7z fallbacku.

---

## 15. Otwarte pytania

1. Jakie dokładnie uprawnienia ma obecny `GetDownloadToken()` na backendzie?
2. Czy v1 desktop 2.x musi nadal wysyłać token, czy backend może zaakceptować publiczne read-only requesty?
3. Czy pliki modów mają być publicznie dostępne przez CDN, czy wymagają signed URLs?
4. Jak długo utrzymujemy fallback 7z dla Steam vanilla?
5. Czy SUStats w desktopie ma docelowo wymagać Discord OAuth, czy wystarczy publiczny/anonimowy odczyt wybranych danych?
6. Gdzie będzie kanoniczny secret manager dla backendu: `.env` na VPS, GitHub Actions secrets, SOPS+age, Azure Key Vault/Vault?

---

## 16. Rekomendacja MVP

Najmniejszy sensowny krok bez rewolucji:

1. Backend: publiczne/rate-limited downloady modów + SHA256 wymagany w DB.
2. Core: usunąć `Authorization: SecretProvider.GetDownloadToken()` z downloadów modów i katalogu API v2.
3. Admin/write: upewnić się, że nie używa tokena desktopowego.
4. Zostawić 7z password tylko jako jawnie opisany legacy fallback do czasu DepotDownloader.
5. Dodać package scan i log redaction.

To daje największą poprawę bezpieczeństwa bez blokowania aktualnego UX instalacji modów.

---

## 17. Źródła użyte

- `SUSModder.Core/Secrets.cs`
- `SUSModder.Core/Secrets.cs.example`
- `SUSModder.Core/GameIntegration/ModManager.cs`
- `SUSModder.Core/GameIntegration/SteamVanillaProvider.cs`
- `SUSModder.Core/Configuration/SUStatsService.cs`
- `SUSModder.Core/Api/SUSModderApiClient.cs`
- `DOC/POC/API v2/README.md`
- `DOC/POC/API v2/consumer-susmodder-3x.md`
- `DOC/POC/STEAM_REFACTOR/2026-06-01-depotdownloader-migration-poc.md`
- `DOC/PLAN/security-audit-discord-oauth2.md`
- `mcp-rag` dla repo i backendu
- `sus-free-doc-scout` dla szerokiego skanu dokumentacji
- Microsoft Learn: app secrets, environment variables, Key Vault, DPAPI/Data Protection guidance

