# Plan: changelogi per mod w kliencie SUSModder

**Data:** 2026-06-11  
**Status:** Plan do wdrożenia  
**Priorytet:** P1  
**Powiązany kontrakt:** `DOC/POC/API v2/contracts/CHANGELOG_API.md`  
**Endpoint produkcyjny:** `GET https://api.susmodder-cdn.ovh/v2/catalog/:id/changelog?lang=pl|en&limit=1..20`

---

## 1. Kontekst

W `susmodder-api` został wdrożony publiczny serwis changelogów per mod. Dane są prefetchowane przez backend z GitHub releases i opcjonalnie tłumaczone z EN na PL. Klient desktopowy ma pokazywać changelog w przepływach instalacji, aktualizacji oraz w panelu szczegółów moda.

Aktualny klient ma już centralną bramę API v2:

- `SUSModder.Core/Api/ISUSModderApiClient.cs`
- `SUSModder.Core/Api/SUSModderApiClient.cs`
- `SUSModder.Core/Api/Models/*`
- `SUSModder.Core/Services/*`

Istniejący `ChangelogService` i `ChangelogDialog` dotyczą changeloga aplikacji SUSModder po aktualizacji Velopack/GitHub. Nie należy ich bezpośrednio mieszać z changelogami modów.

---

## 2. Wyniki smoke testów API

Testowane 2026-06-11 z klienta lokalnego.

| Test | Wynik |
|---|---|
| `GET https://susmodder.app/api/v2/catalog/1/changelog?lang=pl&limit=3` | `200 OK`, PL body, `ETag`, `Cache-Control`, `X-Cache` |
| `GET https://susmodder.app/api/v2/catalog/1/changelog?lang=en&limit=2` | `200 OK`, EN body |
| `GET https://api.susmodder-cdn.ovh/v2/catalog/1/changelog?lang=pl&limit=1` | `200 OK`, zgodne z `Configuration:ApiV2BaseUrl` w `SUSModder/appsettings.json` |
| `If-None-Match` z cudzysłowami | `304 Not Modified` |
| `If-None-Match` bez cudzysłowów | `200 OK` |
| `lang=de` | `400 VALIDATION_ERROR` |
| `limit=0` | `400 VALIDATION_ERROR` |
| nieistniejący mod | `404 NOT_FOUND` |

Uwagi kontraktowe:

- `data[].id` produkcyjnie przychodzi jako string, mimo że kontrakt pokazuje liczbę. Model klienta powinien tolerować string/liczbę.
- `githubRepo` w odpowiedzi wygląda jak URL assetu/downloadu, nie `owner/repo`; UI nie powinno polegać na tym polu w MVP.
- Dokumenty konsumenckie API v2 (`consumer-susmodder-3x.md`, `endpoint-checklist.md`, rollout status) nie wymieniają jeszcze changelog endpointu.
- Obecny `SUSModderApiClient` obcina cudzysłowy z ETag (`Trim('"')`), a backend oczekuje quoted ETag dla `304`.

---

## 3. Cel

Dodać obsługę changelogów modów w trzech miejscach:

1. Po prawidłowej, interaktywnej aktualizacji moda bez auto-update — w modalu/podsumowaniu sukcesu aktualizacji jako rozwijane pole changeloga.
2. W szczegółach moda w panelu po prawej stronie — mały przycisk prowadzący do changelogów, umieszczony pod nagłówkiem/nazwą/opisem.
3. Po zainstalowaniu moda — przycisk w modalu sukcesu instalacji otwierający osobny modal z changelogiem ostatniej lub zainstalowanej wersji.

---

## 4. Non-goals

- Nie dodawać admin endpointów do aplikacji desktopowej.
- Nie pobierać changelogów bezpośrednio z GitHub API w kliencie.
- Nie blokować instalacji lub aktualizacji, gdy changelog API nie działa.
- Nie dodawać nowego renderera markdown/NuGet w MVP.
- Nie zmieniać Velopack ani flow aktualizacji samej aplikacji.
- Nie dodawać SQLite cache w MVP, o ile nie pojawi się wymaganie offline.

---

## 5. User workflow

### 5.1. Panel szczegółów moda

1. Użytkownik wybiera mod z listy.
2. W prawym panelu (`ModDetailDrawer`) widzi przycisk `Changelog` / `Lista zmian`.
3. Kliknięcie otwiera modal changelogów dla wybranego moda.
4. Modal pobiera `limit=10` lub `limit=20` i pokazuje listę wydań.
5. Jeśli brak wpisów, UI pokazuje lokalizowany stan pusty.
6. Jeśli API zwróci błąd, UI pokazuje lokalizowany komunikat i pozwala zamknąć modal.

### 5.2. Sukces instalacji moda

1. Instalacja kończy się sukcesem.
2. Pokazuje się istniejący panel `PostInstallSuccessView`.
3. Panel dostaje dodatkowy przycisk `Zobacz changelog` / `View changelog`.
4. Kliknięcie otwiera osobny modal changeloga.
5. Dla instalacji najnowszej wersji wystarczy `limit=1`.
6. Dla instalacji starszej/przypiętej wersji klient pobiera większy limit i dopasowuje wpis po `version`.

### 5.3. Sukces aktualizacji moda bez auto-update

1. Użytkownik ręcznie potwierdza aktualizację moda z wyłączonym auto-update.
2. Aktualizacja kończy się sukcesem.
3. UI pokazuje podsumowanie sukcesu aktualizacji.
4. Pod sukcesem jest expander `Co się zmieniło?` / `What changed?`.
5. Changelog ładuje się lazy po rozwinięciu.
6. Dla wielu aktualizacji — osobny expander per mod.
7. Auto-update pozostaje cichy/toastowy i nie pokazuje modala changeloga.

---

## 6. Language / i18n impact

Obsługiwane języki MVP: `pl`, `en`. Fallback produktu: `pl`.

Zasady:

- Klient wysyła do API `lang=pl` albo `lang=en` według aktualnego języka aplikacji.
- Jeżeli przyszły język aplikacji nie jest obsługiwany przez endpoint, Core mapuje go do `pl`.
- Jeśli API zwróci `fallbackLanguage: "en"` przy `requestedLanguage: "pl"`, UI pokazuje małą informację:
  - PL: `Tłumaczenie po polsku nie jest jeszcze gotowe — pokazujemy wersję angielską.`
  - EN: `Polish translation is not ready yet — showing English.`
- Treść `body`, `releaseName`, `version`, `releaseUrl` to dane z backendu — nie lokalizować po stronie klienta.
- Wszystkie nowe napisy UI muszą być w `SUSModder/Localization/pl.json` i `SUSModder/Localization/en.json`.
- Placeholdery muszą mieć parytet PL/EN.
- Liczniki wpisów używać ostrożnie; jeśli pojawią się liczby w copy, rozważyć ICU/pluralizację w osobnym kroku.

Proponowane klucze:

```json
"ModChangelog": {
  "Button": "Lista zmian",
  "WindowTitle": "Lista zmian: {0}",
  "Loading": "Ładowanie listy zmian...",
  "Empty": "Brak changeloga dla tego moda.",
  "Error": "Nie udało się pobrać changeloga. Spróbuj ponownie później.",
  "OpenRelease": "Otwórz release na GitHub",
  "FallbackLanguageNotice": "Tłumaczenie po polsku nie jest jeszcze gotowe — pokazujemy wersję angielską.",
  "WhatChanged": "Co się zmieniło?"
}
```

Analogiczne klucze w `en.json`.

Dodatkowo:

- `Dialogs.PostInstallSuccess.ShowChangelogButton`
- `Dialogs.UpdateSuccess.ChangelogExpander`

---

## 7. Core business logic responsibilities

### 7.1. Nowe modele API

Dodać np. `SUSModder.Core/Api/Models/CatalogChangelogModels.cs`:

```csharp
public sealed class CatalogChangelogEntryDto
{
    public long Id { get; init; }
    public int ModId { get; init; }
    public string Version { get; init; } = string.Empty;
    public string ReleaseName { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string RequestedLanguage { get; init; } = string.Empty;
    public string? FallbackLanguage { get; init; }
    public string TranslationStatus { get; init; } = string.Empty;
    public string? TranslationProvider { get; init; }
    public string? TranslationModel { get; init; }
    public string? ReleaseUrl { get; init; }
    public string? GithubRepo { get; init; }
    public string? Source { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public DateTimeOffset? FetchedAt { get; init; }
    public DateTimeOffset? TranslatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
```

Model powinien tolerować `id` jako string dzięki istniejącemu `JsonNumberHandling.AllowReadingFromString` w `SUSModderApiClient`.

### 7.2. Rozszerzenie `ISUSModderApiClient`

Dodać metodę:

```csharp
Task<SusModderApiResult<List<CatalogChangelogEntryDto>>> GetCatalogChangelogAsync(
    int modId,
    string lang = "pl",
    int limit = 5,
    string? ifNoneMatch = null,
    CancellationToken cancellationToken = default);
```

Implementacja:

- path: `catalog/{modId}/changelog`
- query: `lang`, `limit`
- public GET bez auth.

### 7.3. Serwis domenowy

Dodać `SUSModder.Core/Services/ModChangelogService.cs`.

Odpowiedzialności:

- Waliduje i normalizuje `lang` do `pl` / `en`.
- Waliduje `limit` do zakresu `1..20`.
- Wykorzystuje `ISUSModderApiClient`.
- Dodaje memory cache z TTL 2 minuty.
- Obsługuje ETag i `304`.
- Dla `404` zwraca stan pusty lub wynik bez wpisów, nie wyjątek krytyczny.
- Dla błędów sieciowych zwraca kontrolowany rezultat dla UI.
- Nie loguje treści changeloga.

### 7.4. ETag

Produkcja wymaga quoted ETag w `If-None-Match`. Obecny klient API przechowuje ETag bez quotes.

MVP:

- Dla nowego flow re-quote'ować ETag przed wysłaniem, jeśli nie zaczyna się od `"`.
- Dodać test regresyjny, że request z cached ETag może dostać `304`.

Późniejszy refactor:

- Ujednolicić zachowanie ETag dla całego `SUSModderApiClient`, aby nie obcinać cudzysłowów albo poprawnie je odtwarzać w `SendAsync`.

---

## 8. UI / Avalonia responsibilities

### 8.1. Nowy modal changeloga moda

Dodać:

- `SUSModder/ViewModels/ModChangelogViewModel.cs`
- `SUSModder/Views/ModChangelogDialog.axaml`
- `SUSModder/Views/ModChangelogDialog.axaml.cs`

Wymagania UI:

- Stan loading.
- Stan empty.
- Stan error.
- Lista wpisów z wersją, datą publikacji i tytułem release.
- Treść body jako tekst z zachowanymi line breaks i bulletami.
- `Expander` dla wielu wpisów.
- Link/przycisk `Otwórz release na GitHub`, jeśli `releaseUrl` jest dostępny.
- Informacja o fallbacku językowym, jeśli `fallbackLanguage` nie jest null.
- Styl spójny z istniejącymi modalami i kartami.

Nie używać obecnego `ChangelogDialog` bezpośrednio, bo ma copy i strukturę dla changeloga aplikacji.

### 8.2. Panel szczegółów moda

Plik:

- `SUSModder/Views/ModDetailDrawer.axaml`

Dodać mały przycisk pod nazwą/wersją/opisem:

- Widoczny dla `SelectedMod.Id > 0`.
- Command: `OpenSelectedModChangelogCommand`.
- Lokalizowany label.

### 8.3. Sukces instalacji

Pliki:

- `SUSModder/ViewModels/PostInstallSuccessViewModel.cs`
- `SUSModder/Views/PostInstallSuccessView.axaml`
- `SUSModder/ViewModels/MainWindowViewModel.ModOperations.cs`

Zmiany:

- Rozszerzyć `PostInstallSuccessViewModel` o przycisk/komendę `ShowChangelogCommand` lub event `ChangelogRequested`.
- Przekazać `modId`, `modName`, `modVersion` do viewmodelu sukcesu.
- Kliknięcie otwiera `ModChangelogDialog`.
- Nie zamykać automatycznie modala sukcesu, chyba że UX zostanie świadomie wybrany inaczej.

### 8.4. Sukces aktualizacji ręcznej

Plik główny:

- `SUSModder/ViewModels/MainWindowViewModel.Updates.cs`

Obecny `ShowUpdateSummaryAsync` buduje hardcoded PL string. Przy tej okazji warto:

- Zastąpić listy stringów modelem wyniku aktualizacji:

```csharp
private sealed record CompletedModUpdate(
    int ModId,
    string ModName,
    string CurrentVersion,
    string NewVersion);
```

- Przy udanych ręcznych aktualizacjach przechowywać `CompletedModUpdate`, nie sam string.
- Dodać lokalizowany modal/panel podsumowania albo przynajmniej lokalizowane copy w istniejącym podsumowaniu.
- Dodać expander per udany mod.
- Ładować changelog lazy po rozwinięciu.

---

## 9. Config i migracje

- Nie pisać runtime do `appsettings.json`.
- Używać istniejącego `Configuration:ApiV2BaseUrl`.
- Brak nowej tabeli SQLite w MVP.
- Jeśli będzie potrzebny cache offline, dodać osobny plan migracji dla tabeli `mod_changelog_cache` i wpis w `DatabaseService.ApplyMigrations()` z `PRAGMA user_version`.

---

## 10. Platform, packaging, updater, telemetry, privacy, AV

| Obszar | Wpływ |
|---|---|
| Steam/Epic | Brak różnic; changelog zależy od `modId`, nie od źródła gry. |
| Velopack | Brak wpływu. |
| Packaging | Brak nowych binarek i brak nowych plików runtime poza kodem/locale. |
| AV reputation | Bez zmian; brak wykonywania pobranych treści. |
| Privacy | Publiczny GET bez user hash i bez auth. |
| Telemetria | Nie dodawać w MVP. Jeśli później: tylko `mod_changelog_opened` z `modId` i `language`, bez body/URL. |

---

## 11. Verification plan

### 11.1. Testy automatyczne

Core:

- Deserializacja odpowiedzi z `id` jako string.
- PL response.
- EN response.
- `fallbackLanguage` nie-null.
- `404 NOT_FOUND` bez crasha UI.
- `400 VALIDATION_ERROR` dla złego języka/limitu.
- `304` zwraca cached data.
- ETag jest wysyłany z quotes.

UI/viewmodel:

- `ModChangelogViewModel` przechodzi przez stany loading/empty/error/success.
- `PostInstallSuccessViewModel` pokazuje/ukrywa przycisk changeloga zgodnie z danymi moda.
- Komenda panelu szczegółów nie działa dla braku wybranego moda.

Build/test:

```powershell
dotnet build SUSModder.sln
dotnet test SUSModder.sln
```

### 11.2. Testy manualne

- PL: otworzyć Town of Us w panelu szczegółów i kliknąć `Lista zmian`.
- EN: zmienić język aplikacji i powtórzyć.
- Zainstalować mod i sprawdzić przycisk changeloga w panelu sukcesu.
- Zainstalować starszą/przypiętą wersję i sprawdzić dopasowanie wpisu.
- Wymusić ręczną aktualizację z wyłączonym auto-update i sprawdzić expander.
- Sprawdzić, że auto-update nie pokazuje modala.
- Zasymulować offline/API 500 — instalacja/aktualizacja nadal kończy się normalnie, changelog pokazuje localized error.
- Sprawdzić fallback PL→EN, jeśli backend zwróci `fallbackLanguage`.

### 11.3. Smoke test produkcyjny API

Przed merge/release powtórzyć:

```powershell
curl.exe -i "https://api.susmodder-cdn.ovh/v2/catalog/1/changelog?lang=pl&limit=1"
curl.exe -i "https://api.susmodder-cdn.ovh/v2/catalog/1/changelog?lang=en&limit=1"
curl.exe -i -H 'If-None-Match: "<etag>"' "https://api.susmodder-cdn.ovh/v2/catalog/1/changelog?lang=pl&limit=1"
```

---

## 12. Suggested implementation order

### Pakiet 1 — Core API + modele

Pliki:

- `SUSModder.Core/Api/ISUSModderApiClient.cs`
- `SUSModder.Core/Api/SUSModderApiClient.cs`
- `SUSModder.Core/Api/Models/CatalogChangelogModels.cs`
- `SUSModder.Core/Services/ModChangelogService.cs`
- testy Core

Można robić równolegle z pakietem 2 po ustaleniu kontraktu viewmodelu.

### Pakiet 2 — Modal changeloga

Pliki:

- `SUSModder/ViewModels/ModChangelogViewModel.cs`
- `SUSModder/Views/ModChangelogDialog.axaml`
- `SUSModder/Views/ModChangelogDialog.axaml.cs`
- `SUSModder/Localization/pl.json`
- `SUSModder/Localization/en.json`

### Pakiet 3 — Panel szczegółów moda

Pliki:

- `SUSModder/Views/ModDetailDrawer.axaml`
- `SUSModder/ViewModels/MainWindowViewModel.cs`
- ewentualnie nowy partial `MainWindowViewModel.ModChangelog.cs`

### Pakiet 4 — Sukces instalacji

Pliki:

- `SUSModder/ViewModels/PostInstallSuccessViewModel.cs`
- `SUSModder/Views/PostInstallSuccessView.axaml`
- `SUSModder/ViewModels/MainWindowViewModel.ModOperations.cs`

### Pakiet 5 — Sukces aktualizacji ręcznej

Pliki:

- `SUSModder/ViewModels/MainWindowViewModel.Updates.cs`
- ewentualnie nowy `UpdateSummaryViewModel` i widok

Największe ryzyko pakietu: obecne podsumowanie aktualizacji jest hardcoded PL i oparte na stringach.

### Pakiet 6 — Dokumentacja API v2 klienta

Zaktualizować:

- `DOC/POC/API v2/consumer-susmodder-3x.md`
- `DOC/POC/API v2/endpoint-checklist.md`
- `DOC/PLAN/2026-06-07-api-v2-rollout-status.md`

---

## 13. Parallelizable tasks

Można równoleglić:

1. Core API/model/service + testy.
2. UI modal + i18n keys na mockowanych danych.
3. Dokumentacja klienta API v2.

Sekwencyjne zależności:

1. Hooki UI w instalacji/panelu/aktualizacji powinny poczekać na stabilny `ModChangelogService`.
2. Flow aktualizacji ręcznej powinien być ostatni, bo wymaga uporządkowania modelu podsumowania.

---

## 14. Otwarte pytania

1. Czy backend powinien dodać `?version=` dla changeloga konkretnej wersji? Bez tego klient musi pobierać listę i dopasowywać po `version`.
2. Czy przycisk w panelu szczegółów ma być zawsze widoczny, czy ukrywany po pre-checku dostępności changelogów? Rekomendacja: zawsze widoczny, lazy fetch.
3. Czy auto-update ma dostać tylko toast z akcją `Zobacz changelog`, czy pozostać bez changeloga? Rekomendacja MVP: bez modala i bez dodatkowego toast action.
4. Czy w modalu pokazywać tylko najnowszy changelog, czy pełną historię? Rekomendacja: panel szczegółów `limit=10`, post-install/update `limit=1` lub dopasowanie wersji.

---

## 15. Sources used

- `mcp-rag` — lookup istniejących wzorców Core/UI/API.
- `sus-free-doc-scout` — szeroki scan dokumentacji API v2 i planów.
- `DOC/POC/API v2/contracts/CHANGELOG_API.md`.
- `DOC/POC/API v2/consumer-susmodder-3x.md`.
- `DOC/PLAN/2026-06-07-api-v2-rollout-status.md`.
- `DOC/PLAN/2026-06-04-susmodder-client-api-sync-plan.md`.
- `SUSModder.Core/Api/*`.
- `SUSModder.Core/Services/ChangelogService.cs`.
- `SUSModder/Views/ModDetailDrawer.axaml`.
- `SUSModder/Views/PostInstallSuccessView.axaml`.
- `SUSModder/ViewModels/PostInstallSuccessViewModel.cs`.
- `SUSModder/ViewModels/MainWindowViewModel.ModOperations.cs`.
- `SUSModder/ViewModels/MainWindowViewModel.Updates.cs`.
- Microsoft Learn — `HttpRequestHeaders.IfNoneMatch` / ETag assumptions.
- Produkcyjne smoke testy `curl.exe` na `susmodder.app` i `api.susmodder-cdn.ovh`.
