# 09 – Obsługa stanu offline / braku internetu

**Priorytet:** 🟡 P1  
**Effort:** ~1-2h  
**Źródło:** Review sus-senior-quality-reviewer (GLM 5.1)

## Problem

Gdy aplikacja nie ma połączenia z internetem:
- Lista modów jest pusta – user nie wie, czy to błąd czy normalne
- Brak komunikatu offline
- API requesty wiszą lub failują po cichu

## Propozycja

### Empty state z kontekstem

Gdy lista modów jest pusta, zamiast pustej siatki:

```
┌────────────────────────────────────────┐
│                                        │
│           🌐                           │
│   Nie można pobrać listy modów         │
│   Sprawdź połączenie z internetem      │
│                                        │
│   [Spróbuj ponownie]  [Tryb offline]   │
│                                        │
└────────────────────────────────────────┘
```

### Tryb offline

- Jeśli API nie odpowiada → pokaż lokalnie dostępne (zainstalowane) mody
- Oznacz je jako "Tryb offline – tylko zainstalowane"
- Wyłącz przyciski wymagające sieci (Install, Update z API)

### Detekcja stanu

- W `MainWindowViewModel.Initialization.cs` – przy ładowaniu configu z API
- Jeśli timeout / błąd sieci → `IsOffline = true`
- Property w VM → zmiana empty state w UI

## Gdzie w kodzie

| Co | Plik |
|----|------|
| Detekcja offline | `MainWindowViewModel.Initialization.cs` – ładowanie configu |
| Empty state UI | `MainWindow.axaml` – nowy panel widoczny gdy `Mods.Count == 0` |
| Tryb offline | `ConfigService` – fallback do lokalnego config.json |

## Decyzje

- [ ] Czy pokazywać empty state z informacją offline czy generyczny "brak modów"?
- [ ] Czy zapisywać ostatnią znaną listę modów do cache na wypadek offline?
