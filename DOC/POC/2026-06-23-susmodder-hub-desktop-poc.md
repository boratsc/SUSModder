# POC: SUSModder Hub w aplikacji desktopowej

**Data:** 2026-06-23  
**Status:** Wdrożone w desktopie — warianty A, B, C, D, E  
**Zakres:** aplikacja desktopowa SUSModder, bez zmian w bootstrapie/instalatorze  
**Link docelowy:** `https://discord.gg/YRcbKPj6VS`

## Status wdrożenia

- [x] **Wariant A** — pinned official card w panelu Discordów (`RecommendedDiscordsPanel.axaml`).
- [x] **Wariant B** — wejście menu zmienione na „Discord / społeczność” (`UI.Menu.RecommendedDiscords`).
- [x] **Wariant C** — status bar/FAB promo z priorytetowym wpisem SUSModder Hub i etykietą official.
- [x] **Wariant D** — kontekstowy CTA w widoku sukcesu po instalacji moda (`PostInstallSuccessView`).
- [x] **Wariant E** — sekcja „Pomoc i społeczność” w akcjach dodatkowych (`AdditionalActionsPanel`).
- [x] PL/EN lokalizacje dodane dla nowych tekstów.
- [x] Linki otwierane lokalnie przez `Process.Start(..., UseShellExecute = true)` bez WebView/OAuth.
- [x] Brak zmian w bootstrapie/instalatorze, SQLite i runtime `appsettings.json`.

---

## Kontekst

Powstał oficjalny serwer Discord **SUSModder Hub**. To inny byt niż istniejące **Discord Favs**:

- **Discord Favs** — polecane serwery Among Us, gdzie użytkownicy mogą znaleźć ludzi do gry.
- **SUSModder Hub** — oficjalny Discord aplikacji: support, feedback, modding chat, lobby i komunikacja wokół SUSModder.

Web landing jest poza zakresem tego POC — zostaje jak jest. Drobne linki do strony z kodami modpacków są osobnym, małym detalem. Ten dokument dotyczy tylko desktopa.

Dostępne assety desktopowe:

- `SUSModder/Assets/susmodder-hub-banner.png` — baner 768x307, ciemny Among Us/sci-fi background.
- `SUSModder/Assets/susmodder-hub-logo.png` — logo 512x512, circular badge.

Assety są objęte wpisem w `SUSModder/SUSModder.csproj`:

```xml
<AvaloniaResource Include="Assets\**" />
```

---

## Goal

Nakierować użytkowników desktopowej aplikacji na oficjalny Discord **SUSModder Hub** w sposób widoczny, ale nie nachalny, bez przebudowy głównego UI i bez mieszania oficjalnego serwera z dynamiczną listą polecanych Discordów.

---

## Non-goals

- Nie dotykamy bootstrapa/instalatora.
- Nie przepisujemy Discord Favs API.
- Nie dodajemy Discord OAuth ani logowania do samego Huba.
- Nie robimy popupu po starcie aplikacji.
- Nie dodajemy WebView ani zewnętrznego runtime.
- Nie zapisujemy nowych runtime settings do `appsettings.json`.
- Nie uzależniamy działania desktopa od pobrania zdalnych grafik.

---

## Obecny stan i punkty podpięcia

Istniejące miejsca związane z Discordem:

| Obszar | Pliki | Uwagi |
|---|---|---|
| Panel polecanych Discordów | `SUSModder/Views/RecommendedDiscordsPanel.axaml`, `SUSModder/ViewModels/RecommendedDiscordsViewModel.cs` | Najbardziej naturalne miejsce na official card. |
| Menu/FAB | `SUSModder/Views/MainWindow.axaml`, komenda `ShowRecommendedDiscordsCommand` | Obecny label: `UI.Menu.RecommendedDiscords`. |
| Rotacyjna promocja w status barze | `SUSModder/ViewModels/MainWindowViewModel.DiscordPromo.cs`, `MainWindow.axaml` | Obecnie bazuje na Discord Favs/preloaded servers. |
| Lokalizacja | `SUSModder/Localization/pl.json`, `SUSModder/Localization/en.json` | Nowe copy musi mieć PL/EN. |
| Otwieranie linków | `Process.Start(..., UseShellExecute = true)` w obecnych VM | Można użyć tego samego wzorca. |

---

## Wariant A — pinned official card w panelu Discordów

### Opis

W istniejącym panelu **Polecane Discordy** dodać na samej górze osobną kartę **SUSModder Hub**. Pod nią zostaje obecna lista Discord Favs.

Proponowany układ:

```text
[SUSModder Hub — official card]
  Official community / Oficjalna społeczność
  Wsparcie, lobby, wspólna gra i rozmowy o modach.
  [Support] [Lobby] [Modding chat]
  [Dołącz]

Polecane serwery do gry
[obecne Discord Favs]
```

### Gdzie podpiąć

- UI: `RecommendedDiscordsPanel.axaml`
- VM: `RecommendedDiscordsViewModel.cs`
- i18n: `pl.json`, `en.json`

### Jak nakierowuje ludzi

Użytkownik, który już szuka Discordów, najpierw widzi oficjalny Hub, a dopiero potem inne serwery. To najczytelniej rozdziela „oficjalny serwer aplikacji” od „serwerów do grania”.

### Zalety

- Najmniejsza rewolucja.
- Najmniej ryzyka regresji.
- Nie zaśmieca głównego ekranu.
- Dobrze wykorzystuje istniejący mental model: Discordy są w jednym panelu.
- Można użyć banera i logo jako mocny, brandowy element.

### Wady

- Widoczne dopiero po wejściu w menu Discordów.
- Jeżeli użytkownik nigdy nie otwiera tego panelu, może nie zobaczyć Huba.

### Ocena

**Rekomendowany wariant bazowy.** Najlepszy stosunek efektu do ryzyka.

---

## Wariant B — zmiana wejścia menu na „Discord / społeczność” + official micro-card w menu/FAB

### Opis

Zamiast traktować obecny przycisk menu jako „Polecane Discordy”, zmienić go na szerszą sekcję **Discord / społeczność**. Po najechaniu lub w samym menu/FAB można dodać mały wyróżnik „SUSModder Hub” albo podtytuł.

Przykład menu:

```text
Discord / społeczność
Oficjalny Hub + serwery do gry
```

Po kliknięciu otwiera się ten sam panel co w wariancie A, z official card na górze.

### Gdzie podpiąć

- `MainWindow.axaml` — item menu/FAB z `ShowRecommendedDiscordsCommand`.
- `Localization/pl.json`, `Localization/en.json` — zmiana `UI.Menu.RecommendedDiscords` albo dodanie nowego klucza.
- Opcjonalnie tooltip przy przycisku.

### Jak nakierowuje ludzi

Użytkownik widzi już w menu, że to nie jest tylko lista losowych/polecanych serwerów, ale sekcja społeczności aplikacji.

### Zalety

- Bardziej odkrywalne niż sam pinned card w panelu.
- Nadal bez dużej przebudowy.
- Porządkuje nazewnictwo pod przyszłe community features.

### Wady

- Wymaga delikatnej decyzji copy/UX: czy zmieniać istniejące „Polecane Discordy”.
- Nadal nie jest ekspozycją na głównym ekranie, tylko w menu.

### Ocena

**Dobry dodatek do wariantu A.** Samodzielnie za słaby, ale razem z pinned card daje lepszą nawigację.

---

## Wariant C — status bar / FAB promo z priorytetem dla Huba

### Opis

W istniejącej rotacyjnej promocji Discordów w status barze dodać **SUSModder Hub** jako specjalny, priorytetowy wpis. Może być wyświetlany:

1. zawsze jako pierwszy po starcie aplikacji,
2. co któryś cykl rotacji,
3. tylko gdy status bar jest w trybie DiscordPromo.

Wizualnie powinien wyglądać inaczej niż zwykły Discord Fav, np. z etykietą „Official”.

### Gdzie podpiąć

- `MainWindowViewModel.DiscordPromo.cs` — dodać statyczny/special promo item lub osobny model.
- `MainWindow.axaml` — drobne rozszerzenie widoku promo o etykietę official.
- `Localization/pl.json`, `Localization/en.json` — copy dla official promo.

### Jak nakierowuje ludzi

Hub pojawia się w pasywnym, stale widocznym obszarze aplikacji. Użytkownik może zauważyć go bez wejścia w panel Discordów.

### Zalety

- Większa widoczność niż wariant A.
- Wykorzystuje już istniejący mechanizm promocji.
- Nie wymaga nowego panelu ani nowej nawigacji.

### Wady

- Obecny mechanizm jest powiązany z Discord Favs, więc trzeba uważać, żeby nie wymieszać semantyki.
- Może być zbyt reklamowe, jeśli status bar często rotuje treści community.
- Większe ryzyko zmian w `MainWindowViewModel.DiscordPromo.cs` niż w samym panelu.

### Ocena

**Dobry wariant drugiej fazy**, jeśli pinned card nie daje wystarczającej widoczności. Nie robić jako pierwszy krok, chyba że priorytetem jest maksymalny ruch na Discord od razu.

---

## Wariant D — kontekstowy CTA po instalacji / aktualizacji moda

### Opis

Po udanej instalacji lub aktualizacji moda dodać subtelny CTA:

```text
Szukasz ludzi do gry albo potrzebujesz pomocy?
Dołącz do SUSModder Hub.
[Dołącz do Discorda]
```

To nie jest popup po starcie, tylko kontekstowy komunikat po akcji, która naturalnie prowadzi do potrzeby lobby/supportu.

### Gdzie podpiąć

Potencjalne miejsca:

- `PostInstallSuccessView` / `PostInstallSuccessViewModel` — jeśli obecny flow po instalacji moda ma dedykowany widok sukcesu.
- Panel modpacków / ekran kodów modpacków — mały link „SUSModder Hub” przy udostępnianiu lub odbieraniu paczki.
- Komunikaty po aktualizacji moda — tylko jeśli istnieje nieinwazyjny surface.

### Jak nakierowuje ludzi

CTA pojawia się wtedy, gdy użytkownik najpewniej chce zagrać, znaleźć lobby albo dopytać o problem z modem.

### Zalety

- Bardzo trafny kontekst.
- Nie zaśmieca głównej nawigacji.
- Może realnie zwiększyć dołączenia ludzi aktywnie instalujących mody.

### Wady

- Wymaga znalezienia/ujednolicenia kilku miejsc sukcesu po akcji.
- Łatwo przesadzić z liczbą CTA, jeśli pojawi się po każdej drobnej operacji.
- Mniej „brandowe” niż karta z banerem.

### Ocena

**Dobry wariant uzupełniający**, szczególnie pod ludzi szukających lobby po instalacji moda. Najlepiej jako mały link/card, nie pełny reklamowy blok.

---

## Wariant E — sekcja „Pomoc i społeczność” w ustawieniach / akcjach dodatkowych

### Opis

Dodać SUSModder Hub jako stały element w istniejących miejscach supportowych: ustawienia, akcje dodatkowe, diagnostyka/help. Przekaz: oficjalny kanał pomocy, feedbacku i rozmów.

Przykład:

```text
Pomoc i społeczność
- SUSModder Hub — oficjalny Discord aplikacji
- Zgłoś problem / diagnostyka
- GitHub / strona www
```

### Gdzie podpiąć

- `AppSettingsView` lub istniejące `AdditionalActionsPanel` — zależnie od tego, gdzie aktualnie są linki pomocnicze.
- Lokalizacja PL/EN.
- Ten sam mechanizm otwierania linków.

### Jak nakierowuje ludzi

Użytkownik szukający pomocy lub konfiguracji znajduje oficjalny Discord jako kanał kontaktu.

### Zalety

- Bardzo naturalne dla supportu.
- Małe ryzyko UX.
- Nie konkuruje z listą modów ani update flow.

### Wady

- Niska widoczność dla zwykłych użytkowników.
- Bardziej „support channel” niż „community/lobby”.

### Ocena

**Dobry fallback/support placement**, ale nie wystarczy jako główna ekspozycja.

---

## Porównanie wariantów

| Wariant | Widoczność | Ryzyko | Wysiłek | Najlepsze zastosowanie |
|---|---:|---:|---:|---|
| A. Pinned card w panelu Discordów | Średnia | Niskie | Niski/średni | Bazowa implementacja |
| B. Menu „Discord / społeczność” | Średnia | Niskie | Niski | Ulepszenie discoverability wariantu A |
| C. Status bar / FAB promo | Wysoka | Średnie | Średni | Druga faza, gdy potrzeba większej ekspozycji |
| D. CTA po instalacji/aktualizacji | Średnia/wysoka kontekstowo | Średnie | Średni | Nakierowanie aktywnych graczy na lobby/support |
| E. Pomoc i społeczność w ustawieniach | Niska/średnia | Niskie | Niski | Stały kanał supportu |

---

## Rekomendowana ścieżka

### MVP bez rewolucji

1. [x] **Wariant A** — pinned official card w panelu Discordów.
2. [x] **Wariant B** — zmiana nazwy wejścia na „Discord / społeczność” albo podobne copy.
3. [x] Link docelowy bezpośrednio przez `https://discord.gg/YRcbKPj6VS`.

### Faza 2

4. [x] **Wariant D** — mały CTA po udanej instalacji moda albo przy modpack codes.
5. [x] **Wariant E** — link w sekcji support/help.

### Faza 3 tylko jeśli potrzebna większa ekspozycja

6. [x] **Wariant C** — status bar/FAB promo z priorytetem dla Huba.

---

## Language / i18n impact

Nowe user-facing strings muszą być w `pl.json` i `en.json`. Fallback pozostaje PL.

Proponowane klucze:

```jsonc
"OfficialDiscordHub": {
  "Eyebrow": "Oficjalna społeczność",
  "Title": "SUSModder Hub",
  "Description": "Wsparcie, lobby, wspólna gra i rozmowy o modach.",
  "JoinButton": "Dołącz",
  "Tags": {
    "Support": "Support",
    "Lobby": "Lobby",
    "ModdingChat": "Modding chat"
  },
  "RecommendedServersHeading": "Polecane serwery do gry",
  "MenuLabel": "Discord / społeczność",
  "MenuSubtitle": "Oficjalny Hub i serwery do gry",
  "PostInstallCtaTitle": "Szukasz ludzi do gry?",
  "PostInstallCtaDescription": "Dołącz do SUSModder Hub po wsparcie, lobby i rozmowy o modach."
}
```

EN odpowiedniki:

```jsonc
"OfficialDiscordHub": {
  "Eyebrow": "Official community",
  "Title": "SUSModder Hub",
  "Description": "Support, lobbies, playing together and modding chat.",
  "JoinButton": "Join",
  "Tags": {
    "Support": "Support",
    "Lobby": "Lobby",
    "ModdingChat": "Modding chat"
  },
  "RecommendedServersHeading": "Recommended servers to play on",
  "MenuLabel": "Discord / community",
  "MenuSubtitle": "Official Hub and servers to play on",
  "PostInstallCtaTitle": "Looking for people to play with?",
  "PostInstallCtaDescription": "Join SUSModder Hub for support, lobbies and modding chat."
}
```

Uwagi:

- `SUSModder Hub`, `Discord`, `Among Us`, `Steam`, `Epic Games`, `BepInEx` zostają nazwami własnymi.
- Jeśli pojawią się liczniki użytkowników, użyć istniejącego wzorca lokalizacji albo docelowo ICU/pluralization.
- Nie hardcodować copy w AXAML/VM poza nazwami własnymi i danymi.

---

## Core business logic responsibilities

Preferowane minimum:

- Core bez zmian.
- UI otwiera stały link przyciskiem.
- Brak zapisu do SQLite i brak nowych tabel.

Opcjonalne ulepszenie:

- Dodać redirect `https://susmodder.app/discord` po stronie serwera/proxy, żeby przyszła zmiana invite nie wymagała update desktopa.

Nie zaleca się w MVP:

- osobnego endpointu API dla community links,
- dynamicznych grafik,
- logowania Discord,
- telemetrii osobowej.

---

## UI / Avalonia responsibilities

- Dodać osobny visual component albo sekcję w istniejącym `RecommendedDiscordsPanel.axaml`.
- Wykorzystać lokalne assety `avares://SUSModder/Assets/susmodder-hub-banner.png` i/lub `avares://SUSModder/Assets/susmodder-hub-logo.png`.
- CTA używa istniejącego wzorca `Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true })`.
- Zachować responsywność panelu — baner nie powinien wymuszać zbyt dużej wysokości przy mniejszych oknach.
- Oficjalny Hub nie powinien być elementem kolekcji `DiscordServers`, żeby nie sortował się razem z favsami po liczbie członków.

---

## Config and migration implications

- Brak migracji SQLite.
- Brak zmian runtime w `appsettings.json`.
- Brak wpływu na `user_settings`.
- Jeśli link ma być konfigurowalny, preferowany jest redirect web/proxy zamiast nowej konfiguracji klienta.

---

## Platform, packaging, updater, telemetry, privacy, AV constraints

- Windows desktop: zwykłe otwarcie linku w domyślnej przeglądarce.
- Velopack: tylko dodatkowe assety w paczce; brak zmiany updatera.
- AV/reputation: lokalne assety i brak WebView są bezpieczniejsze niż runtime web content.
- Privacy: klik w link nie wymaga Discord OAuth ani tokenów; aplikacja nie zbiera danych Discord.
- Telemetry opcjonalnie w przyszłości: tylko event typu `official_discord_join_clicked`, bez identyfikatorów Discord i bez treści użytkownika.
- Bootstrap/installer: poza zakresem, nie dotykać.

---

## Verification plan

### Statycznie

- Sprawdzić, że nowe stringi istnieją w PL i EN.
- Sprawdzić placeholder parity, jeśli pojawią się parametry.
- Sprawdzić, że nie ma hardcoded user-facing copy w nowych AXAML/VM.
- Sprawdzić, że assety ładują się przez `avares://SUSModder/Assets/...`.

### Build

```powershell
dotnet build SUSModder.sln
```

### Manual QA

- PL: wejście w menu `Discord / społeczność`, karta Huba widoczna, CTA działa.
- EN: analogicznie po przełączeniu języka.
- Offline/API error: official card nadal widoczny, Discord Favs mogą pokazać fallback/status jak dotychczas.
- Małe okno: baner/logo nie rozbija layoutu.
- Klik CTA: otwiera domyślną przeglądarkę i invite/redirect.

---

## Suggested implementation order

### Zadania równoległe

1. **UI/Avalonia:** przygotować official card w panelu Discordów.
2. **i18n:** dodać PL/EN klucze i ewentualnie zmienić label menu.
3. **Backend/web/proxy opcjonalnie:** dodać redirect `/discord`.

### Sekwencja MVP

1. Dodać i18n keys.
2. Dodać command/link w `RecommendedDiscordsViewModel` albo reuse istniejącej komendy z nowym parametrem.
3. Dodać official card w `RecommendedDiscordsPanel.axaml`.
4. Opcjonalnie zmienić label menu/FAB.
5. Build i manual QA.

### Sekwencja po MVP

6. Dodać małe CTA po sukcesie instalacji moda / przy modpack codes.
7. Dodać stały link w help/settings.
8. Dopiero potem rozważyć status bar/FAB promo.

---

## Decyzja proponowana

Na start wdrożyć **A + B**:

- pinned official card w panelu Discordów,
- zmiana wejścia menu na „Discord / społeczność”.

Jako późniejsze uzupełnienie dodać **D** przy flow instalacji/modpacków, bo to najbardziej naturalny moment na pytanie „gdzie znaleźć ludzi do gry?”. Status bar/FAB promo zostawić na koniec, jeśli po MVP ekspozycja okaże się za słaba.
