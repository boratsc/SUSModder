# SUSModder 3.0 — changelog dla użytkowników

To największa aktualizacja SUSModdera od wersji 2.x. Zmienił się wygląd aplikacji, doszły modpacki, lepsza diagnostyka, tablica lobby, nowe integracje i dużo poprawek „pod maską”.

---

## Najważniejsze nowości

### Nowy wygląd aplikacji

SUSModder ma odświeżony interfejs 3.0:

- większe i czytelniejsze karty modów,
- wygodniejszy panel szczegółów po prawej stronie,
- osobne sekcje dla katalogu modów, dodatków DLL i zestawów,
- lepsze komunikaty o stanie instalacji, aktualizacji i problemach,
- nowy szklany motyw oraz poprawione istniejące motywy.

### Modpacki — zestawy modów

Możesz tworzyć i udostępniać zestawy modów.

Przykładowo: jedna osoba przygotowuje zestaw, a reszta ekipy wpisuje kod albo otwiera link i dostaje gotową konfigurację.

W praktyce oznacza to mniej tłumaczenia „co zainstalować” i mniej ręcznego klikania przed grą.

### Własne dodatki DLL i custom content

Modpacki obsługują także dodatkowe DLL i niestandardowe elementy z GitHuba.

SUSModder pokazuje status skanowania i bezpieczeństwa tam, gdzie jest dostępny, żeby łatwiej ocenić, czy plik jest gotowy do użycia.

### Tablica lobby

Doszła tablica lobby:

- możesz wrzucić kod swojego lobby,
- możesz znaleźć aktywne lobby innych graczy,
- przy wspieranych modach aplikacja potrafi automatycznie wykryć kod lobby z gry.

To ma skrócić drogę od „kto gra?” do faktycznego wejścia do lobby.

### Changelogi modów

Przy modach pojawiają się informacje o zmianach w nowych wersjach.

Nie trzeba już zgadywać, co zmieniło się po aktualizacji moda — SUSModder pokaże changelog tam, gdzie jest dostępny.

### Lepsza diagnostyka problemów z uruchamianiem

Jeśli mod nie startuje, aplikacja potrafi lepiej rozpoznać możliwe przyczyny:

- problemy z BepInEx,
- blokady antywirusa / Defendera / firewalla,
- problemy z plikami moda,
- wybrane problemy Epic/legendary.

Zamiast ogólnego „nie działa”, aplikacja powinna częściej pokazać konkretną wskazówkę, co sprawdzić.

### Discord OAuth / SUStats / Clair

Dodano nowe logowanie przez Discord dla integracji SUStats/Clair.

Dzięki temu aplikacja jest przygotowana pod dalsze funkcje społecznościowe i statystyczne.

---

## Ulepszenia codziennego używania

- Można wygodniej zarządzać większą liczbą modów.
- Dodano operacje masowe i kolejkę instalacji.
- Poprawiono stan offline i komunikaty przy problemach z API.
- Poprawiono aktualizacje modów i komunikaty po instalacji.
- Dodano tray / minimalizację do zasobnika.
- Poprawiono pobieranie i instalowanie wybranych wersji gry/modów.
- Poprawiono obsługę Steama i Epic Games.
- Dodano więcej tłumaczeń PL/EN.

---

## Co zmieniło się „pod maską”

Nie musisz tego konfigurować ręcznie, ale ważne dla stabilności:

- aplikacja korzysta z nowszego API v2,
- dane aplikacji są przechowywane w SQLite zamiast w luźnych plikach JSON,
- poprawiono cache i synchronizację katalogu modów,
- dodano dużo testów automatycznych,
- poprawiono bezpieczeństwo instalacji modpacków i dodatkowych plików.

---

## Znane ograniczenia w becie

- Buildy nie są podpisane certyfikatem, więc Windows SmartScreen lub antywirus mogą ostrzegać przy pierwszym uruchomieniu.
- Część funkcji supportu AI i automatycznych napraw jest jeszcze wyłączona albo zostawiona na później.
- Linux/Steam Deck/UMU nie są celem tej bety — główny target to Windows.
- Automatyczna wyszukiwarka publicznych lobby Among Us jest nadal osobnym tematem na później.

---

## Krótka wersja na Discorda / GitHub

**SUSModder 3.0 beta** jest gotowy do testów.

Największe zmiany:

- nowy interfejs 3.0,
- modpacki i udostępnianie zestawów kodem/linkiem,
- custom DLL / GitHub content w modpackach,
- tablica lobby i wykrywanie kodu lobby,
- changelogi modów w aplikacji,
- lepsza diagnostyka problemów z uruchamianiem,
- Discord OAuth / SUStats / Clair,
- SQLite zamiast starych plików JSON,
- API v2 i sporo poprawek stabilności.

To jest beta, więc możliwe są drobne problemy. Najważniejsze flow zostały sprawdzone: build, testy, API smoke i manualne E2E przechodzą bez blockerów.
