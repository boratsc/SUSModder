# SUSModder Bootstrapper

Natywny, maly bootstrapper Windows dla nowych instalacji SUSModder.

## Cel

- Staly publiczny plik do pobrania dla nowych uzytkownikow.
- Brak zaleznosci od .NET runtime.
- Docelowy rozmiar artefaktu: `<= 500 kB`.
- Pobieranie aktualnego `full .nupkg` i przygotowanie instalacji kompatybilnej z obecnym flow Velopack.

## Stan wdrozenia

Aktualnie projekt zawiera:

- szkielet natywnego GUI Win32,
- klient manifestu HTTP,
- downloader WinHTTP,
- walidacje SHA-256,
- wstepny seed install do czasu zakonczenia spike'a Velopack.

## Build

Do builda potrzebne sa narzedzia Visual Studio / Build Tools z komponentem C++.

Przyklad:

```powershell
.\SKRYPTY\Build\build-bootstrapper.ps1 -Configuration Release
```

## Ograniczenie

Finalna pierwsza instalacja zgodna z Velopack zalezy od wyniku dokumentu `DOC/TINY_INSTALLER/SPIKE_CHECKLIST.md`.
