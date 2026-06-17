# Review: Implementacja Discord OAuth2 PKCE — SUSModder

**Data:** 2026-05-27
**Reviewer:** sus-quality-reviewer
**Status:** 🔴 Wymaga poprawek przed implementacją
**Dokumenty źródłowe:**
- \DOC/POC/Discord Oauth - Clair/2026-05-27-discord-oauth2-sustats-auth.md\
- \DOC/POC/Discord Oauth - Clair/sustats-discord-oauth2-endpoints.md\
- \SUSModder.Core/Data/DatabaseService.cs\
- \SUSModder.Core/Data/ModRepository.cs\
- \SUSModder.Core/Data/UserSettingsRepository.cs\
- \SUSModder.Core/Configuration/SUStatsService.cs\
- \SUSModder/ViewModels/SUStatsConfigViewModel.cs\
- \SUSModder/ViewModels/MainWindowViewModel.GameLaunch.cs\
- \SUSModder/Services/SUStatsSessionManager.cs\
- \SUSModder/App.axaml.cs\
- \SUSModder/Services/Localization/LocalizationService.cs\
- \SUSModder/Localization/pl.json\, \n.json\

---

## 🔴 BŁĘDY KRYTYCZNE (blokują implementację)

### 1. Sprzeczność: port loopback — stały vs losowy

**Problem:** Plan zawiera trzy sprzeczne informacje:
- Sekcja 4: Redirect URI \http://localhost:{port}/susmodder/callback\ — sugeruje dynamiczny port
- Sekcja 6.5: \OAuthStartResult(string AuthUrl, int Port)\ — zwraca port dynamicznie
- Sekcja 13: \localhost:random_port\ — potwierdza losowy port

**Rzeczywistość:** Discord OAuth App wymaga **zarejestrowanego** Redirect URI. Nie można zarejestrować \http://localhost:{random_port}\ — Discord nie zaakceptuje wildcard portu.

**Rozwiązanie:** Discord pozwala na zarejestrowanie \http://localhost\ (bez portu) LUB konkretnego portu. Należy:
1. Zarejestrować \http://127.0.0.1\ (bez portu) w Discord Developer Portal
2. Użyć stałego portu (np. 53124) w aplikacji
3. Lub użyć \HttpListener\ z dynamicznym portem, ale wtedy redirect URI musi być \http://localhost\ (Discord akceptuje dowolny port dla localhost bez portu w redirect URI)

**Rekomendacja:** Użyć stałego portu 53124 i zarejestrować \http://127.0.0.1:53124/susmodder/callback\ w Clair Discord App. To eliminuje sprzeczność i upraszcza \OAuthStartResult\ — port nie musi być zwracany.

### 2. PKCE code_verifier — nieprecyzyjna specyfikacja

**Problem:** Plan mówi \"generuje \code_verifier\ (SHA256)\" — to nieprecyzyjne. PKCE S256 wymaga:
- \code_verifier\: 43-128 znaków, losowy string z unreserved characters \[A-Za-z0-9-._~]\
- \code_challenge\: \BASE64URL(SHA256(code_verifier))\ (bez paddingu \=\)

**Ryzyko:** Implementacja może wygenerować niepoprawny verifier (zły charset, zła długość, padding w base64url).

**Rekomendacja:** Dodać do planu dokładny algorytm:
\\\
code_verifier = RandomString(64, charset: A-Za-z0-9-._~)
code_challenge = Base64UrlEncode(SHA256(code_verifier)).TrimEnd('=')
\\\
Użyć \RandomNumber.GetBytes()\ z .NET, nie \Random\.

### 3. SUStatsSessionManager — plain-text secret w %TEMP% pozostaje po migracji

**Problem:** Obecny \SUStatsSessionManager\ zapisuje secret (hasło SUStats) w plain text w \%TEMP%\\SUSModder\\sustats_session.json\. Plan migracji (sekcja 12.2) mówi \"nie migrujemy starych danych\", ale **nie mówi nic o usunięciu tego pliku**.

**Ryzyko bezpieczeństwa:** Po migracji na Discord OAuth, stary plik sesji nadal zawiera secret w plain text. Każdy proces na maszynie użytkownika może go odczytać.

**Rekomendacja:** Dodać krok migracji:
- Przy pierwszym uruchomieniu po migracji: \File.Delete(sustats_session.json)\
- Lub dodać do \DatabaseService.CleanupJsonFiles()\ analogiczną logikę dla plików sesji

### 4. SUStatsService refactor — nieokreślony impact na istniejące zależności

**Problem:** Plan (Faza 5) mówi \"update SUStatsService (usuwa zależność od susmodder-api)\" ale nie precyzuje:
- Czy \SUStatsService\ zostanie zastąpiony nowym serwisem, czy zmodyfikowany in-place?
- Co się dzieje z \SUStatsConfigViewModel.CreateSUStatsService()\ (statyczny factory)?
- Czy \MainWindowViewModel.GameLaunch.cs\ (linie 305-320, 455-527) będzie nadal działać?

Obecnie \MainWindowViewModel.GameLaunch.cs\ wywołuje:
- \SUStatsConfigViewModel.HasSelectedServer\ (static)
- \SUStatsConfigViewModel.GetSelectedServerData()\ (static)
- \SUStatsConfigViewModel.ClearGlobalSelection()\ (static)

Plan mówi (sekcja 7.4): \"zamiast odczytywać \SUStatsConfigViewModel.GetSelectedServerData()\: odczytuje z \ISustatsCredentialsRepository.GetActiveAsync()\\" — ale nie precyzuje **kiedy** następuje przełączenie.

**Rekomendacja:** 
- Dodać warunek feature flag: jeśli Discord OAuth jest skonfigurowany (mamy token w \discord_auth\), używaj nowego flow; w przeciwnym razie fallback do starego.
- Lub: całkowite usunięcie starego flow w tej fazie (ale wtedy backward compat dla starych klientów jest tylko po stronie API, nie w aplikacji).

---

## 🟠 BŁĘDY POWAŻNE (wymagają wyjaśnienia przed implementacją)

### 5. Revoke token na logout — Discord nie obsługuje revocation bez client_secret

**Problem:** Plan (sekcja 6.5) mówi \"Wylogowanie (revoke + czyści DB)\". Discord's token revocation endpoint (\POST /oauth2/token/revoke\) wymaga client authentication. W flow PKCE **nie mamy client_secret** (to celowe).

**Rzeczywistość:** Discord **nie wymaga** revocation — access token wygasa sam (7 dni), refresh token można unieważnić po stronie Discorda tylko z client_secret.

**Rekomendacja:** Zmienić \LogoutAsync\ na:
1. Wyczyść \discord_auth\ z bazy
2. Wyczyść \sustats_credentials\ z bazy (opcjonalnie — user może chcieć zachować credentials)
3. **Nie** wołaj Discord revoke endpoint (nie zadziała bez client_secret)
4. Opcjonalnie: wywołaj \POST /api/susmodder/credentials\ z flagą revocation po stronie Clair

### 6. Token refresh — brak strategii proaktywnej

**Problem:** Plan mówi \"wykrywa expiry, automatycznie refreshuje przed wywołaniami API\" ale nie precyzuje:
- Czy refresh jest proaktywny (przed expiry, np. 5 min wcześniej) czy reaktywny (po 401)?
- Jaki jest timeout na refresh request?
- Co się dzieje jeśli refresh token też wygasł?

**Ryzyko:** Jeśli refresh jest tylko reaktywny, użytkownik zobaczy błąd 401 podczas pobierania guildów. Jeśli refresh token wygasł, flow powinien graceful fallback do \"Zaloguj ponownie\".

**Rekomendacja:** 
- Proaktywny refresh: jeśli \xpires_at < DateTime.UtcNow + 5 minutes\, odśwież token przed wywołaniem API
- Timeout: 15s na refresh request
- Jeśli refresh fails: wyczyść token, ustaw \IsLoggedIn = false\, pokaż komunikat \"Sesja wygasła — zaloguj ponownie\"

### 7. ALTER TABLE user_settings — brak IF NOT EXISTS w SQLite

**Problem:** Plan migracji (sekcja 12.1):
\\\sql
ALTER TABLE user_settings ADD COLUMN active_sustats_guild_id TEXT DEFAULT NULL;
\\\

SQLite **nie obsługuje** \IF NOT EXISTS\ dla \ALTER TABLE ADD COLUMN\. Jeśli kolumna już istnieje (np. z buggy first-run lub ręcznej modyfikacji), migracja rzuci błąd.

**Rekomendacja:** 
- Sprawdzić czy kolumna istnieje przed ALTER:
\\\sql
SELECT COUNT(*) FROM pragma_table_info('user_settings') WHERE name='active_sustats_guild_id';
\\\
- Lub użyć \CREATE TABLE IF NOT EXISTS\ pattern z rebuild tabeli (SQLite nie wspiera DROP COLUMN do wersji 3.35.0, ale Microsoft.Data.Sqlite 10.0.8 używa SQLite 3.45+).

### 8. OAuthLoopbackListener — brak cancellation i timeout

**Problem:** Plan (sekcja 7.3) definiuje \OAuthLoopbackListener\ ale nie ma:
- Mechanizmu cancellation (user zamyka przeglądarkę, anul flow)
- Timeoutu (listener nasłuchuje w nieskończoność jeśli code nie przyjdzie)
- Obsługi błędów (port zajęty, firewall blokuje)

**Rekomendacja:**
- Dodać \CancellationToken\ do \StartAsync\
- Timeout: 5 minut (standard OAuth)
- Jeśli port zajęty: spróbuj inny port (ale wtedy redirect URI musi być dynamiczny — patrz finding #1)
- Zwrócić wynik: \Task<OAuthCallbackResult>\ z sukcesem/błędem/cancel

### 9. Brak retry logic dla Clair API calls

**Problem:** Plan nie wspomina o retry logic dla HTTP calls do Clair API. Obecny \ModManager\ ma retry logic dla download failures. Clair API może zwrócić 502 (Discord API error) lub 503 (Redis unavailable).

**Rekomendacja:** Dodać retry z exponential backoff (max 3 próby) dla:
- \GET /api/susmodder/config\ (lekki, może 1 próba)
- \POST /api/susmodder/guilds\ (retry na 502/503)
- \POST /api/susmodder/credentials\ (NIE retry na 429 — rate limit)

---

## 🟡 UWAGI ŚREDNIE (ulepszenia, nie blokują)

### 10. i18n — placeholder format {0} vs {modName}

**Problem:** Plan (sekcja 10) używa \{0}\ w kluczach i18n:
- \DiscordAuth.LoggedInAs\: \"Zalogowano jako: {0}\" / \"Signed in as: {0}\"
- \DiscordAuth.LoginError\: \"Błąd logowania: {0}\" / \"Login error: {0}\"

Istniejący system lokalizacji używa \string.Format\ (potwierdzone w \LocalizationService.GetFormatted()\), więc \{0}\ jest poprawny. **ALE** konwencja i18n z dokumentacji preferuje nazwane placeholdery dla czytelności.

**Status:** ✅ \{0}\ jest kompatybilny z istniejącym systemem. Nie wymaga zmiany, ale warto dodać komentarz w kodzie że używamy \string.Format\ a nie ICU MessageFormat.

### 11. Brakujące klucze i18n

**Problem:** Plan (sekcja 10) definiuje 10 kluczy, ale brakuje:
- Komunikat o otwieraniu przeglądarki (jest \BrowserOpened\ ✅)
- Błąd \"port zajęty\" dla loopback listener
- Błąd \"brak połączenia z Clair API\"
- Potwierdzenie wyboru guildy (\"Połączono z serwerem: {0}\")
- Błąd rate limit (429) z Clair
- Błąd 403 \"brak uprawnień\"
- Tekst na stronie callback HTML (\"Możesz zamknąć to okno\" — też powinien być i18n?)

**Rekomendacja:** Dodać klucze:
| Klucz | PL | EN |
|---|---|---|
| \DiscordAuth.PortInUse\ | Port {0} jest zajęty. Spróbuj ponownie. | Port {0} is in use. Please try again. |
| \DiscordAuth.ApiUnavailable\ | Nie można połączyć się z serwerem Clair. | Cannot connect to Clair server. |
| \DiscordAuth.GuildSelected\ | Połączono z serwerem: {0} | Connected to server: {0} |
| \DiscordAuth.RateLimited\ | Zbyt wiele żądań. Spróbuj ponownie za godzinę. | Too many requests. Try again in an hour. |
| \DiscordAuth.NoPermission\ | Nie masz uprawnień na tym serwerze. | You don't have permission on this server. |

### 12. CredentialProtector — Linux AES-GCM nie jest MVP

**Problem:** Plan (sekcja 6.2) definiuje \CredentialProtector\ z DPAPI (Windows) i AES-GCM (Linux). Ale Linux nie jest wspierany w MVP (sekcja 13: \"Na Linux fallback AES-GCM (w przyszłości, gdy będzie wsparcie Linux)\").

**Rekomendacja:** W MVP zaimplementować tylko DPAPI. Na Linux rzucić \NotSupportedException\ z komunikatem \"Encryption not supported on Linux yet\". Dodać TODO dla przyszłej implementacji.

### 13. DI registration — nowe serwisy nie zarejestrowane w planie

**Problem:** Plan nie precyzuje jak nowe serwisy zostaną zarejestrowane w DI. Obecny pattern (App.axaml.cs):
\\\csharp
services.AddSingleton<IModRepository>(sp => new ModRepository(sp.GetRequiredService<DatabaseService>()));
\\\

**Rekomendacja:** Dodać do planu rejestrację:
\\\csharp
services.AddSingleton<IDiscordAuthRepository>(sp => new DiscordAuthRepository(sp.GetRequiredService<DatabaseService>()));
services.AddSingleton<ISustatsCredentialsRepository>(sp => new SustatsCredentialsRepository(sp.GetRequiredService<DatabaseService>()));
services.AddSingleton<IDiscordOAuthService>(sp => new DiscordOAuthService(...));
services.AddSingleton<IClairDiscordService>(sp => new ClairDiscordService(...));
\\\

### 14. sustats_credentials — brak powiązania z Discord user

**Problem:** Tabela \sustats_credentials\ przechowuje credentials per guild_id, ale nie ma powiązania z Discord user_id. Jeśli użytkownik zmieni konto Discord, credentials pozostaną w bazie.

**Ryzyko:** Niskie — credentials są ważne dopóki Clair nie unieważni tokenu. Ale warto dodać \discord_user_id\ do tabeli dla audytu i czyszczenia przy logout.

**Rekomendacja:** Dodać kolumnę \discord_user_id TEXT\ do \sustats_credentials\. Przy logout, wyczyść credentials dla aktualnego usera.

### 15. GameLaunch integration — nieokreślony moment przełączenia

**Problem:** Plan (sekcja 7.4) mówi że \MainWindowViewModel.GameLaunch.cs\ będzie odczytywać z \ISustatsCredentialsRepository.GetActiveAsync()\ zamiast \SUStatsConfigViewModel.GetSelectedServerData()\. Ale nie precyzuje:
- Czy stary kod zostanie usunięty czy zostanie jako fallback?
- Co jeśli \GetActiveAsync()\ zwróci null (user nie wybrał guildy)?

**Rekomendacja:** 
- Jeśli \GetActiveAsync()\ zwróci null → pokaż dialog \"Wybierz serwer SUStats\" lub uruchom grę bez SUStats (zależnie od decyzji właściciela)
- Stary kod usunąć w tej fazie (nie zostawiać dead code)

---

## 🟢 DROBNY POPRAWKI

### 16. appsettings.json — nazewnictwo kluczy

**Problem:** Plan dodaje \ClairApiBaseUrl\ i \SusmodderApiBaseUrl\. Istniejący klucz to \BaseUrl\ (https://susmodder.app/). Nowy \SusmodderApiBaseUrl\ jest redundantny.

**Rekomendacja:** Zachować istniejący \BaseUrl\ dla susmodder.app. Dodać tylko \ClairApiBaseUrl\ dla clairbot.app.

### 17. discord_auth — CHECK(id=1) ogranicza do jednego konta

**Problem:** Tabela \discord_auth\ ma \CHECK (id = 1)\ — singleton. To oznacza że tylko jedno konto Discord może być zalogowane naraz.

**Status:** ✅ To jest zamierzone (MVP, jedno konto). Warto dodać komentarz w kodzie że to ograniczenie może być usunięte w przyszłości.

### 18. Brak testów integracyjnych w planie

**Problem:** Plan (sekcja 14) definiuje testy manualne (checkboxy) ale nie wspomina o testach automatycznych.

**Rekomendacja:** Dodać:
- Test jednostkowy: \CredentialProtector.Protect/Unprotect\ round-trip
- Test jednostkowy: PKCE code_verifier → code_challenge
- Test integracyjny: OAuthLoopbackListener przechwytuje code z URL
- Test migracji: v1 → v2 tworzy tabele, nie psuje istniejących danych

---

## PODSUMOWANIE

| Kategoria | Liczba | Status |
|---|---|---|
| 🔴 Błędy krytyczne | 4 | Blokują implementację |
| 🟠 Błędy poważne | 5 | Wymagają wyjaśnienia |
| 🟡 Uwagi średnie | 6 | Ulepszenia |
| 🟢 Drobne poprawki | 3 | Kosmetyka |

### Werdykt: 🔴 PLAN WYMAGA POPRAWEK

Plan jest **dobrze przemyślany** pod względem architektury (Core/UI split, repository pattern, DI, i18n), ale ma **4 krytyczne luki** które muszą być rozwiązane przed implementacją:

1. **Port loopback** — musi być stały (53124) i zarejestrowany w Discord Developer Portal
2. **PKCE code_verifier** — wymaga precyzyjnej specyfikacji algorytmu
3. **SUStatsSessionManager cleanup** — plain-text secret musi być usunięty po migracji
4. **SUStatsService refactor** — musi być jasno określony impact na istniejące zależności

Po rozwiązaniu tych 4 problemów, plan jest gotowy do implementacji.

### Źródła użyte
- \mcp-rag\: SUStatsService, DatabaseService, DI patterns
- Pliki źródłowe: \DatabaseService.cs\, \ModRepository.cs\, \UserSettingsRepository.cs\, \SUStatsService.cs\, \SUStatsConfigViewModel.cs\, \MainWindowViewModel.GameLaunch.cs\, \SUStatsSessionManager.cs\, \App.axaml.cs\, \LocalizationService.cs\, \pl.json\, \n.json\, \AmongTokensResponse.cs\
- Discord OAuth2 PKCE spec: RFC 7636
- Microsoft.Data.Sqlite 10.0.8 documentation
