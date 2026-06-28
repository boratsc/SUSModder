# Spike techniczny - pierwsza instalacja bez Velopack Setup.exe

## Cel

Potwierdzic, jak poprawnie wykonac pierwsza instalacje SUSModder z `full .nupkg`, bez korzystania z publicznego, zmiennego `Setup.exe`, ale z zachowaniem zgodnosci z obecnym flow Velopack po stronie aplikacji.

## Pytania, na ktore spike ma odpowiedziec

- Jaki dokladnie layout katalogow i plikow jest wymagany, aby aplikacja uznala srodowisko za instalacje Velopack?
- Czy instalacja moze byc seeded bezposrednio z lokalnego `full .nupkg`?
- Ktore pliki sa krytyczne dla pozniejszych aktualizacji?
- Czy wpis uninstall i skroty musi tworzyc bootstrapper, czy mozna to delegowac do istniejacego mechanizmu?
- Jak zapisac wybrany kanal tak, by pierwsze sprawdzenie aktualizacji dzialalo poprawnie?

## Checklist eksperymentow

### 1. Inspekcja instalacji wykonanej przez obecny Velopack Setup.exe

- Zainstalowac aktualna wersje przez obecny `Setup.exe`.
- Zmapowac pelna strukture katalogow po instalacji.
- Spisac wszystkie pliki dodawane poza `current\`.
- Sprawdzic lokalizacje skrotow i wpis uninstall.

### 2. Inspekcja `full .nupkg`

- Rozpakowac aktualny `full .nupkg`.
- Spisac, jakie pliki i metadane zawiera.
- Ustalic, co trzeba odtworzyc podczas pierwszej instalacji.

Obserwacje z pierwszego podejscia:

- aktualny `full .nupkg` ma payload aplikacji w `lib/app/`,
- zawiera `sq.version`,
- nie zawiera `Update.exe`,
- `SUSModder.exe` daje sie uruchomic bezposrednio z rozpakowanego `lib/app/`.
- layout `root/current` z plikami z `lib/app/` tez uruchamia `SUSModder.exe` nawet bez `Update.exe`.
- `Update.exe` wystepuje w `Portable.zip` w katalogu root, wiec mozna go publikowac osobno lub pozyskiwac z artefaktow Velopack.

### 3. Minimalny reczny seed install root

- Przygotowac testowy install root bez uzycia `Setup.exe`.
- Odtworzyc layout katalogow i plikow wymaganych przez aplikacje.
- Uruchomic `current\SUSModder.exe`.
- Sprawdzic, czy aplikacja wykrywa srodowisko jako zainstalowane przez Velopack.

### 4. Test pierwszego auto-update po seeded install

- Na seeded instalacji uruchomic check update.
- Zweryfikowac, czy aplikacja poprawnie pobiera manifest i planuje update.
- Zweryfikowac, czy `VelopackUpdateService.IsInstalledAsync()` zwraca oczekiwany wynik.

### 5. Kanal release/beta

- Dla seeded install zapisac `release`.
- Dla seeded install zapisac `beta`.
- Zweryfikowac, czy pierwsze sprawdzenie aktualizacji idzie do poprawnego kanalu.

## Kryteria zaliczenia spike'a

- Aplikacja startuje poprawnie z `current\SUSModder.exe`.
- Aplikacja rozpoznaje srodowisko jako instalacje Velopack.
- Co najmniej jedno kolejne sprawdzenie aktualizacji dziala poprawnie.
- Wiadomo, ktore elementy musi tworzyc bootstrapper podczas pierwszej instalacji.
- Wiadomo, czy potrzebny jest dodatkowy helper / embedded `Update.exe`, czy wystarczy seed katalogu + metadane.

## Artefakty po spike'u

- Krotka notatka z finalnym modelem instalacji.
- Lista plikow i katalogow wymaganych na starcie.
- Decyzja, co bootstrapper robi sam, a co deleguje.
- Decyzja, czy MVP bootstrappera mozna implementowac bez dalszych badan.
