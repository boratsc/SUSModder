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

## Decyzje

- [ ] Wariant A (MVP) czy od razu B (GitHub API)?
- [ ] Widok jako osobne okno czy inline dialog?
- [ ] Czy pokazywać changelog tylko po aktualizacji, czy też dostępny z menu "O aplikacji"?
- [ ] Czy pokazywać pełną historię (kilka wersji wstecz) czy tylko ostatnią?
