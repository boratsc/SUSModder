# 04 – Changelog / "Co nowego" po aktualizacji

**Priorytet:** 🟡 P1  
**Effort:** ~2-3h (MVP)  

## Stan obecny

Brak. Po aktualizacji Velopack restartuje apkę – użytkownik nie wie co się zmieniło.

## Propozycja

Po aktualizacji pokazać okno z listą zmian – "newsletter" informujący co nowego.

### Źródło danych (3 warianty)

| Wariant | Opis | Plusy | Minusy |
|---------|------|-------|--------|
| **A) `whatsnew.json`** | Plik JSON w apce z release notes | Prosty, offline | Trzeba aktualizować ręcznie |
| **B) GitHub API** | `GET /repos/{owner}/{repo}/releases` | Automatyczne | Rate limiting, parsowanie MD |
| **C) Własne API** | `GET /api/changelog` na susmodder.app | Kontrola formatu | Trzeba utrzymywać backend |

**Rekomendacja:** Wariant A (MVP) + później B.

### Flow

1. Przy starcie: sprawdź `LastSeenVersion` w `user-settings.json`
2. Jeśli `CurrentVersion > LastSeenVersion` → wczytaj changelog
3. Pokaż modal "Co nowego w SUSModder {version}"
4. Po zamknięciu zapisz `LastSeenVersion = CurrentVersion`
5. Nie pokazuj ponownie dla tej samej wersji

### UI

```
┌──────────────────────────────────────────────┐
│  🆕 Co nowego w SUSModder 2.5.0?        [✕] │
│                                              │
│  ✨ Nowe funkcje                             │
│  • Dodano system toast notifications         │
│  • FAB pokazuje licznik aktualizacji         │
│                                              │
│  🔧 Poprawki                                 │
│  • Naprawiono błąd z Epic Games              │
│                                              │
│  [Zamknij]    [Pełny changelog na GitHub →]  │
└──────────────────────────────────────────────┘
```

### Format `whatsnew.json`

```json
{
  "version": "2.5.0",
  "date": "2026-05-20",
  "sections": [
    {
      "icon": "✨",
      "title": "Nowe funkcje",
      "items": ["Dodano system toast notifications", "..."]
    },
    {
      "icon": "🔧",
      "title": "Poprawki",
      "items": ["Naprawiono błąd z Epic Games", "..."]
    }
  ],
  "githubUrl": "https://github.com/whichtwix/SUSModder/releases/tag/v2.5.0"
}
```

### Integracja z toastami

Toast po pierwszym uruchomieniu:
> 🆕 Zaktualizowano do v2.5.0! Kliknij, aby zobaczyć co nowego.

## Gdzie w kodzie

| Co | Plik |
|----|------|
| Wykrycie nowej wersji | `MainWindowViewModel.Initialization.cs` |
| `lastSeenVersion` | `user-settings.json` – nowe pole |
| Widok changeloga | Nowy: `Views/ChangelogDialog.axaml` LUB użyć istniejącego `IsInlineDialogVisible` |
| Dane | `Assets/whatsnew.json` (CopyToOutput) |
| Link do GitHub | `MainWindowViewModel.ExternalActions.cs` – `OpenUrl()` istnieje |

## Decyzje (ostateczne)

- [x] Wariant B (GitHub API) – jako primary, brak fallbacka do pliku
- [x] Jeśli GitHub API nie odpowiada – tylko toast z linkiem do GitHub releases
- [x] Widok jako osobne okno (Window) – wzorowane na `NoUpdateDialog`
- [x] Changelog tylko po aktualizacji (LastSeenVersion w user-settings.json)
- [x] Tylko ostatnia wersja, z linkiem do GitHub dla pełnej historii
- [x] Toast po każdej aktualizacji, z onClick: otwiera dialog (jeśli GitHub OK) lub GitHub (jeśli fail)
- [x] `whatsnew.json` – usunięty, nieutrzymywany

---

## Stan implementacji (na 2026-05-26)

### Wdrożone komponenty

| Komponent | Plik | Opis |
|-----------|------|------|
| Model danych | `SUSModder.Core/Models/ChangelogData.cs` | `ChangelogData` + `ChangelogSection` z `titleKey` |
| Serwis GitHub API | `SUSModder.Core/Services/ChangelogService.cs` | `FetchFromGitHubAsync()`, `ParseMarkdownToSections()`, `IsNewerVersion()` |
| UserSettings | `SUSModder.Core/Configuration/UserSettings.cs` | Pole `LastSeenVersion` |
| UserSettingsService | `SUSModder.Core/Services/UserSettingsService.cs` | Metoda `SaveLastSeenVersion()` |
| Widok | `SUSModder/Views/ChangelogDialog.axaml` + `.axaml.cs` | Okno dialogowe z sekcjami, animacjami |
| Lokalizacja PL/EN | `SUSModder/Localization/pl.json` + `en.json` | Klucze `Changelog.*`, `Toast.AppUpdated` |
| Integracja | `MainWindowViewModel.Initialization.cs` | `ShowChangelogIfNewVersionAsync()` w startup flow |

### Flow

```
CheckForUpdatesAfterMainWindowLoadAsync()
  ↓ (po 1.5s)
ShowChangelogIfNewVersionAsync()
  ↓
IsNewerVersion(AppVersion, lastSeenVersion)?
  ├─ false → skip
  └─ true → FetchFromGitHubAsync("boratsc", "SUSModder")  (prefetch dla onClick)
              ↓
         Toast: "🆕 Zaktualizowano do X!" (8s, auto-close)
              ↓
         ┌── Kliknięcie? ──→ sukces? → ChangelogDialog
         │                   └─ fail? → otwórz GitHub releases w przeglądarce
         └── Brak kliknięcia → (nic, toast znika po 8s)
              ↓
         SaveLastSeenVersion(AppVersion)
```

**Ważne:** Dialog changeloga NIE pokazuje się automatycznie – tylko po kliknięciu w toasta.

### GitHub API

- Endpoint: `GET https://api.github.com/repos/boratsc/SUSModder/releases/latest`
- Parsuje markdown body: `## ` → sekcja, `- ` / `* ` → item
- Emoji w tytule sekcji → ikona, ignoruje polskie znaki (Latin Extended)
- User-Agent: `SUSModder/2.9.0`

### Parsowanie wersji

- Obsługuje `v` prefix i `-beta`/`-rc.*` sufiksy prerelease
- `2.6.1 > 2.6.0` ✅
- `2.6.0 > 2.6.0-beta` ✅ (stable > beta)
- `2.6.0-beta > 2.5.0` ✅ (wyższy numer)
