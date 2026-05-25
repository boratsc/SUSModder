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
