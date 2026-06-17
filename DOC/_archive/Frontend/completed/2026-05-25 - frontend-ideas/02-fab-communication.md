# 02 – FAB – lepsza komunikacja i discoverability

**Priorytet:** 🟡 P1  
**Effort:** ~2-3h  

## Stan obecny

FAB to okrągły przycisk `+` (56×56). Otwiera menu z 8 opcjami. Animowany (scale hover, rotate 45°, backdrop).

**Brakuje:**
- Badge''a/licznika dostępnych aktualizacji
- Kontekstowej ikony (zawsze ➕)
- Szybkiej akcji bez wchodzenia w menu

## Propozycje

### 1. FAB badge z licznikiem aktualizacji

```
     ┌──┐
     │ 3│  ← czerwony badge
  ┌──┴──┴──┐
  │   ⬇️    │  ← ikona zmieniona
  └────────┘
```

- Czerwony badge gdy `AvailableUpdatesCount > 0`
- W menu FAB opcja "Sprawdź aktualizacje" też z badge''em
- Znika gdy wszystkie update''y ogarnięte

### 2. Kontekstowa ikona FAB

| Stan | Ikona |
|------|-------|
| Domyślnie | ➕ (menu) |
| Są update''y | ⬇️ (pobierz) |
| Trwa instalacja | ⏳ (progress) |
| Wszystko OK | ✅ (gotowe) |

### 3. Ostatnia użyta akcja jako primary

FAB mógłby pamiętać ostatnią akcję i pokazywać ją jako szybki przycisk (long-press = menu, tap = ostatnia akcja).

### 4. Discord promo – opcje

| Opcja | Opis |
|-------|------|
| Zostawić | Rotacyjna karta obok FAB (co 10s) – obecny stan |
| Status bar | Przenieść jako sekcję w status barze (jak w `FRONTEND_REORGANIZATION_PLAN.md` Faza 3) |
| Mniejszy chip | Kompaktowy chip przy FAB zamiast pełnej karty |

## Gdzie w kodzie

| Co | Plik |
|----|------|
| Styl FAB | `SUSModder/Styles/FabButtonStyle.axaml` |
| FAB w layoucie | `MainWindow.axaml` (status bar, kolumna 0) |
| Discord promo | `MainWindowViewModel.DiscordPromo.cs`, `MainWindow.axaml.cs:49` |
| Licznik update''ów | `MainWindowViewModel.Updates.cs` → `AvailableUpdatesCount` już istnieje |

## Decyzje

- [ ] FAB badge: kropka czy licznik?
- [ ] Kontekstowa ikona: wystarczy zmiana przy update''ach czy pełny zestaw?
- [ ] Long-press = ostatnia akcja – czy warto?
- [ ] Discord promo: zostawić czy przenieść?
