# PoC: wyszukiwarka publicznych lobby w zmodowanym Among Us

## Cel
Stworzyć narzędzie, które:
1. łączy się z modowanym regionem Among Us
2. pobiera listę publicznych lobby
3. wyciąga z niej:
   - nazwę / identyfikator lobby
   - kod lobby
   - liczbę graczy
   - region
   - ewentualnie mapę / ustawienia
4. próbuje określić, jaki mod działa na lobby

---

## Założenia
- `region.json` jest tylko punktem wejścia i źródłem endpointów regionów
- publiczne lobby nie są zapisane lokalnie w tym pliku
- lista lobby musi być pobierana dynamicznie z serwera regionu
- modowany region może używać innego flow niż vanilla
- identyfikacja moda może nie być dostępna bezpośrednio w liście lobby

---

## Zakres PoC v1
PoC ma odpowiedzieć tylko na pytanie:
"czy da się pobrać publiczne lobby z modowanego regionu i wyciągnąć z nich kod?"

Na tym etapie nie zakładamy:
- pełnego joinowania do lobby
- obsługi wszystkich modów
- stabilności produkcyjnej
- GUI
- filtrowania zaawansowanego
- wykrywania moda ze 100% skutecznością

---

## Architektura logiczna

### 1. Loader regionów
Moduł odpowiedzialny za:
- odczyt `region.json`
- wybór aktywnego regionu
- wyciągnięcie:
  - nazwy regionu
  - adresu serwera
  - portu
  - typu połączenia
- oznaczenie, czy region jest vanilla czy modded

Wejście:
- ścieżka do `region.json`

Wyjście:
- lista regionów gotowych do użycia przez klienta PoC

---

### 2. Warstwa transportu / klient protokołu
Moduł odpowiedzialny za:
- zestawienie połączenia z wybranym regionem
- odtworzenie minimalnego flow klienta Among Us potrzebnego do pobrania listy lobby
- wysłanie requestu typu "pobierz publiczne gry"
- odbiór odpowiedzi z listą lobby

Na etapie PoC ten moduł może działać w jednym z 2 trybów:

#### Tryb A: reverse live traffic
- najpierw analizujemy ruch prawdziwego klienta
- ustalamy dokładny request / endpoint / format payloadu
- PoC tylko odtwarza to 1:1

#### Tryb B: implementacja uproszczona
- próbujemy zbudować klienta na podstawie znanego starego flow
- testujemy, czy modowany region odpowiada zgodnie z oczekiwaniem

Do PoC sensowniejszy jest Tryb A, bo szybciej pokazuje, czy to w ogóle działa.

---

### 3. Parser odpowiedzi serwera
Moduł odpowiedzialny za:
- rozpakowanie odpowiedzi z serwera
- przeparsowanie listy lobby
- zamianę surowych rekordów na czytelny model danych

Minimalny model lobby:
- internal_game_id
- lobby_code
- host_name lub host_id jeśli dostępne
- player_count
- max_players
- map
- age / uptime
- region_name
- raw_payload

Dobrze zachować też:
- pełny surowy rekord odpowiedzi
żeby później analizować pola, których na początku nie rozumiemy

---

### 4. Konwerter Game ID -> Lobby Code
Osobny moduł odpowiedzialny za:
- przeliczenie identyfikatora gry na kod widoczny dla graczy
- obsługę obu wariantów kodowania, jeśli serwer używa różnych formatów
- walidację, czy wygenerowany kod wygląda poprawnie

Wejście:
- internal_game_id

Wyjście:
- kod lobby, np. 6-znakowy / 4-literowy / inny zgodny z aktualnym formatem

---

### 5. Mod detector
Najbardziej eksperymentalny moduł

Cel:
- spróbować określić, jaki mod działa na danym lobby

Możliwe strategie:

#### Strategia 1: heurystyka po regionie
- jeśli lobby pochodzi z `Modded EU`, oznacz jako "modded"
- bez wskazania konkretnego moda

Plus:
- łatwe
Minus:
- nie mówi jaki mod

#### Strategia 2: analiza metadanych z listy lobby
- sprawdzić, czy w odpowiedzi są dodatkowe pola
- np. custom tags, version markers, hidden flags, extra payload

Plus:
- nie wymaga joinowania
Minus:
- może nic tam nie być

#### Strategia 3: lekki join / handshake probe
- wejść częściowo w flow dołączania do lobby
- sprawdzić, czy serwer odsyła custom RPC / custom options / identyfikator moda

Plus:
- największa szansa na wykrycie moda
Minus:
- to już wykracza poza prosty listing i komplikuje PoC

#### Strategia 4: fingerprinting po zachowaniu
- porównywać zestawy opcji, custom roles, wersje, dodatkowe komunikaty
- na tej podstawie zgadywać:
  - Town of Us
  - TOHE
  - Las Monjas
  - inne

Plus:
- można dojść do dobrych wyników
Minus:
- to będzie heurystyka, nie pewnik

Na PoC v1 najlepiej przyjąć:
- wynik:
  - `modded: true/false`
  - `mod_name: unknown`
  - `confidence: low/medium/high`

---

## Przepływ działania PoC

### Scenariusz podstawowy
1. aplikacja czyta `region.json`
2. użytkownik wybiera region, np. `Modded EU (MEU)`
3. klient łączy się z endpointem regionu
4. wysyła request o listę publicznych lobby
5. odbiera odpowiedź
6. parser wyciąga rekordy lobby
7. każdy rekord przechodzi przez:
   - parser danych
   - konwerter ID -> code
   - mod detector
8. wynik jest zwracany jako lista lobby

---

## Przykładowy model danych wyjściowych

LobbyRecord
- region: "Modded EU (MEU)"
- server: "https://au-eu.duikbo.at"
- game_id_internal: ...
- lobby_code: ...
- host: ...
- players_current: 8
- players_max: 15
- map: ...
- age_seconds: ...
- modded: true
- mod_name: "unknown"
- detection_method: "region_only"
- raw_extra_fields: ...

---

## Jak sprawdzić wykonalność

### Faza 1: potwierdzenie listowania
Cel:
- ustalić, czy modowany region w ogóle oddaje publiczną listę lobby

Sukces:
- dostajemy jakąkolwiek listę gier

Porażka:
- region wymaga dodatkowego handshaku
- region blokuje custom klienta
- endpoint nie działa jak klasyczny matchmaking

---

### Faza 2: potwierdzenie dekodowania kodu lobby
Cel:
- sprawdzić, czy z odpowiedzi da się wyprowadzić prawidłowy kod

Sukces:
- kod zgadza się z kodem widocznym w kliencie albo po joinie

Porażka:
- format identyfikatora się zmienił
- potrzeba dodatkowego pola z odpowiedzi

---

### Faza 3: potwierdzenie wykrywania moda
Cel:
- sprawdzić, czy da się odróżnić konkretny mod od samego "modded"

Sukces minimalny:
- tylko `modded = true`

Sukces rozszerzony:
- np. `TOHE`, `Town of Us`, itp.

Porażka:
- brak metadanych w listingu
- mod możliwy do poznania dopiero po pełnym joinie

---

## Źródła prawdy dla PoC
W praktyce PoC powinien korzystać z 3 źródeł informacji naraz:

### 1. region.json
Do znalezienia endpointów i regionów

### 2. ruch sieciowy klienta
Do poznania:
- endpointów
- kolejności requestów
- formatów payloadów
- ewentualnych tokenów / nagłówków / wersji

### 3. dekompilacja moda / patcha
Do poznania:
- czy mod tylko dodaje region
- czy mod zmienia request listowania lobby
- czy mod dosyła własne metadane

To ostatnie jest bardzo ważne, bo może się okazać, że:
- vanilla klient pobiera listę jednym sposobem
- modowany klient innym
- albo filtruje wyniki już po stronie klienta

---

## Ryzyka

### 1. Nieaktualny protokół
Stare opisy protokołu mogą być częściowo niezgodne z aktualnym klientem

### 2. TLS / HTTPS / własny backend
Modowany region może używać własnej logiki po HTTP(S), a nie klasycznego flow

### 3. Anti-abuse / rate limit
Serwer może blokować zbyt częste pobieranie listy lobby

### 4. Brak informacji o modzie
Serwer może nigdy nie zwracać typu moda w liście publicznych gier

### 5. Zmiany między modami
Różne mody mogą używać innych convention i innych region backendów

---

## Minimalny sukces PoC
PoC uznajemy za udany, jeśli:
- z modowanego regionu da się pobrać listę publicznych lobby
- dla każdego lobby da się pokazać:
  - region
  - liczba graczy
  - kod lobby
- oraz przynajmniej oznaczyć:
  - modded / not modded

---

## Sukces rozszerzony
PoC uznajemy za bardzo udany, jeśli dodatkowo:
- da się wykrywać konkretny mod
- da się odświeżać listę cyklicznie
- da się filtrować lobby po:
  - mapie
  - liczbie graczy
  - typie moda
  - wersji

---

## Plan realizacji

### Etap 0
Zebrać próbki:
- `region.json`
- logi moda
- kilka sesji ruchu sieciowego przy otwieraniu public lobby

### Etap 1
Zidentyfikować dokładny request pobierania lobby

### Etap 2
Napisać minimalnego klienta, który potrafi tylko pobrać listę

### Etap 3
Dodać parser rekordów lobby

### Etap 4
Dodać dekoder kodów lobby

### Etap 5
Dodać podstawowe heurystyki wykrywania moda

### Etap 6
Porównać wynik z tym, co widzisz w prawdziwym kliencie gry

---

## Wersja v1 interfejsu
Na start wystarczy nawet bardzo prosty output tekstowy:

[MEU] ABCDEF | 8/15 | modded | mod: unknown
[MEU] QWERTY | 12/15 | modded | mod: unknown
[MEU] ZXCVBN | 4/15 | modded | mod: unknown

Dopiero potem:
- GUI
- wyszukiwarka
- sortowanie
- odświeżanie live

---

## Najważniejsza decyzja techniczna
Nie zaczynać od zgadywania protokołu w ciemno.

Najpierw ustalić:
- co dokładnie wysyła klient gry przy wejściu w public lobby na modowanym regionie
- czy mod podmienia tylko regiony czy także cały mechanizm matchmakingu

Bo od tego zależy, czy to będzie:
- prosty parser i requester
czy
- pełny mini-klient reverse-engineered pod konkretny mod/backend

---

## Realistyczny wniosek
Takie PoC ma sens i jest realne.

Najbardziej prawdopodobne:
- lista publicznych lobby: tak
- kod lobby: tak
- dokładny mod lobby: może tak, ale nie należy tego zakładać na starcie

Czyli najlepszy pierwszy cel:
"wyciągnąć publiczne lobby + ich kody z modowanego regionu"
a wykrywanie konkretnego moda potraktować jako bonusowy etap v2

---

## Ustalenia po pierwszym rozpoznaniu

Stan na podstawie:
- lokalnego `regionInfo.json`
- `Mini.RegionInstall`
- publicznych źródeł modów (`TOU-Mira`, `TOHE`, `Las Monjas`)
- bezpośrednich prób HTTP na `au-eu.duikbo.at` i `aumods.org`

### Co udało się potwierdzić
- `regionInfo.json` faktycznie zawiera modowane regiony jako `StaticHttpRegionInfo`
- `Mini.RegionInstall` tylko instaluje / podmienia wpisy regionów w kliencie, nie implementuje pobierania listy lobby
- `TOU-Mira` nie zawiera własnego klienta matchmakingu; korzysta z vanilla flow i ma tylko patche UX / join po kodzie
- `TOHE` potwierdza, że lista lobby zawiera co najmniej `TrueHostName` i `GameId`, bo patchuje `FindAGameManager.HandleList` i wyświetla `GameCode.IntToGameNameV2(container.gameListing.GameId)`
- `TOHE` patchuje też `InnerNetClient.HostGame` i dopisuje własny `Guid` do requestu hostowania, co sugeruje, że backend rozróżnia modowane lobby po dodatkowych danych w standardowym flow hostowania

### Najważniejsze obserwacje z endpointów
Bez autoryzacji te serwery nie zwracają listy lobby:

```text
GET  https://au-eu.duikbo.at/api/games           -> 401 Missing Authorization header
GET  https://au-eu.duikbo.at/api/games/filtered  -> 401 Missing Authorization header
POST https://au-eu.duikbo.at/api/user            -> 401 Missing Authorization header
GET  https://aumods.org/api/games/filtered       -> 401 Missing Authorization header
```

To nie jest już blocker nie do przejścia, tylko warunek wejścia do flow.
Po poprawnym odtworzeniu auth listing działa.

### Co z tego wynika
- samo dodanie regionów nie wystarczy do pobrania publicznych lobby
- backend tych serwerów używa HTTP matchmakera z obowiązkową autoryzacją
- klient najpierw zdobywa token regionu, a dopiero potem odpytuje listę lobby lub konkretne lobby
- publiczne repo modów nie pokazują całego tego flow, więc krytyczna logika siedzi częściowo w kliencie gry, a częściowo w custom backendzie regionu

### Co wiadomo o publicznych modach
- `Mini.RegionInstall`: tylko region injection
- `TOU-Mira`: join po znanym `GameId`, brak własnego listowania lobby
- `TOHE`: rozszerza UI listy lobby i hostowanie, ale nadal opiera się na istniejącym flow klienta gry
- `Las Monjas`: również opiera się na custom regionach, a nie na osobnym publicznym API listowania

### Wniosek techniczny
Najbardziej obiecujący kierunek nie brzmi już:
"zgadnąć endpoint listy lobby"

tylko:
"ustalić skąd klient bierze `Authorization` dla HTTP matchmakera i jakie requesty wykonuje po wejściu w public lobby"

### Następne kroki o najwyższej wartości
1. przechwycić ruch HTTPS prawdziwego klienta przy wejściu w `Find Game` na modowanym regionie
2. ustalić dokładny request do zdobycia tokenu / auth
3. odtworzyć ręcznie tylko minimalny flow:
   - auth
   - pobranie listy lobby
   - dekodowanie `GameId -> lobby code`
4. dopiero potem pisać właściwy klient PoC w Pythonie lub C#

---

## Ustalenia z dekompilacji klienta

Na lokalnej instalacji gry udało się potwierdzić istnienie pełnej warstwy HTTP matchmakingu po stronie klienta Among Us.

### Kluczowe klasy
- `AuthManager`
- `HttpMatchmakerManager`
- `FindAGameManager`
- `AmongUsClient`
- `ServerManager`
- `AmongUs.HTTP.RetryableWebRequest`

Źródła nazw z dekompilacji wskazują m.in. na pliki:
- `Assets/MatchMaking/HttpMatchmakerManager.cs`
- `Assets/MatchMaking/FindAGameManager.cs`
- `Assets/InnerNet/ServerManager.cs`
- `Assets/AuthManager.cs`

### Potwierdzony model flow
Najbardziej prawdopodobny flow klienta wygląda tak:

1. wejście do `Find Game` uruchamia `FindAGameManager`
2. `FindAGameManager` odświeża filtry i listę lobby
3. lista jest pobierana przez `HttpMatchmakerManager.CoRequestGameListFiltered(...)`
4. zanim request zostanie wysłany, klient woła `HttpMatchmakerManager.CoGetOrRefreshToken(...)`
5. to najpierw próbuje `TryReadCachedToken(...)`, a jeśli token jest nieprawidłowy / przeterminowany, robi `CoRefreshTokenInternal(...)`
6. odpowiedź trafia do `FindAGameManager.HandleList(...)`
7. join po kodzie idzie osobnym flow przez:
   - `AmongUsClient.CoFindGameInfoFromCode(...)`
   - albo `AmongUsClient.CoFindGameInfoFromCodeAndJoin(...)`
8. finalne połączenie do hosta schodzi przez `AmongUsClient.CoConnectToGameServer(...)`

### Endpointy potwierdzone w metadanych klienta
W lokalnych metadanych gry pojawiają się jawnie ścieżki:
- `api/user`
- `api/games`
- `api/games/filtered`
- `api/games?gameId=...`
- `api/filters`
- `api/filtertags`

To bardzo mocno potwierdza, że klient rzeczywiście korzysta z HTTP matchmakera, a nie z jakiegoś ukrytego alternatywnego protokołu do samego listowania.

### Potwierdzone modele danych
W warstwie matchmakingu występują m.in. takie obiekty:
- `UserTokenRequestData { Puid, Username, ClientVersion, Language }`
- `FindGameByCodeResponse { Errors, Game, Region, UntranslatedRegion }`
- `FindGamesListFilteredResponse { Games, Metadata }`
- `GamesListMetadata { AllGamesCount, MatchingGamesCount }`
- `HostServer { Ip, Port }`
- `MatchmakerToken`
- `MatchmakerTokenPayload { Puid, ClientVersion, ExpiresAt }`

### Co to mówi o auth
- token matchmakingu jest osobnym bytem od samego loginu konta
- klient trzyma go jako:
  - `token`
  - `base64Token`
- token ma payload z co najmniej:
  - `Puid`
  - `ClientVersion`
  - `ExpiresAt`
- istnieje `TryParse(...)` i `IsValid`, więc klient lokalnie waliduje ten token
- istnieje też logika retry przy auth failure, co sugeruje automatyczny refresh tokenu

### Najważniejszy nowy wniosek
PoC nie wymaga zgadywania "czy jest API" - to już wiemy.

Prawdziwe pytanie brzmi teraz:
"jak dokładnie wygląda request do `api/user`, jakie nagłówki idą w auth i z czego klient buduje matchmaker token dla danego regionu"

### Co nadal nieudowodnione
- dokładna metoda HTTP i pełny body / query dla każdego endpointu
- dokładny format nagłówka auth przy `api/user`
- czy token jest persystowany na dysku czy tylko trzymany w RAM
- czy custom serwery `duikbo` / `aumods` używają dokładnie tego samego formatu auth co vanilla, czy tylko bardzo podobnego

### Wniosek praktyczny
To był krok naprzód i został już domknięty w praktyce:
- listing lobby jest osiągalny
- wymaga poprawnego odtworzenia flow auth
- MITM / trace prawdziwego klienta potwierdził pełny minimalny flow HTTP

### Potwierdzone w praktyce na `au-eu.duikbo.at`
Flow odtworzony na prawdziwym kliencie moda:

1. `POST /api/user`
   - `Authorization: Bearer <globalny idToken klienta>`
   - body JSON:
   ```json
   {"Puid":"...","Username":"...","ClientVersion":50652900,"Language":0}
   ```
   - response: plain-text token regionu

2. `GET /api/games/{gameId}`
   - `Authorization: Bearer <token regionu>`
   - `Client-Mods: 1;2;auavengers.tou.mira=1.5.9;mira.api=0.3.9`
   - response: pełne dane konkretnego lobby (`HostName`, `TrueHostName`, `PlayerCount`, `IP`, `Port`, `Options`, itd.)

3. `GET /api/games/filtered?filter=...`
   - `Authorization: Bearer <token regionu>`
   - `Client-Mods: ...`
   - response: lista lobby + metadata (`allGamesCount`, `matchingGamesCount`)

4. `PUT /api/games`
   - create game / hostowanie
   - response zwraca tylko `{ "Ip": ..., "Port": ... }`
   - `gameId` / kod lobby nie wraca w tym HTTP response, więc jego nadanie dzieje się później w protokole gry lub po stronie backendu

### Stan obecny PoC
Lokalny skrypt `DOC/Lobby-searcher/lookup_lobby.py` obsługuje już dwa tryby:
- `lookup` - lookup konkretnego kodu lobby po `gameId`
- `list` - pobranie listy lobby przez `games/filtered`

To oznacza, że rdzeń wyszukiwarki lobby dla modded regionów został praktycznie potwierdzony.

---

## Aktualny stan po MITM i Wiresharku

### Co jest już potwierdzone praktycznie
- da się pobrać listę publicznych lobby z modowanego regionu przez `GET /api/games/filtered?filter=...`
- da się pobrać szczegóły konkretnego lobby przez `GET /api/games/{gameId}`
- da się przeliczyć `GameId <-> lobby code`
- da się odzyskać kod lobby z odpowiedzi `games/filtered`
- region stosuje rate limit (`429 Too Many Requests`), więc klient musi mieć retry / backoff

### Jak wygląda minimalny pipeline
1. globalny `idToken` klienta (`Authorization: Bearer <...>`)
2. `POST /api/user` do regionu
3. response: plain-text token regionu
4. `GET /api/games/filtered?filter=...` albo `GET /api/games/{gameId}` z tokenem regionu
5. opcjonalnie `PUT /api/games` do hostowania

### Jakie dane są już osiągalne bez joinowania
Z samego HTTP listingu / lookupu można wyciągnąć:
- `GameId`
- `lobby_code`
- `HostName` / `TrueHostName`
- `PlayerCount`
- `MaxPlayers`
- `NumImpostors`
- `MapId`
- `Language`
- `IP`
- `Port`
- `Age`
- `Options`
- `metadata.allGamesCount`
- `metadata.matchingGamesCount`

### Co nadal nie przychodzi wprost z HTTP
W `games/filtered` i `games/{gameId}` nie ma jawnego pola:
- `mod`
- `modVersion`

Czyli sama warstwa HTTP rozwiązuje listing lobby, ale nie pełną identyfikację moda.

### Detekcja moda: co udało się potwierdzić dla Miry
Po wejściu / handshake UDP do lobby Miry w payloadzie pojawiają się jawnie identyfikatory pluginów i wersje.

Przykładowe sygnatury wykryte w pakietach UDP:
- `gg.reactor.api` `2.5.0`
- `at.duikbo.regioninstall` `1.2.0`
- `auavengers.tou.mira` `1.5.9`
- `Town of Us Mira`
- `mira.api` `0.3.9`
- `MiraAPI`

To oznacza, że lobby Miry da się wykryć pewnie po handshake UDP, nawet jeśli HTTP listing nie zwraca nazwy moda.

### Co to oznacza architektonicznie
Docelowe rozwiązanie powinno mieć 2 warstwy:

#### Warstwa 1: HTTP enumerator
Cel:
- pobrać listę lobby per region
- odzyskać `GameId`, `code`, hosta, graczy, mapę, opcje

#### Warstwa 2: UDP classifier
Cel:
- dla wybranego lobby wykonać krótki probe / join-handshake
- odczytać listę pluginów i wersji
- sklasyfikować lobby jako np. `Mira`, `TOHE`, `unknown`

### Aktualny status celu PoC
Cel minimalny został już osiągnięty:
- pobranie listy publicznych lobby z modowanego regionu - tak
- wyciągnięcie kodu lobby - tak
- wyciągnięcie podstawowych danych lobby - tak

Cel rozszerzony jest częściowo osiągnięty:
- wykrycie konkretnego moda - tak, ale na razie potwierdzone praktycznie dla Miry przez UDP handshake
- wykrycie wersji moda - tak, jeśli handshake zawiera plugin GUID i version string

### Najbardziej sensowne dalsze kroki
1. utrwalić warstwę HTTP w stabilnym kliencie z retry / backoff
2. złapać i opisać sygnatury handshake dla Syzyfowy / TOHE
3. zbudować klasyfikator `mod -> version -> confidence`
4. dopiero potem integrować to w `SUSModder.Core`

---

## Alternatywa: odpytanie po kodach lobby

Jeśli listing publicznych gier po stronie serwera jest martwy, ograniczony albo niestabilny,
to istnieje druga droga:
- nie pytać o listę gier
- tylko pytać o konkretne kody / `gameId`

### Dlaczego ten pomysł ma sens
- klient gry ma osobny flow `CoFindGameInfoFromCode(...)`
- istnieje endpoint lookup po konkretnym lobby (`api/games?gameId=...` lub równoważny flow po kodzie)
- nie musimy polegać na tym, że `api/games/filtered` działa poprawnie

### Główny problem
Przestrzeń kodów jest skończona, ale nadal duża.

Przy założeniu alfabetu 26 znaków dla 6-literowych kodów:
- `26^6 = 308,915,776` możliwych kodów

To oznacza orientacyjnie:
- przy `10 req/s` -> ok. `357.5` dnia pełnego skanu
- przy `100 req/s` -> ok. `35.8` dnia
- przy `1000 req/s` -> ok. `3.6` dnia

W praktyce dochodzą jeszcze:
- auth
- rate limiting
- bany / anti-abuse
- duplikaty i błędy sieciowe

Czyli pełny brute-force wszystkich kodów produkcyjnego serwera raczej nie jest realistyczny.

### Co może być realistyczne
Nie pełny brute-force, tylko wersja zawężona:
- skan tylko jednego regionu
- skan tylko jednego wariantu kodowania
- skan tylko wybranych zakresów `gameId`, jeśli okaże się, że są lokalne czasowo / sekwencyjne
- skan przyrostowy i cache wyników
- wykorzystanie już znanych kodów z Discorda / hostów / logów jako punktów startowych

### Najbardziej obiecująca wersja tego pomysłu
Najpierw ustalić:
- czy lookup po kodzie wymaga tego samego tokenu matchmakera co listing
- czy `gameId` na backendzie są rozłożone losowo czy jednak rosną / grupują się w czasie

Jeżeli `gameId` są w praktyce lokalne czasowo albo przewidywalne, wtedy:
- nie skanujemy całego `26^6`
- tylko niewielkie aktywne okno kandydatów

To mogłoby już być wykonalne.

### Wniosek
Pomysł "kod po kodzie" nadal ma sens jako fallback albo drugi tryb pracy.

Na obecnym etapie nie jest to już główny kierunek, bo `games/filtered` został potwierdzony jako działający po poprawnym auth.

Ale trzeba go traktować nie jako pełny brute-force całej przestrzeni,
tylko jako:
- lookup po kodzie
- zawężony heurystykami
- najlepiej po wcześniejszym poznaniu charakteru `gameId`
