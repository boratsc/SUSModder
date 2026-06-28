# Velopack Implementation Log

## 2025-11-03 – Krok 1: Analiza systemu aktualizacji
- Zapoznałem się z dokumentacją refaktoringu (`README.md`, `VELOPACK_IMPLEMENTATION.md`, `MIGRATION_PLAN.md`) oraz aktualnym kodem (`AppUpdateService`, `MainWindowViewModel.*`).
- Zidentyfikowałem obecną architekturę: pobieranie ZIP + zewnętrzny `Updater.exe`, mechanizm zapisu ustawień użytkownika oraz UI wyzwalający aktualizacje.
- Wyznaczyłem docelowy kierunek: wdrożenie Velopack w wersji pomostowej (legacy + Velopack) przed pełnym usunięciem starego updatera.
- Wstępny plan implementacji:
  1. Dodać referencje Velopack do projektu i przygotować `VelopackUpdateService` w Core.
  2. Rozszerzyć `Program.cs` o hooki Velopack i zapewnić kompatybilność z kopią ustawień.
  3. Rozszerzyć UI (`MainWindowViewModel`, `AppUpdateDialog`) o detekcję i obsługę obu mechanizmów.
  4. Przygotować proces build/publish (CLI `vpk`, pakiety, mostek 2.0.2).
  5. Stopniowo wygasić stary updater po przejściu użytkowników na Velopack.
- Kolejny krok: przygotować środowisko build (NuGet Velopack, narzędzia) i utworzyć szkielet nowego serwisu w `SUSModder.Core`.

## 2025-11-03 – Krok 2: Dodanie stabilnych referencji Velopack
- Zweryfikowałem oficjalne źródła (NuGet, GitHub releases). Najnowsza stabilna wersja biblioteki to `0.0.1298`; nowsze wydania (`0.0.13xx`) są oznaczone jako pre-release.
- Uzupełniłem referencje NuGet w `SUSModder.Core/SUSModder.Core.csproj` i `SUSModder/SUSModder.csproj`, wskazując wersję `0.0.1298` zamiast wcześniejszego placeholdera `0.0.1335`.
- Przygotowane projekty są gotowe do dalszych prac nad integracją API Velopack.

## 2025-11-03 – Krok 3: Nowy serwis `VelopackUpdateService`
- Utworzyłem `SUSModder.Core/Services/VelopackUpdateService.cs`, który enkapsuluje integrację z `Velopack.UpdateManager` (inicjalizacja, sprawdzanie, pobieranie i zastosowanie aktualizacji).
- Zaimplementowałem pomocnicze struktury wyników (`VelopackUpdateCheckResult`, `VelopackUpdateDownloadResult`) oraz metodę `IsInstalledAsync`, co umożliwi logice UI rozróżnienie środowiska Velopack oraz dwustopniowy proces `download → apply`.
- Kod rejestruje diagnostykę przez istniejący `IDiagnosticsOutput`, zachowując spójność z dotychczasowym systemem logowania.

## 2025-11-03 – Krok 4: Integracja Velopack w `Program.cs`
- Zaktualizowałem `SUSModder/Program.cs`, aby uruchamiać `VelopackApp.Build().Run()` przed startem Avalonii – to wymagane do obsługi hooków instalacyjnych/aktualizacyjnych.
- Przywracanie kopii ustawień użytkownika (`AppUpdateService.RestoreUserSettingsIfNeeded`) wykonuje się teraz tuż po hookach Velopack, zanim wystartuje UI, co zapewnia zgodność z trybem mostkowym.
- Dodany prosty `ConsoleDiagnosticsOutput` przekazuje diagnostykę procesu startowego do konsoli.

## 2025-11-03 – Krok 5: UI mostkowe (Velopack + legacy)
- Zmodyfikowałem `MainWindowViewModel.CheckForAppUpdatesOnStartup`, aby najpierw wykrywać instalacje Velopack (`VelopackUpdateService.IsInstalledAsync`), a następnie korzystać odpowiednio z nowej lub starej ścieżki aktualizacji.
- Dodałem okno `Views/VelopackUpdateDialog` z logiką pobierania i stosowania aktualizacji Velopack (progress bar, automatyczny restart). W przypadku braku instalacji Velopack nadal wyświetlany jest dotychczasowy dialog ZIP.
- Projekt (`SUSModder.csproj`) został uzupełniony o powiązanie kod-behind dla nowego dialogu.

## 2025-11-03 – Krok 6: Automatyzacja publikacji Velopack
- Przygotowałem skrypt `build-release-velopack.ps1`, który automatyzuje sekwencję build → publish → `vpk pack` (opcjonalnie z delta i podpisywaniem przez `VELOPACK_SIGN_TEMPLATE`).
- Skrypt czyści poprzednie artefakty, aktualizuje `CurrentVersion` w `SUSModder/appsettings.json` oraz generuje artefakty do katalogu `Releases/`, zgodnie z workflow opisanym w dokumentacji.
- Dodane komunikaty końcowe przypominają o wgraniu pakietów na endpoint `/releases` oraz o przeprowadzeniu smoke-testów bridge → Velopack.

## 2025-11-03 – Krok 7: Ręczne sprawdzanie aktualizacji w aplikacji
- Dodałem komendę `CheckForAppUpdatesCommand` w `MainWindowViewModel`, która współdzieli logikę z refaktoryzowanym `CheckForAppUpdatesOnStartup` (wydzielone metody `CheckForAppUpdatesCoreAsync`, ścieżki Velopack/legacy z diagnostyką i obsługą błędów).
- W menu szybkich akcji (FAB) pojawiła się nowa pozycja „Sprawdź aktualizacje aplikacji”, dzięki czemu użytkownik może ręcznie uruchomić dialog Velopack lub – w trybie mostkowym – legacy ZIP updater.
- Rozszerzyłem lokalizacje (`pl/en`) o etykietę menu oraz komunikaty o błędach podczas weryfikacji aktualizacji, by zachować spójny UX w obu językach.

## 2025-11-03 – Krok 8: Korekta adresu feedu Velopack
- Zmieniono `VelopackUpdateService.GetUpdateFeedUrl()` tak, aby korzystał z endpointu `/api/releases`, odzwierciedlając docelowe rozmieszczenie paczek na serwerze produkcyjnym.
- Dzięki temu logowanie diagnostyczne (`[Velopack] Initializing UpdateManager...`) wskazuje właściwy adres, a dalsze kroki integracji backendu pozostają spójne z planem migracji.

## 2025-11-04 – Krok 9: Klient Velopack zasilany API `/api/releases`
- Zaimplementowałem `VelopackApiSource`, który pobiera manifest z nowego endpointu (`GET /api/releases`), waliduje odpowiedź `success/error` i przekłada pole `manifest` na `VelopackAssetFeed`. Źródło obsługuje także `downloadBaseUrl`, aby prawidłowo rozwiązywać ścieżki paczek.
- `VelopackApiSource.DownloadReleaseEntry` streamuje pliki z wykorzystaniem `HttpClient`, raportuje progres i tworzy brakujące katalogi docelowe.
- `VelopackUpdateService` korzysta teraz z `VelopackApiSource` (zamiast `SimpleWebSource`), pilnując, aby feed URL był absolutny oraz dispose'ując źródło podczas zwalniania usługi.
