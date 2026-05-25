# 10 – Udostępnianie kodów do lobby (P2P mini-chat)

**Priorytet:** 🟢 P2
**Effort:** MVP ~1-2 dni, Nostr ~3-5 dni, auto-detect ~5-7 dni

## Cel

Umożliwić użytkownikom SUSModder dzielenie się kodami do lobby między sobą – bez centralnego serwera, na zasadzie sieci P2P.

## Dlaczego to potrzebne

- **Serwery Among Us mają bugi** – wyszukiwanie publicznych lobby często nie działa, kody nie są widoczne
- **Społeczność jest rozproszona** – gracze są na wielu serwerach Discord, nie ma jednego miejsca z kodami
- **Wielu graczy nie jest na żadnym serwerze na stałe** – potrzebują prostego sposobu na znalezienie/udostępnienie kodu bez dołączania do kolejnego Discorda
- **Obecny flow jest uciążliwy** – znalezienie kodu = otwórz Discord → znajdź serwer → znajdź kanał → skopiuj kod → wklej w grze. SUSModder może to zrobić w 2 kliknięcia.

## Założenia

- **Bez serwera** – komunikacja P2P, brak backendu do utrzymania (docelowo Nostr)
- **Per mod** – kody są przypisane do konkretnego moda (np. Town of Us, The Other Roles)
- **Tymczasowe** – kody lobby mają krótki TTL (np. 15 min), auto-wygasają
- **Lekkie** – nie bloatware, nie kolejny Discord

## Jak to działałoby z perspektywy usera

1. User instaluje/uruchamia mod (np. Town of Us)
2. W panelu moda pojawia się nowa sekcja: **"Lobby"**
3. Widzi listę kodów lobby udostępnionych przez innych graczy
4. Może ręcznie wkleić swój kod

`
+---------------------------------------------+
|  🎮 Lobby - Town of Us                      |
|  Udostępnione kody (ostatnie 15 min)        |
|                                             |
|  +-------------------------------------+   |
|  | 🔴 EU-MAIN  | 12/15 graczy          |   |
|  | Kod: ABCDEF | 2 min temu            |   |
|  +-------------------------------------+   |
|  +-------------------------------------+   |
|  | 🟢 NA-EAST  | 8/10 graczy           |   |
|  | Kod: XYZ123 | 5 min temu            |   |
|  +-------------------------------------+   |
|                                             |
|  [Twój kod: ______] [Udostępnij]            |
+---------------------------------------------+
`

## Protokół komunikacji – przegląd opcji

Szukamy czegoś lekkiego, chat-only, szyfrowanego, bez własnego serwera.

| Protokół | Opis | Waga | Serwer? | Szyfrowanie | Ocena |
|----------|------|------|---------|-------------|-------|
| **Nostr** | Prosty protokół pub/sub przez relaye | Bardzo lekki | Publiczne relaye | NIP-04 (opcjonalne) | ⭐ Najlepszy |
| **Matrix** | Zdecentralizowany chat (federacja) | Ciężki | Homeserver | E2E (Olm/Megolm) | ❌ Overkill |
| **WebRTC** | P2P data channels | Średni | STUN (publiczny) | DTLS (wbudowane) | ⚠️ Słaby .NET |
| **Tox** (toxcore) | P2P IM przez DHT | Średni | Brak | NaCl | ⚠️ Słaby binding |
| **libp2p** | Modularny P2P stack (IPFS) | Ciężki | DHT | Noise | ❌ Overkill |
| **MQTT** | Pub/sub, bardzo lekki | Mikro | Broker | TLS | ⚠️ Potrzebny broker |
| **Własne API** | susmodder.app/api/lobby-codes | Lekki | Nasz serwer | HTTPS | ✅ MVP |

### Rekomendacja: Nostr

**Dlaczego Nostr?**

- **Zero infrastruktury** – publiczne, darmowe relaye (nie trzeba nic hostować)
- **Bardzo lekki** – jeden JSON na wiadomość:
  {"kind":1, "content":"KOD:ABCDEF EU-MAIN 12/15", "tags":[["m","TownOfUs"],["v","5.1.2"]]}
- **Naturalny TTL** – relaye same czyszczą stare eventy
- **Prosty klient w C#** – NNostr.Client (NuGet) lub własna implementacja (~300 linii)
- **Per-mod "pokoje"** – przez tagi ["m", "TownOfUs"], subskrypcja z filtrem #m

**Flow z Nostr:**
1. User wkleja kod → apka tworzy Nostr event
2. Event wysyłany na publiczne relaye
3. Inni userzy subskrybują relaye z filtrem: #m = "TownOfUs"
4. Widzą kody w czasie rzeczywistym
5. Eventy starsze niż 15 min ignorowane (filtr po created_at)

### Strategia: API (MVP) → Nostr (V2)

1. **MVP:** Własne API (1-2 dni) – działa od razu, zero ryzyka, mamy backend
2. **V2:** Nostr (2-3 dni) – prawdziwy P2P, bez serwera, lepsza skala

## V3: Auto-wykrywanie kodu lobby (przyszłość)

Automatyczne udostępnianie kodu przy tworzeniu gry **wymaga integracji z modem** – osobny DLL wstrzykujący się w proces Among Us i komunikujący z SUSModder.

### Dlaczego to trudne

- Każdy mod ma inną strukturę wewnętrzną – nie ma uniwersalnego API
- Trzeba per-mod DLL-i które hookują tworzenie lobby
- Realne tylko dla modów z udokumentowanym API

Uwaga: DLL injection jako mechanizm **nie jest blokowany przez Windows Defender** w kontekście Among Us – BepInEx (używany przez SUSModder) już działa na tej samej zasadzie i nie powoduje false positives.

### Realne mody do integracji

| Mod | API | Status |
|-----|-----|--------|
| **Town of Us** | Mira API | Ma hooki do lobby – realne |
| **The Other Roles** | ? | Do sprawdzenia |
| Pozostałe | Brak / nieznane | Mało realne |

### Flow techniczny (Town of Us + Mira API)

`
Among Us + Town of Us + SUSModder.DLL
  +-> user tworzy lobby
  +-> Mira API hook wyzwala event
  +-> SUSModder.DLL wysyła kod do SUSModder (named pipe / localhost HTTP)
  +-> SUSModder publikuje na Nostr / API
`

**Effort:** ~5-7 dni na pierwszy mod (ToU + Mira), potem ~2-3 dni na każdy kolejny.

## Wyzwania

### 1. Bezpieczeństwo i moderacja

- Nostr: brak centralnej moderacji = ryzyko spamu
- Ograniczyć do samych kodów + region + player count, **bez dowolnych wiadomości tekstowych**
- Można dodać "zgłoś" → lokalny filtr per user

### 2. Auto-wygasanie

- Kody lobby mają sens tylko przez ~15 minut
- Nostr: filtrowanie po created_at po stronie klienta
- API: auto-cleanup na backendzie

### 3. Gdzie w UI?

Opcje:
- **W prawym panelu**, jako nowa sekcja przy szczegółach moda (rekomendowane)
- **W FAB menu**, jako nowa opcja "Kody lobby"
- **Osobne okno** – LobbyWindow

## Minimalna wersja (MVP)

1. Backend: POST /api/lobby-codes – wyślij kod, GET /api/lobby-codes?modId=X – pobierz aktywne
2. UI: w panelu moda, pod przyciskami Install/Launch, sekcja "Udostępnione lobby"
3. **User ręcznie wkleja kod** – MVP to tylko manualne udostępnianie
4. Auto-cleanup: kody starsze niż 15 min ignorowane

## Decyzje

- [ ] MVP przez API czy od razu Nostr?
- [ ] Tylko kody + metadane, czy też dowolne wiadomości?
- [ ] Auto-wygasanie: 10, 15 czy 30 minut?
- [ ] Gdzie w UI: prawy panel, status bar, FAB, osobne okno?
- [ ] Czy user może "zgłosić" nieodpowiedni kod?
- [ ] Auto-detect – czy w ogóle robić? Tylko dla ToU + Mira?
