# 07 – Większe funkcje na później

**Priorytet:** ⚪ P3-P4

## 1. Dwuetapowy flow po instalacji

Zamiast od razu otwierać DLL modal – pokazać ekran sukcesu z wyborem:

```
+------------------------------------------+
|  ✅ Town of Us zainstalowany!  🎉        |
|                                          |
|  Ten mod obsługuje modyfikacje DLL.      |
|  Chcesz dodać dodatkowe pluginy?         |
|                                          |
|  [🚀 Uruchom]  [🔧 Dodaj DLL...]        |
|                                          |
|  ☐ Nie pokazuj więcej                   |
+------------------------------------------+
```

**Effort:** ~1-2 dni. Najlepsze UX dla flow poinstalacyjnego.

---

### Implementacja [2026-05-27] — Zrealizowane ✅

#### Plan

**Cel:** Po instalacji moda obsługującego DLL, zamiast od razu otwierać modal DLL, pokazać dialog sukcesu z wyborem: Uruchom grę / Dodaj DLL.

**Kluczowe decyzje:**
1. Nowy `PostInstallSuccessDialog` (Window) z trzema wynikami: Launch, AddDll, Dismiss
2. Flaga `DontShowPostInstallDialog` w `FullModInstallation` (per-mod, w installation-map.json)
3. Dialog pojawia się tylko dla modów z `DllInstallPath != null`
4. Gdy "Nie pokazuj więcej" → flaga zapisywana, dialog pomijany przy kolejnych instalacjach tego moda

#### Zmiany w kodzie

| # | Plik | Zmiana |
|---|------|--------|
| 1 | `SUSModder.Core/Models/FullModInstallation.cs` | Dodano `DontShowPostInstallDialog` (bool, JsonPropertyName="dontShowPostInstallDialog") |
| 2 | `SUSModder/Views/PostInstallSuccessDialog.axaml` | Nowy dialog: ikona sukcesu, tytuł, komunikat, 2 przyciski, checkbox |
| 3 | `SUSModder/Views/PostInstallSuccessDialog.axaml.cs` | Code-behind: `PostInstallAction` enum, obsługa przycisków, lokalizacja |
| 4 | `SUSModder/ViewModels/MainWindowViewModel.ModOperations.cs` | W `Install()`: zamiast bezpośredniego `ShowDllSelectionWindowInternal` → sprawdź flagę, pokaż dialog, obsłuż wybór. Dodano 3 metody pomocnicze |
| 5 | `SUSModder/Localization/en.json` | Sekcja `Dialogs.PostInstallSuccess` (6 kluczy) |
| 6 | `SUSModder/Localization/pl.json` | Sekcja `Dialogs.PostInstallSuccess` (6 kluczy) |

#### UX

- **Warunek wyświetlenia:** mod obsługuje DLL (`DllInstallPath` niepusty) AND flaga `DontShowPostInstallDialog == false`
- **Flaga "Nie pokazuj więcej":** per-mod, zapisywana w `installation-map.json`
- **Wybór Launch:** wywołuje `LaunchAsync()`
- **Wybór Add DLL:** otwiera `DllSelectionWindowInternal` (jak do tej pory)
- **Domyślne zachowanie (gdy brak DLL lub flaga ustawiona):** pozostaje bez zmian (bez dialogu pośredniego)

#### Flow

```
Install()
  └→ InstallSteamModAsync / InstallEpicModAsync
  └→ if (success)
       └→ modConfig.DllInstallPath != null?  ← mod obsługuje DLL?
            ├→ NIE: koniec (bez zmian)
            └→ TAK:
                 ├→ IsPostInstallDialogSuppressedAsync()?
                 │    ├→ TAK (flaga ustawiona): koniec (bez zmian)
                 │    └→ NIE: ShowPostInstallSuccessDialogAsync()
                 │         ├→ Launch → LaunchAsync()
                 │         ├→ AddDll → ShowDllSelectionWindowInternal()
                 │         └→ Dismiss → nic
                 │
                 └→ Jeśli checkbox "Nie pokazuj więcej" zaznaczony
                      → SaveDontShowPostInstallDialogAsync()
```
## 2. Auto-update aplikacji i modów

### Auto-update aplikacji

Przydatne – aplikacja sama sprawdza i pobiera update w tle (Velopack już to wspiera).  
**Effort:** ~1 dzień.

### Auto-update modów (checkbox)

**Jak to działa teraz** (z kodu w `MainWindowViewModel.Updates.cs`):

```
CheckForModUpdatesAsync()
  └→ ModUpdateManager.CheckForUpdatesAsync()
       └→ ConfigRepository.LoadConfigFromApiAsync()  // pobiera zdalny config
       └→ porównuje wersje lokalne vs zdalne
       └→ zwraca List<ModUpdateInfo>
  └→ ProcessUpdatesWithIndividualDialogsAsync(updates)
       └→ per mod: ShowUpdateModConfirmDialogAsync()  ← TYLKO TO POMIJAMY
       └→ per mod: UpdateSingleModWithDialogAsync()   // reinstalacja + progress
            └→ ConfigService.CheckSingleModUpdateAsync()
            └→ ConfigService.UpdateSingleModConfigAsync()
            └→ reinstalacja (delete + download + extract)
            └→ UpdateProgressDialog pokazuje postęp
```

**Z auto-update checkboxem** – jedyna zmiana:

```
if (modItem.AutoUpdateEnabled)
    → pomiń ShowUpdateModConfirmDialogAsync()
    → od razu UpdateSingleModWithDialogAsync()
else
    → pokaż dialog jak dotychczas
```

**Co zostaje bez zmian:**
- `UpdateProgressDialog` – user widzi postęp (dobrze, niech widzi)
- `ModUpdateManager` – sprawdzanie wersji przez API bez zmian
- DLL – ten sam flow, tylko `.dll` zamiast `.zip`
- Każda aktualizacja to reinstall – nie ma wyjątków

**Gdzie w kodzie:**

| Co | Plik |
|----|------|
| Sprawdzanie update'ów | `MainWindowViewModel.Updates.cs:33` |
| Confirm dialog | `MainWindowViewModel.Updates.cs` – `ShowUpdateModConfirmDialogAsync()` |
| Wykonanie update'u | `MainWindowViewModel.Updates.cs` – `UpdateSingleModWithDialogAsync()` |
| Progress dialog | `Views/UpdateProgressDialog.axaml` |
| Checkbox + stan | `ModItem.AutoUpdateEnabled` (nowe pole) + `user-settings.json` per mod |

**Effort:** ~2-3h – dodać pole, checkbox w UI, ifa pomijającego dialog.

---

### Implementacja [2026-05-27] — Zrealizowane ✅

#### Plan

**Cel:** Użytkownik może włączyć auto-aktualizację per mod z poziomu panelu bocznego. Gdy włączona → aktualizacje instalowane automatycznie bez dialogu potwierdzenia.

**Kluczowe decyzje:**
1. Nowa właściwość `AutoUpdateEnabled` w `FullModInstallation` (osobna od istniejącego `DisableAutoUpdatePrompt` przeznaczonego do przypinania wersji)
2. Persystencja przez `installation-map.json` (nie `config.json` ani `user-settings.json`)
3. UI: ToggleSwitch w panelu bocznym dla zainstalowanych modów (nie-Vanilla, nie-przypiętych)
4. Zmiana tylko w `ProcessUpdatesWithIndividualDialogsAsync` → skip `ShowUpdateModConfirmDialogAsync`

#### Zmiany w kodzie

| # | Plik | Zmiana |
|---|------|--------|
| 1 | `SUSModder.Core/Models/FullModInstallation.cs` | Dodano `AutoUpdateEnabled` (bool, JsonPropertyName="autoUpdateEnabled") |
| 2 | `SUSModder/ViewModels/ModItem.cs` | Dodano `AutoUpdateEnabled` (ReactiveObject property) + `IsAutoUpdateToggleVisible` (derived) |
| 3 | `SUSModder/ViewModels/MainWindowViewModel.Helpers.cs` | Ładowanie `AutoUpdateEnabled` z install map przy odświeżaniu modów |
| 4 | `SUSModder/ViewModels/MainWindowViewModel.ModOperations.cs` | Dodano `ToggleAutoUpdateAsync(ModItem, bool)` — zapis do install map |
| 5 | `SUSModder/ViewModels/MainWindowViewModel.Updates.cs` | W `ProcessUpdatesWithIndividualDialogsAsync`: jeśli `AutoUpdateEnabled=true` → skip confirm dialog |
| 6 | `SUSModder/Views/MainWindow.axaml` | ToggleSwitch w panelu bocznym (między Update a Delete) |
| 7 | `SUSModder/Views/MainWindow.axaml.cs` | Event handler `AutoUpdateToggle_Click` → wywołuje `ViewModel.ToggleAutoUpdateAsync` |
| 8 | `SUSModder/Localization/en.json` | Klucz `UI.Labels.AutoUpdate`: "Auto-update" |
| 9 | `SUSModder/Localization/pl.json` | Klucz `UI.Labels.AutoUpdate`: "Auto-aktualizacja" |

#### UX

- ToggleSwitch widoczny tylko gdy `IsAutoUpdateToggleVisible = true` (zainstalowany, nie Vanilla, nie przypięta wersja)
- Po przełączeniu: wartość zapisywana natychmiast do `installation-map.json`
- Przy sprawdzaniu aktualizacji: jeśli `AutoUpdateEnabled=true` → pomijany jest dialog `ShowUpdateModConfirmDialogAsync`, ale nadal pokazywany jest `UpdateProgressDialog` i podsumowanie
- Domyślnie: `AutoUpdateEnabled = false` (zgodne z obecnym zachowaniem)

#### Zachowanie przy aktualizacji modów

```
CheckForModUpdatesAsync()
  └→ ModUpdateManager.CheckForUpdatesAsync()
       └→ zwraca List<ModUpdateInfo> (niezmienione)
  └→ ProcessUpdatesWithIndividualDialogsAsync(updates)
       └→ per mod:
            ├→ GetOrCreateModItemAsync()  ← przesunięte wcześniej
            ├→ if (modItem.AutoUpdateEnabled)
            │     → pomiń ShowUpdateModConfirmDialogAsync()
            │     → idź od razu do update
            └→ else
                  → pokaż dialog potwierdzenia (jak dotychczas)
            └→ UpdateSingleModWithDialogAsync() (niezmienione)
            └→ UpdateProgressDialog (niezmienione)
            └→ podsumowanie (niezmienione)
```
