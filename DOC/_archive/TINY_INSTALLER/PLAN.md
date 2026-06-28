# Tiny Installer Plan

## Cel

- Zastapic publiczny, zmienny `Setup.exe` z Velopack stalym, malym bootstrapperem dla nowych uzytkownikow.
- Docelowy plik do pobrania ma byc podpisywany niezaleznie od zwyklych release'ow aplikacji.
- Bootstrapper ma dzialac bez .NET runtime i miescic sie w budzecie rozmiaru `<= 500 kB`.
- Po instalacji aplikacja ma dalej korzystac z obecnego flow auto-update opartego o Velopack.

## Uzgodnione decyzje

- Technologia bootstrappera: natywny `C++`.
- UI: zwykle okno Windows, bez CLI.
- Stack Windows: `Win32 API` + `WinHTTP` + `BCrypt`.
- Domyslny kanal instalacji: `release`.
- Kanal `beta`: ukryty pod `Opcje zaawansowane`.
- Bootstrapper budowany i podpisywany osobno od release'ow SUSModder.
- Publiczny link dla nowych uzytkownikow ma wskazywac na bootstrapper, nie na Velopack `Setup.exe`.

## Dlaczego nie .NET / nie Rust

- `.NET` wymusza kompromis: albo runtime po stronie usera, albo wiekszy plik self-contained.
- Celem jest model zblizony do malego downloadera jak w Firefoxie, wiec potrzebny jest natywny exe.
- `Rust` bylby mozliwy, ale przy GUI + HTTPS + checksum + logowaniu ryzyko przekroczenia `500 kB` jest wyraznie wieksze niz w `C++`.
- `C++ + Win32 + systemowe API` daje najlepsza szanse na maly, stabilny artefakt bez dodatkowych zaleznosci.

## Zakres odpowiedzialnosci bootstrappera

Bootstrapper ma robic tylko pierwsza instalacje i orkiestracje procesu:

1. Pokazac proste GUI instalatora.
2. Odczytac wybrany kanal (`release` domyslnie, `beta` w opcjach zaawansowanych).
3. Pobrac manifest release z backendu.
4. Wybrac najnowszy `full .nupkg` dla wskazanego kanalu.
5. Pobrac pakiet do katalogu tymczasowego.
6. Zweryfikowac `SHA256`.
7. Zainstalowac aplikacje w sposob zgodny z oczekiwaniami obecnego modelu Velopack.
8. Zapisac `updateChannel` do ustawien uzytkownika.
9. Utworzyc skroty / wpis uninstall, jesli bedzie to wymagane po naszej stronie.
10. Uruchomic `current\SUSModder.exe`.

Bootstrapper nie ma przejmowac odpowiedzialnosci za pozniejsze aktualizacje aplikacji. To zostaje w obecnym kodzie SUSModder.

## Wymagania funkcjonalne

- GUI, nie CLI.
- Jeden prosty ekran instalacji.
- Progress bar dla pobierania i instalacji.
- Czytelny tekst statusu.
- Obsluga bledow z przyciskiem `Ponow`.
- Prosty log diagnostyczny zapisywany do pliku.
- HTTPS only.
- Walidacja `SHA256` przed instalacja.
- Dzialanie bez .NET runtime.
- Maksymalny rozmiar publicznego bootstrappera: `500 kB`.

## Wymagania niefunkcjonalne

- Minimalny footprint i minimalna liczba zaleznosci.
- Brak ciezkich frameworkow UI.
- Brak WebView2, Qt, wxWidgets, MFC, .NET, Electron-like wrapperow.
- Kod i pipeline maja byc rozdzielone od zwyklych release'ow aplikacji.
- Podpis cyfrowy ma byc nakladany na bootstrapper tylko przy zmianach samego bootstrappera.

## Proponowana architektura projektu

Nowy projekt:

- `SUSModder.Bootstrapper`

Sugerowany podzial plikow:

- `main.cpp` - entrypoint i inicjalizacja aplikacji.
- `MainWindow.cpp/.h` - glowne okno i obsluga komunikatow Win32.
- `InstallController.cpp/.h` - orkiestracja flow instalacji.
- `ManifestClient.cpp/.h` - pobieranie i parsowanie manifestu backendowego.
- `Downloader.cpp/.h` - pobieranie z raportowaniem progresu.
- `HashVerifier.cpp/.h` - liczenie i porownanie `SHA-256`.
- `Installer.cpp/.h` - logika instalacji lokalnej.
- `SettingsWriter.cpp/.h` - zapis `updateChannel` i ewentualnych ustawien startowych.
- `Logger.cpp/.h` - prosty logger plikowy.
- `Win32Helpers.cpp/.h` - sciezki systemowe, shell, procesy, registry, skroty.
- `resources.rc` - ikona, version info, stringi, ewentualne dialog resources.

## Proponowany stack techniczny

- UI: czyste `Win32 API` + `Common Controls`.
- HTTP/HTTPS: `WinHTTP`.
- Hash: `BCrypt` (`SHA-256`).
- Procesy: `CreateProcess` / `ShellExecuteEx`.
- Pliki i katalogi: Win32 file APIs.
- Logowanie: prosty plik tekstowy, np. w `%LocalAppData%\SUSModderBootstrapper\logs\`.

## Proponowany UX

Jedno male okno z nastepujacymi elementami:

- Logo / nazwa `SUSModder`.
- Status, np. `Pobieranie pakietu...`.
- Progress bar.
- Przycisk `Anuluj`.
- Przy bledzie: `Ponow`.
- Link lub przycisk `Opcje zaawansowane`.

Sekcja `Opcje zaawansowane`:

- wybor kanalu `Release` / `Beta`.
- opcjonalnie w przyszlosci: niestandardowa sciezka instalacji.

Na start nie planujemy kreatora wieloekranowego.

## Kontrakt backendu

Na start bootstrapper ma korzystac z istniejacego API:

- `GET /api/releases?channel=release`
- `GET /api/releases?channel=beta`

Wymagane pola odpowiedzi:

- `success`
- `channel`
- `latestVersion`
- `downloadBaseUrl`
- `manifest.Releases[]`
- `manifest.Releases[].Version`
- `manifest.Releases[].File`
- `manifest.Releases[].SHA256`

Bootstrapper ma wybierac tylko pelny pakiet (`full .nupkg`), nie `delta`.

Przykladowy minimalny ksztalt odpowiedzi:

```json
{
  "success": true,
  "channel": "release",
  "latestVersion": "2.2.0",
  "downloadBaseUrl": "https://susmodder.app/releases/release",
  "manifest": {
    "LatestVersion": "2.2.0",
    "Releases": [
      {
        "Version": "2.2.0",
        "File": "SUSModder-2.2.0-release-full.nupkg",
        "SHA256": "..."
      }
    ]
  }
}
```

## Docelowy flow instalacji

1. Start GUI.
2. Wybor kanalu (`release` domyslnie).
3. `GET /api/releases?channel=...`.
4. Parsowanie manifestu.
5. Wybor najnowszego `full .nupkg`.
6. Download do `%TEMP%\SUSModderBootstrapper\`.
7. Weryfikacja `SHA256`.
8. Instalacja do katalogu docelowego.
9. Zapis `updateChannel` do ustawien usera.
10. Utworzenie skrotow / wpisu uninstall, jesli bedzie to potrzebne po naszej stronie.
11. Uruchomienie `current\SUSModder.exe`.

## Najwazniejszy spike techniczny

Przed wlasciwa implementacja trzeba potwierdzic jeden krytyczny punkt:

- jak wykonac pierwsza instalacje kompatybilna z Velopack bez uzywania publicznego, zmiennego `Setup.exe`

Cel spike'a:

- ustalic wymagany layout katalogow i plikow,
- ustalic, ktore elementy sa niezbedne, aby SUSModder uznal srodowisko za instalacje Velopack,
- sprawdzic, czy z lokalnego `full .nupkg` da sie bezpiecznie postawic install root zgodny z oczekiwaniami aplikacji.

Kryteria zaliczenia spike'a:

- aplikacja uruchamia sie z `current\SUSModder.exe`,
- srodowisko jest rozpoznawane jako zainstalowane,
- dalsze aktualizacje dzialaja juz obecnym mechanizmem aplikacji.

Bez zaliczenia tego spike'a nie nalezy rozpoczynac pelnej implementacji bootstrappera.

## Etapy wdrozenia

### Etap 0 - Spike techniczny

- Zweryfikowac swieza instalacje kompatybilna z Velopack z lokalnego `full .nupkg`.
- Spisac wynik i decyzje implementacyjne.

### Etap 1 - Szkielet projektu

- Utworzyc projekt `SUSModder.Bootstrapper`.
- Dodac podstawowe okno GUI.
- Dodac logger.
- Dodac podstawowe version info, ikone i zasoby.

### Etap 2 - Manifest i download

- Dodac klienta API.
- Dodac parsing JSON.
- Dodac wybor odpowiedniego `full .nupkg`.
- Dodac pobieranie z progresem.
- Dodac weryfikacje `SHA256`.

### Etap 3 - Instalacja lokalna

- Dodac tworzenie katalogow roboczych i docelowych.
- Dodac logike seedowania install root.
- Dodac zapis `updateChannel`.
- Dodac uruchamianie aplikacji po sukcesie.

### Etap 4 - UX i odpornosc

- Dodac `Ponow`.
- Dodac `Anuluj`.
- Dodac cleanup po bledach.
- Dodac lepsze komunikaty bledow.
- Dodac pelniejsze logowanie diagnostyczne.

### Etap 5 - Integracja z pipeline

- Dodac osobny skrypt build, np. `SKRYPTY/Build/build-bootstrapper.ps1`.
- Dodac osobny skrypt podpisywania bootstrappera.
- Dodac osobny deploy bootstrappera pod staly URL.
- Rozdzielic pipeline bootstrappera od zwyklych release'ow aplikacji.

### Etap 6 - Rollout

- Podmienic publiczny link pobierania dla nowych userow na bootstrapper.
- Zostawic obecny Velopack `Setup.exe` jako fallback techniczny na czas przejsciowy, jesli bedzie potrzebny.
- Nie zmieniac flow auto-update dla juz zainstalowanych klientow.

## Zmiany w build i deploy

Obecny pipeline aplikacji ma nadal produkowac:

- `.nupkg`
- manifesty release/beta
- ewentualnie `Setup.exe` jako fallback techniczny

Nowy pipeline bootstrappera ma produkowac:

- jeden maly, staly `SUSModder Installer.exe` albo podobnie nazwany artefakt
- podpis cyfrowy
- publikacje pod staly publiczny URL

Bootstrapper nie moze byc przebudowywany co release aplikacji tylko dlatego, ze zmienila sie wersja SUSModder.

## Testy akceptacyjne

- Czysta instalacja na Windows bez .NET runtime.
- Instalacja z kanalu `release`.
- Instalacja z kanalu `beta` przez `Opcje zaawansowane`.
- Poprawna walidacja `SHA256`.
- Poprawne zachowanie przy braku internetu.
- Poprawne zachowanie przy uszkodzonym pakiecie.
- Poprawne logowanie bledow do pliku.
- Poprawne uruchomienie aplikacji po instalacji.
- Poprawne dzialanie kolejnego auto-update po stronie SUSModder.

## Kryteria akceptacji projektu

- Publiczny installer ma rozmiar `<= 500 kB`.
- Publiczny installer nie wymaga .NET runtime.
- Publiczny installer nie zmienia sie przy zwyklych release'ach aplikacji.
- Nowy user instaluje SUSModder bez korzystania z per-release Velopack `Setup.exe`.
- Po instalacji aplikacja korzysta z obecnego systemu auto-update bez specjalnych wyjatkow.
- `Beta` jest wspierana, ale schowana pod `Opcje zaawansowane`.

## Glowne ryzyka

- Najwieksze: poprawna pierwsza instalacja kompatybilna z oczekiwaniami Velopack.
- Dotrzymanie limitu rozmiaru `500 kB`.
- Poprawna obsluga bledow sieciowych i niekompletnych instalacji.
- Zachowanie dobrego UX przy bardzo malym, natywnym installerze.
- Reputacja SmartScreen nadal zalezy od podpisu i historii pliku, ale tym razem budowana jest na jednym stalym bootstrapperze.

## Rekomendacja startowa

Startujemy od `Etap 0 - Spike techniczny`.

To jest najwazniejszy etap, bo zdejmie glowne ryzyko architektoniczne przed rozpoczeciem implementacji bootstrappera i zmian pipeline'u.
