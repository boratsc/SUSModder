# 11 – Wyszukiwarka lobby

**Priorytet:** 🟢 P2  
**Effort:** ~3-5 dni  
**Bazuje na:** `DOC\Lobby-searcher\PoC.md`

## Cel

Wyszukiwanie publicznych lobby bezpośrednio z serwerów Among Us – bez konieczności wklejania kodów przez innych graczy.

W przeciwieństwie do `10-lobby-code-sharing.md`, to jest **aktywne skanowanie** serwerów gry, a nie wymiana kodów między userami.

## Stan obecny

W `DOC\Lobby-searcher\` jest PoC (Python) który:
1. Odczytuje `region.json`
2. Łączy się z modowanym regionem
3. Pobiera listę publicznych lobby
4. Parsuje: kod, liczba graczy, mapa, host

PoC jest na etapie badawczym – sprawdza czy to w ogóle działa. **Nie ma jeszcze integracji z C# / UI.**

## Wyzwania

1. **Protokół Among Us nie jest publiczny** – trzeba reverse-engineering ruchu sieciowego
2. **Każdy region ma inny serwer** – trzeba obsłużyć wiele endpointów
3. **Modowane serwery mogą używać niestandardowych portów/protokołów**
4. **Identyfikacja moda** – trudno stwierdzić czy lobby używa Town of Us czy The Other Roles
5. **Rate limiting** – zbyt częste skanowanie może skutkować banem IP

## Integracja z SUSModder

Gdyby PoC się powiódł, integracja z aplikacją:

### UI – osobna zakładka / okno

```
┌──────────────────────────────────────────────┐
│  🔍 Wyszukiwarka lobby                  [✕] │
│                                              │
│  Mod: [Town of Us ▼]  Region: [EU ▼]        │
│  [Odśwież]                                   │
│                                              │
│  ┌──────────────────────────────────────┐   │
│  │ 🔴 EU-MAIN  │ 12/15  │ Skeld        │   │
│  │ ABCDEF      │ 2 min  │ ⭐ Dołącz     │   │
│  └──────────────────────────────────────┘   │
│  ┌──────────────────────────────────────┐   │
│  │ 🟢 NA-EAST  │ 8/10   │ Polus        │   │
│  │ XYZ123      │ 5 min  │ ⭐ Dołącz     │   │
│  └──────────────────────────────────────┘   │
└──────────────────────────────────────────────┘
```

### Gdzie w UI?

- **Osobne okno** – `LobbySearcherWindow` (rekomendowane – nie zaśmieca głównego)
- **FAB** – nowa opcja "Wyszukaj lobby"
- **Prawy panel** – jako dodatkowa sekcja (ale już jest przeładowany)

### Architektura w C#

```
SUSModder.Core/
├── Lobby/
│   ├── RegionLoader.cs        – odczyt region.json
│   ├── LobbyClient.cs         – połączenie z serwerem, request/response
│   ├── LobbyParser.cs         – parsowanie odpowiedzi
│   └── LobbyInfo.cs           – model danych

SUSModder/
├── Views/LobbySearcherWindow.axaml
└── ViewModels/LobbySearcherViewModel.cs
```

## Zależność od 10-lobby-code-sharing

Oba tematy się uzupełniają – można połączyć:
- Wyszukiwarka skanuje serwery (automatycznie)
- Code sharing pozwala userom dzielić się kodami (społecznościowo)
- W jednym widoku: zakładka "Znalezione" (skaner) + "Udostępnione" (P2P/API)

## Decyzje

- [ ] Kontynuować PoC w Pythonie czy od razu przepisać na C#?
- [ ] Czy to ma być osobne okno czy zintegrowane z main window?
- [ ] Czy łączyć z code sharing (jeden widok) czy trzymać osobno?
- [ ] Jak rozwiązać rate limiting / ryzyko bana?
- [ ] Czy próbować identyfikację moda (regex na nazwie lobby?)
