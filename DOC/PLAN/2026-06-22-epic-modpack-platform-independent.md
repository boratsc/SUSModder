# Plan: modpacki niezależne od platformy + instalacja na Epic

**Data:** 2026-06-22  
**Status:** Zaimplementowano — wymaga ręcznego QA na Epic i weryfikacji backendu dla wariantów Epic  
**Priorytet:** P0/P1 — ostatnia luka przed domknięciem modpacków  
**Zakres:** SUSModder 2.x/3.x desktop Avalonia + `SUSModder.Core`, backend-compatible z `susmodder.app` / API v2  
**Teza:** udostępniony modpack jest platform-independent. Paczka zapisuje tożsamość moda i wersję moda, a klient instalujący dobiera wariant pliku do swojej platformy (`steam` albo `epic`). Uruchamianie zainstalowanej instancji modpacka nie zmienia się względem zwykłego moda/instancji.

---

## 0. Status implementacji (2026-06-22)

| Faza | Opis | Status |
|------|------|--------|
| 0 | `ModDownloadUrlBuilder` respektuje przypiętą wersję modpacka przy wyborze wariantu platformowego. | ✅ |
| 1 | `EpicModInstaller` + `EpicFullModInstanceInstaller` + `PlatformFullModInstanceInstaller` — instalacja Epic do konkretnej ścieżki instancji. | ✅ |
| 2 | `ModPackInstaller` przekazuje platformę do instalatora; nowe kody błędów (download/SHA256/platform). | ✅ |
| 3 | Nowe klucze PL/EN dla błędów modpacków; mapowanie error code w UI; informacja o wariancie platformy w preview. | ✅ |
| 4 | Testy jednostkowe Core (218 passing) + build solution. | ✅ |
| 5 | "Suche" testy backendu: publiczne endpointy catalog + HEAD na URL-e downloadu Steam/Epic dla tej samej wersji moda. | ✅ |
| 6 | Test lokalny: ten sam pack code instalowany jako Steam i Epic tworzy dwie instancje z różnymi `Platform`. | ✅ |
| 7 | Ręczne QA na Epic z prawdziwym kontem, pack code i uruchomieniem gry. | ⏳ do wykonania |

---

## 1. Kontekst i obecny problem

Modpacki działają jako snapshot zestawu:

- full mod: `fullModId` + `fullModVersion`,
- DLL katalogowe: `dllModId` + `dllModVersion`,
- opcjonalne custom/external DLL,
- opcjonalny ToU config,
- opcjonalne `integration.dll`,
- lokalna instancja w `mod_instances` z `origin = shared_pack` i `sourcePackCode`.

Obecnie ścieżka instalacji modpacka idzie przez `ModPackInstaller.InstallPackAsync(pack, platform)` → `ModInstanceInstaller.InstallFullModInstanceAsync(...)` → `ModManagerFullModInstanceInstaller` → `ModManager.InstallFullModToPathAsync(...)`.

Luka: `ModManager.InstallFullModToPathAsync` obsługuje tylko `mode == "steam"` i dla Epic rzuca `NotSupportedException("Local instance installation currently supports Steam full mods only.")`. Zwykła instalacja Epic istnieje w `EpicVersionManager.ModifyEpicAsync`, ale jest starym flow katalogowym (`mods.InstallPath`) i nie umie instalować do konkretnej ścieżki instancji modpacka.

Kluczowa decyzja produktowa: **modpack nie może być zależny od platformy twórcy**. Jeżeli twórca stworzył pack na Steam, użytkownik Epic ma zainstalować ten sam `fullModId/fullModVersion`, ale pobrać wariant Epic dla swojej platformy. Analogicznie Steam odbiorca paczki stworzonej przez Epic ma pobrać wariant Steam.

---

## 2. Źródła i fakty z obecnego kodu

- `DOC/_archive/PLAN/completed/2026-05-29-mod-pack-sharing-plan.md` — modpack sharing to snapshot full moda + wersji + DLL; brak marketplace i brak edycji paczki po utworzeniu.
- `DOC/_archive/POC/completed/API v2/contracts/MODPACK_API.md` — kontrakt `POST /mod-packs` zapisuje `fullModId`, `fullModVersion`, `dllModId`, `dllModVersion`; nie ma pola platformy jako wymaganego składnika paczki.
- `SUSModder.Core/Models/ModPack.cs` — `ModPackFullMod` ma tylko `Id` i `Version`, `ModPackDllMod` ma `DllModId` i `DllModVersion`; model wspiera platform-independent identity.
- `SUSModder.Core/Services/InstanceToModPackMapper.cs` — mapper z lokalnej instancji zapisuje `BaseModId` i `FullModVersion`, nie platformę jako część requestu.
- `SUSModder.Core/Services/ModPackInstaller.cs` — już przyjmuje `platform` od UI i klonuje katalogowy mod do wersji z paczki (`CloneForInstall(match, fullMod.Version)`).
- `SUSModder/ViewModels/MainWindowViewModel.ModPacks.cs` — UI pobiera platformę z `UserSettings.Mode` i przekazuje ją do instalatora modpacka.
- `SUSModder.Core/Utilities/ModDownloadUrlBuilder.cs` — potrafi budować/pobierać URL według platformy (`/downloads/mod/{id}/{version}?platform=steam|epic`) oraz wybiera `EpicGitHubRepoOrLink` dla Epic przy direct URL custom/legacy.
- `SUSModder.Core/GameIntegration/EpicVersionManager.cs` — zwykłe Epic install flow pobiera URL przez `ModDownloadUrlBuilder.ResolveAsync(modConfig, "epic")`, rozpakowuje payload, zapisuje installation map z `Platform = "epic"`, ale docelową ścieżkę wylicza sam jako katalog moda, a nie przyjmuje ścieżki instancji.
- `SUSModder.Core.Tests/Services/ModPackInstallerInstallAsNewInstanceTests.cs` — testy modpacków pokrywają happy path tylko dla `"steam"`; brak testu Epic.

---

## 3. Goal

1. Użytkownik w trybie Epic może wprowadzić kod/link modpacka i zainstalować go jako lokalną instancję.
2. Ten sam kod modpacka działa na Steam i Epic — pack zapisuje `modId + modVersion`, a instalator dobiera wariant downloadu na podstawie platformy użytkownika instalującego.
3. Instalacja Epic modpacka tworzy pełnoprawną `mod_instance` tak samo jak Steam: `InstanceId`, `DisplayName`, `BaseModId`, `FullModVersion`, `AmongVersion`, `Platform = epic`, `InstallPath`, `Origin = shared_pack`, `SourcePackCode`.
4. DLL katalogowe, custom/external DLL, ToU config i `integration.dll` są nakładane na docelowy folder zainstalowanej instancji Epic tak samo jak dla Steam, z zachowaniem istniejących zabezpieczeń safe-path/SHA256/VT.
5. Uruchamianie modpacka pozostaje bez zmian — launcher korzysta z istniejącego `InstallPath` instancji/moda i nie dostaje osobnego trybu „modpack Epic”.

---

## 4. Non-goals

- Nie dodajemy platformy do publicznego modelu modpacka jako wymagania instalacji.
- Nie tworzymy osobnych kodów modpacków dla Steam/Epic.
- Nie zmieniamy kontraktu backendu w sposób breaking; ewentualne nowe pola tylko addytywne/debugowe.
- Nie refaktorujemy całego `EpicVersionManager` ani launchera Epic, jeżeli wystarczy wąska metoda instalacji do wskazanej ścieżki.
- Nie rozwiązujemy braków katalogowych na backendzie: jeśli dla danego moda/wersji nie ma wariantu Epic, UI ma pokazać czytelny błąd „wariant Epic niedostępny”, a nie zgadywać Steam payload.
- Nie zmieniamy polityki custom content: non-clean/pending nadal blokuje instalację.

---

## 5. Language / i18n impact

PL/EN copy wymagane dla wszystkich nowych komunikatów UI/progress/error:

- `ModPacks.PlatformVariantUnavailable` — brak wariantu moda dla platformy użytkownika.
- `ModPacks.InstallingForPlatform` — np. „Instalowanie wariantu {platform}…”.
- `ModPacks.EpicInstallFailed` — błąd instalacji Epic modpacka.
- `ModPacks.EpicPreparingInstance` — przygotowanie instancji Epic.
- Ewentualnie stabilne error code z Core:
  - `mod_pack_platform_variant_unavailable`,
  - `mod_pack_epic_install_failed`,
  - `mod_pack_full_mod_download_unavailable`,
  - `mod_instance_platform_not_supported`.

Zasady:

- PL i EN muszą mieć komplet kluczy.
- Placeholdery muszą być identyczne w obu językach (`{platform}`, `{modName}`, `{version}`).
- Brak liczników, więc ICU pluralization raczej nie dotyczy.
- Fallback locale pozostaje `pl`.
- Nowe komunikaty z Core powinny wracać jako stabilny kod + techniczny fallback, a UI mapuje na lokalizację.
- Nie wprowadzamy runtime translation downloads.
- Przyszła lokalizacja ma być możliwa przez dopisanie zasobów, bez zmiany logiki komponentów.

---

## 6. User workflow

### 6.1 Instalacja kodu/linku przez użytkownika Epic

1. Użytkownik ma w ustawieniach `Mode = epic` albo przeszedł wizard/wybór platformy jako Epic.
2. Otwiera link `susmodder://pack/{code}` albo wpisuje kod w UI.
3. UI pobiera pack z API i pokazuje preview bez rozróżniania „Steam pack”/„Epic pack”.
4. Po potwierdzeniu UI przekazuje `platform = epic` do `ModPackInstaller`.
5. Core:
   - rozwiązuje full mod po `fullModId + fullModVersion`,
   - dobiera download URL wariantu Epic,
   - instaluje full mod do nowej ścieżki instancji,
   - zapisuje `mod_instances.Platform = epic`,
   - instaluje DLL i dodatki do tej instancji.
6. UI pokazuje sukces/partial/failure tak jak dziś.
7. Użytkownik uruchamia instancję z „Moje zestawy” tak samo jak zwykły modpack — bez nowego flow.

### 6.2 Instalacja kodu/linku przez użytkownika Steam

Bez zmian funkcjonalnych, ale po refaktorze Steam ma korzystać z tej samej abstrakcji platform-aware. Pack stworzony przez Epic ma zadziałać na Steam, jeśli backend ma wariant Steam dla tej wersji moda.

### 6.3 Brak wariantu platformy

Jeśli backend/katalog nie ma payloadu dla `fullModId + fullModVersion + platform`:

1. Core nie próbuje instalować wariantu drugiej platformy.
2. Zwraca stabilny błąd `mod_pack_platform_variant_unavailable` z danymi diagnostycznymi (`modId`, `version`, `platform`).
3. UI pokazuje lokalizowany komunikat i sugeruje przełączenie platformy tylko jeśli użytkownik faktycznie posiada drugą platformę — nie robimy tego automatycznie.

---

## 7. Core business logic responsibilities

### 7.1 Rozdzielenie identity modpacka od wariantu platformy

- Zachować `ModPackFullMod { Id, Version }` jako źródło prawdy paczki.
- Nie zapisywać `platform` w `ModPackCreateRequest` jako semantyki paczki.
- Przy instalacji używać platformy lokalnego użytkownika (`UserSettings.Mode` → `InstallPackAsync(pack, platform)`).
- W `InstanceToModPackMapper` nie dodawać platformy do requestu; lokalna instancja może mieć `Platform`, ale pack pozostaje snapshotem moda/w wersji.

### 7.2 Platform-aware full mod instance installer

Wariant preferowany: rozszerzyć istniejącą abstrakcję `IFullModInstanceInstaller` bez zmiany publicznego flow UI.

Proponowane podejście:

1. Dodać w Core metodę instalacji Epic do wskazanej ścieżki, np.:
   - `EpicVersionManager.InstallFullModToPathAsync(ModConfiguration modConfig, string targetInstallPath, IProgressReporter progress, IDiagnosticsOutput log, ModManagerUserCallbacks callbacks, Action<string>? onSpeedUpdate = null)`
   - albo nowy serwis `EpicFullModInstanceInstaller : IFullModInstanceInstaller`.
2. Zmienić `ModManagerFullModInstanceInstaller.InstallAsync(...)` lub zastąpić go kompozytowym `PlatformFullModInstanceInstaller`, który:
   - dla `steam` używa obecnej ścieżki `ModManager.InstallFullModToPathAsync`,
   - dla `epic` używa nowej ścieżki Epic do konkretnego `targetInstallPath`,
   - dla innych wartości zwraca `mod_instance_platform_not_supported`.
3. Epic install-to-path powinien wykonać odpowiednik `EpicVersionManager.ModifyEpicAsync`, ale bez aktualizacji katalogowego `mods.InstallPath`:
   - `ModDownloadUrlBuilder.ResolveWithHashAsync(modConfig, "epic")`,
   - pobranie ZIP do temp,
   - weryfikacja SHA256, jeśli znany,
   - ekstrakcja przez istniejący extractor,
   - ustalenie `sourcePath` analogicznie do obecnego Epic flow,
   - skopiowanie zawartości do `targetInstallPath`,
   - zapis `.susmodder-install.json` v2 przez `ModInstanceInstaller.SaveInstallationMapV2Async` lub spójny helper,
   - brak `ConfigManager.SaveConfig` dla `mods.InstallPath`.
4. Upewnić się, że `targetInstallPath` po Epic instalacji zawiera `Among Us.exe` w miejscu oczekiwanym przez obecny launcher instancji.

### 7.3 Version/platform resolution

- `CloneForInstall(match, pack.FullMod.Version)` musi dalej pinować `ModVersion` do wersji z paczki.
- `ModDownloadUrlBuilder.ResolveWithHashAsync` musi respektować tę wersję, nie nadpisywać jej `CurrentVersion` przy szczegółach katalogu.
  - Do sprawdzenia w implementacji: obecny `ResolveWithHashAsync` przy `CatalogModDetailDto` wybiera wariant z `detail.Data.Variants`; trzeba potwierdzić, czy wariant jest dla żądanej wersji, a nie tylko `CurrentVersion`.
  - Jeśli endpoint detail nie rozróżnia wariantów po wersji, dla modpacków powinno się preferować bezpośrednie `BuildModDownloadUrl(mod.Id, mod.ModVersion, platform)` albo dodać query/endpoint detail dla konkretnej wersji.
- Jeśli API zwraca 404/410/unsupported dla platformy, propagować stabilny error code do UI.

### 7.4 DLL i dodatki

- `DllModificationServiceInstanceInstaller` już przyjmuje `platform`; upewnić się testami, że dla Epic wybiera `EpicGitHubRepoOrLink` / `/downloads/mod/{id}/{version}?platform=epic`.
- Custom/external DLL instalować bez zmian: safe path, SHA256, VT status, temp file + replace.
- ToU config i `integration.dll` stosować względem docelowego `InstallPath` instancji, nie katalogowego moda.

### 7.5 Legacy fallback

`InstallPackLegacyAsync` może pozostać ograniczony do starego flow, ale główny flow aplikacji powinien zawsze mieć `ModInstanceInstaller` z DI. Jeżeli legacy path zostaje:

- dla `platform = epic` nie aktualizować cicho katalogowego `mods.InstallPath` jako zamiennika instancji,
- zwracać lokalizowalny błąd „instalator instancji niedostępny” albo wymusić inicjalizację DI.

---

## 8. UI / Avalonia responsibilities

- Nie dodawać wyboru platformy do modpack preview jako właściwości paczki. Platforma instalacji = aktualne ustawienie użytkownika.
- W preview można dodać informację neutralną: „Zostanie zainstalowany wariant dla Twojej platformy: Steam/Epic”, z i18n PL/EN.
- `MainWindowViewModel.ModPacks.cs` już pobiera `settings.Mode`; zostawić ten punkt jako jedyne źródło platformy dla instalacji.
- Dla błędów z Core mapować error code na i18n, zamiast pokazywać surowy wyjątek.
- Progress dialog powinien używać istniejącego wzorca `UpdateProgressDialog`, ale nowe statusy nie mogą być hardcoded tylko po polsku, jeśli trafiają bezpośrednio do UI. Minimum: znormalizować komunikaty progress na klucze albo zaakceptować je jako techniczne tymczasowe tylko z taskiem follow-up.
- Po sukcesie odświeżyć `Mods` i `PackInstances` jak dziś; ustawić `ActiveBrowserTab = MyPacks` gdy `InstanceId` istnieje.

---

## 9. Config and migration implications

- Brak migracji backendowego formatu modpacka.
- Brak nowej tabeli wymaganej.
- Istniejące `mod_instances.Platform` powinno zostać ustawione na `epic` dla nowych instancji.
- `mods.InstallPath` nie powinno być aktualizowane podczas instalacji modpacka jako instancji.
- `.susmodder-install.json` v2 ma nadal pełnić rolę redundancji; upewnić się, że zapis dla Epic zawiera `Platform = epic`, `InstanceId`, `DisplayName`, `Origin = shared_pack`, `SourcePackCode`.
- `appsettings.json` pozostaje read-only; nie dodawać runtime writes.
- Nie zmieniać `user-settings` poza odczytem `Mode`.

---

## 10. Platform, packaging, updater, telemetry, privacy, AV constraints

### Platform

- Zakres runtime: Windows desktop, Steam/Epic.
- Epic wykorzystuje istniejące założenia `legendary.exe` / Epic integration tam, gdzie potrzebne do ownership/launch; sama instalacja modpacka nie powinna wymagać nowej autoryzacji, jeśli pobiera gotowy wariant moda z backendu/CDN.
- Nie kopiować Steam payloadu na Epic ani Epic payloadu na Steam.

### Packaging / updater

- Brak zmian w Velopack packaging.
- Nie dodawać nowych zewnętrznych binariów.
- Nie zmieniać ścieżek upload/release.

### Telemetry / privacy

- Nie dodawać nowej telemetrii bez decyzji produktowej; wcześniejszy plan odrzucił `pack_created/pack_installed` jako mało użyteczne.
- Logi diagnostyczne mogą zawierać `platform`, `modId`, `version`, `packCode` i error code; nie logować tokenów, prywatnych invite payloadów ani pełnych credentiali Epic/Steam.
- `creatorHash` pozostaje tylko w flow API modpacków.

### AV / bezpieczeństwo

- Zachować istniejące zabezpieczenia custom/external DLL: VirusTotal status, SHA256, safe path, temp write.
- Dla full mod Epic dodać SHA256 verification, jeśli `ModDownloadUrlBuilder.ResolveWithHashAsync` zwraca hash.
- Unikać nowych patternów self-modifying/executable download poza istniejącym flow mod payloadów, żeby nie pogarszać reputacji AV.

---

## 11. Backend compatibility (`susmodder.app`)

Kontrakt docelowy pozostaje kompatybilny:

- Pack payload nadal używa `fullModId/fullModVersion` i `dllModId/dllModVersion`.
- Download full moda/DLL odbywa się przez istniejący wzorzec `GET /downloads/mod/{id}/{version}?platform={steam|epic}&arch=x86` albo zgodny wrapper `ISUSModderApiClient.BuildModDownloadUrl`.
- Jeśli backend potrzebuje doprecyzowania wariantu po wersji, zmiana powinna być addytywna:
  - albo endpoint detail po wersji,
  - albo gwarancja, że `/downloads/mod/{id}/{version}?platform=epic` jest źródłem prawdy dla modpacków.
- Web fallback `https://susmodder.app/pack/{code}` nie musi znać platformy; platforma jest rozstrzygana dopiero w kliencie.

---

## 12. Verification plan

### 12.1 Unit tests Core

Dodać/rozszerzyć testy:

1. `ModPackInstallerInstallAsNewInstanceTests`:
   - `InstallPack_AsNewInstance_Epic_UsesEpicPlatformAndCreatesInstance`.
   - Pack: `FullMod { Id = 10, Version = "5.5.0" }`, platform `"epic"`.
   - Fake `IFullModInstanceInstaller` asercyjnie sprawdza, że dostał `platform == "epic"`, `modConfig.ModVersion == "5.5.0"`, a `targetInstallPath` jest nową instancją.
   - Sprawdzić `stored.Platform == "epic"`, `stored.Origin == "shared_pack"`, `stored.SourcePackCode == pack.PackCode`.
2. Test `PlatformFullModInstanceInstaller` / nowej klasy:
   - `steam` deleguje do Steam installer.
   - `epic` deleguje do Epic installer.
   - nieznana platforma zwraca stabilny błąd.
3. `ModDownloadUrlBuilderTests`:
   - dla katalogowego moda i wersji z modpacka URL zawiera tę wersję i `platform=epic`.
   - nie używa `CurrentVersion`, jeżeli `ModConfiguration.ModVersion` jest przypięty przez pack.
4. Test error handling:
   - brak wariantu Epic / 404 → `mod_pack_platform_variant_unavailable`.
5. Existing tests for safe DLL path/SHA256 muszą przechodzić bez zmian.

### 12.2 Integration/manual tests

1. Steam user instaluje pack stworzony na Steam — regresja.
2. Epic user instaluje ten sam pack — nowy happy path.
3. Steam user instaluje pack stworzony z instancji Epic — potwierdzenie platform-independent.
4. Pack z DLL katalogowym wybiera Epic DLL URL, jeśli dostępny.
5. Pack z custom/external DLL nadal wymaga clean VT i poprawnego SHA256.
6. Brak Epic wariantu full moda pokazuje lokalizowany błąd, bez częściowej śmieciowej instalacji.
7. Uruchamianie z „Moje zestawy” po instalacji Epic działa tak samo jak zwykły mod; brak osobnego przycisku/ścieżki.

### 12.3 Commands

```powershell
dotnet test SUSModder.Core.Tests\SUSModder.Core.Tests.csproj
dotnet build SUSModder.sln
```

Dodatkowo smoke manualny z realnym kodem modpacka i kontem Epic na maszynie testowej.

---

## 13. Suggested implementation order

### Faza 0 — potwierdzenie kontraktu i danych (mała, blokująca)

1. Zweryfikować na backendzie/API, czy `/downloads/mod/{id}/{version}?platform=epic` działa dla modów full używanych w modpackach.
2. Spisać listę modów/wariantów bez Epic payloadu — to są przypadki expected failure, nie bug klienta.
3. Potwierdzić, że `CatalogModDetailDto.Variants` nie nadpisuje przypiętej wersji paczki. Jeśli nadpisuje, zaplanować małą poprawkę `ModDownloadUrlBuilder` przed installerem.

### Faza 1 — Core: platform-aware installer

1. Dodać `Epic` install-to-target-path jako osobną małą klasę/metodę.
2. Dodać/zmienić kompozyt `IFullModInstanceInstaller`, żeby obsługiwał `steam` i `epic`.
3. Nie dotykać UI poza obsługą błędów.
4. Dodać unit tests dla delegacji platform i pinned version.

### Faza 2 — Core: ModPackInstaller hardening

1. Uporządkować error codes z `InstallPackAsNewInstanceAsync`.
2. Upewnić się, że `CloneForInstall` pinning wersji działa też dla DLL na Epic.
3. Dodać test Epic happy path z fake installerem.
4. Dodać test braku wariantu platformowego.

### Faza 3 — UI/i18n

1. Dodać PL/EN klucze dla nowych komunikatów.
2. Zmapować nowe error codes na lokalizowane komunikaty w flow modpack install.
3. Opcjonalnie dodać w preview krótką informację „wariant dla Twojej platformy”.
4. Sprawdzić placeholder parity PL/EN.

### Faza 4 — manual QA i regresja

1. Test Steam regresyjny.
2. Test Epic happy path.
3. Test cross-platform share: pack utworzony na Steam instalowany na Epic i odwrotnie.
4. Test custom/external DLL i ToU config na Epic.
5. Test launch z `MyPacks`.

---

## 14. Parallelizable tasks

Można robić równolegle po Fazie 0:

- **Core A:** `EpicFullModInstanceInstaller` / platform-aware delegacja.
- **Core B:** `ModDownloadUrlBuilder` pinned-version/platform tests i poprawka resolution.
- **UI/i18n:** dodanie kluczy PL/EN i mapowania error code, bez czekania na pełny installer.
- **QA:** przygotowanie dwóch realnych pack code i macierzy mod/version/platform na backendzie.

Nie robić równolegle bez synchronizacji:

- zmian w kontrakcie backendu i `ModPackCreateRequestSerializer`, bo pack ma pozostać platform-independent;
- zmian launchera, dopóki test nie udowodni, że obecny launcher nie potrafi uruchomić Epic instancji z `InstallPath`.

---

## 15. Open questions przed implementacją

1. Czy `CatalogModDetailDto.Variants` reprezentuje warianty najnowszej wersji, czy konkretnej wersji przekazanej w modpacku?
2. Czy Epic payload full moda zawiera `Among Us.exe` w root/oczekiwanej strukturze, czy wymaga folderu `AmongUs` jak obecny `EpicVersionManager.ModifyEpicAsync`?
3. Czy obecny launcher instancji używa `ModInstance.Platform` do Epic-specific import/repair w `legendary`, czy po prostu startuje `Among Us.exe` z `InstallPath`?
4. Jak UI ma pokazać przypadek „modpack installable, ale nie dla Twojej platformy” — jako blokadę w preview czy dopiero failure po kliknięciu install?
5. Czy backend dla DLL katalogowych zawsze ma oddzielny wariant Epic, czy część DLL jest platform-neutral i może używać tego samego pliku? Klient powinien nadal pytać platformowo, backend może zwrócić ten sam artefakt.

---

## 16. Definition of done

- Ten sam pack code instaluje się na Steam i Epic, jeśli backend ma warianty dla obu platform.
- Pack code nie zawiera i nie wymaga platformy twórcy.
- Nowe instancje Epic mają poprawne rekordy `mod_instances`, installation map v2 i działają w „Moje zestawy”.
- Brak wariantu platformy kończy się lokalizowanym, zrozumiałym błędem.
- PL/EN klucze są kompletne, placeholdery zgodne.
- `dotnet test SUSModder.Core.Tests\SUSModder.Core.Tests.csproj` i `dotnet build SUSModder.sln` przechodzą.
- Manualnie potwierdzono launch Epic modpacka bez osobnego flow.
