# SUSModder - Integracja Wersjonowania i Kompatybilności Modów

## 🎯 Cel Projektu

Integracja dwóch nowych systemów API z aplikacją SUSModder:
1. **System Wersjonowania Modów** - możliwość instalacji starszych wersji modów FULL i DLL
2. **Matryca Kompatybilności** - informacje o kompatybilności między modami FULL a DLL

**Data utworzenia:** 2025-10-22
**Status:** 📝 Dokumentacja - Gotowa do implementacji
**Priorytet:** 🔴 Wysoki

---

## 📚 Struktura Dokumentacji

### Dokumenty Analityczne

| Dokument | Opis | Czas czytania | Dla kogo |
|----------|------|---------------|----------|
| **[README.md](./README.md)** | Ten dokument - wprowadzenie i nawigacja | 5 min | Wszyscy |
| **[00_ANALIZA_OBECNEGO_STANU.md](./00_ANALIZA_OBECNEGO_STANU.md)** | Analiza istniejącego kodu SUSModder | 20 min | Backend Dev |
| **[01_ARCHITEKTURA_ROZWIAZANIA.md](./01_ARCHITEKTURA_ROZWIAZANIA.md)** | Architektura integracji z nowymi API | 30 min | Wszyscy Dev |
| **[02_MODELE_DANYCH.md](./02_MODELE_DANYCH.md)** | Nowe klasy C# i modele danych | 15 min | Backend Dev |

### Dokumenty Implementacyjne

| Dokument | Opis | Czas czytania | Dla kogo |
|----------|------|---------------|----------|
| **[03_INSTALACJA_STARSZYCH_WERSJI.md](./03_INSTALACJA_STARSZYCH_WERSJI.md)** | Implementacja wyboru i instalacji starszych wersji | 40 min | Full-stack Dev |
| **[04_AKTUALIZACJE_DLL.md](./04_AKTUALIZACJE_DLL.md)** | Automatyczne aktualizacje modów DLL | 35 min | Backend Dev |
| **[05_SYSTEM_KOMPATYBILNOSCI.md](./05_SYSTEM_KOMPATYBILNOSCI.md)** | Integracja matrycy kompatybilności | 30 min | Full-stack Dev |
| **[06_PLAN_IMPLEMENTACJI.md](./06_PLAN_IMPLEMENTACJI.md)** | Kolejność kroków i timeline | 25 min | PM, Dev Lead |

### Dokumenty Infrastrukturalne

| Dokument | Opis | Czas czytania | Dla kogo | Priorytet |
|----------|------|---------------|----------|-----------|
| **[07_INSTALLATION_MAP_SYSTEM.md](./07_INSTALLATION_MAP_SYSTEM.md)** | System trwałej mapy zainstalowanych modów | 45 min | Backend Dev, Architect | 🔴 **KRYTYCZNY** |

---

## 🚀 Quick Start

### Dla Project Managera

1. ✅ Przeczytaj **README.md** (to co właśnie czytasz)
2. ✅ Przeczytaj **01_ARCHITEKTURA_ROZWIAZANIA.md** - zrozumiesz całościowy obraz
3. ✅ Przeczytaj **06_PLAN_IMPLEMENTACJI.md** - poznasz timeline i priorytety

**Czas:** ~30 minut

### Dla Developera (Backend)

1. ✅ Przeczytaj **README.md**
2. ✅ **WAŻNE**: Przeczytaj **07_INSTALLATION_MAP_SYSTEM.md** - KRYTYCZNE dla stabilności systemu!
3. ✅ Przeczytaj **00_ANALIZA_OBECNEGO_STANU.md** - zobaczysz co już działa
4. ✅ Przeczytaj **01_ARCHITEKTURA_ROZWIAZANIA.md** - zrozumiesz jak to połączyć
5. ✅ Przeczytaj **02_MODELE_DANYCH.md** - stworzysz nowe klasy
6. ✅ Przeczytaj **04_AKTUALIZACJE_DLL.md** - zaimplementujesz logikę aktualizacji

**Czas:** ~2.5 godziny

### Dla Developera (Frontend/Full-stack)

1. ✅ Przeczytaj **README.md**
2. ✅ Przeczytaj **01_ARCHITEKTURA_ROZWIAZANIA.md** - zrozumiesz przepływy
3. ✅ Przeczytaj **03_INSTALACJA_STARSZYCH_WERSJI.md** - zaimplementujesz UI wyboru wersji
4. ✅ Przeczytaj **05_SYSTEM_KOMPATYBILNOSCI.md** - dodasz wizualne oznaczenia

**Czas:** ~1.5 godziny

---

## 🎯 Główne Wymagania (TL;DR)

### 1. Instalacja Starszych Wersji Modów

**Problem:**
Obecnie użytkownik może zainstalować tylko najnowszą wersję moda. Brak możliwości cofnięcia się do starszej, stabilnej wersji.

**Rozwiązanie:**
- FAB menu po kliknięciu prawym przyciskiem na mod
- Opcja "Zainstaluj starszą wersję"
- Dialog z listą dostępnych wersji (z datami)
- Instalacja wybranej wersji (dla modów FULL i DLL)

**Endpoint API:**
```
GET /susmodder-config-versions?modId={id}
```

**Przykładowy response:**
```json
{
  "success": true,
  "modId": 1,
  "count": 3,
  "versions": [
    {
      "VersionId": 3,
      "ModVersion": "5.4.0",
      "AmongVersion": "2024.10.29",
      "GitHubRepoOrLink": "https://github.com/...",
      "CreatedAt": "2024-10-29T09:15:00.000Z"
    },
    {
      "VersionId": 2,
      "ModVersion": "5.3.1",
      "AmongVersion": "2024.10.01",
      "GitHubRepoOrLink": "https://github.com/...",
      "CreatedAt": "2024-10-01T14:30:00.000Z"
    }
  ]
}
```

---

### 2. Automatyczne Aktualizacje Modów DLL

**Problem:**
- Obecnie tylko mody FULL są sprawdzane pod kątem aktualizacji
- Mody DLL mogą być zainstalowane w wielu lokalizacjach (wiele modów FULL)
- Użytkownik musi ręcznie sprawdzać każdą lokalizację

**Rozwiązanie:**
- Rozszerzenie `ModUpdateChecker` o sprawdzanie modów DLL
- Wykrycie wszystkich lokalizacji gdzie DLL jest zainstalowany
- Dialog wyboru lokalizacji do aktualizacji (domyślnie wszystkie zaznaczone)
- Aktualizacja w wybranych miejscach

**Workflow:**
```
1. Sprawdź dostępne aktualizacje DLL (endpoint: /susmodder-config)
2. Dla każdego DLL do aktualizacji:
   a. Znajdź wszystkie mody FULL gdzie jest zainstalowany
   b. Pokaż dialog wyboru lokalizacji
   c. Zaktualizuj w zaznaczonych lokalizacjach
3. Pokaż podsumowanie aktualizacji
```

---

### 3. System Kompatybilności DLL z Modami FULL

**Problem:**
Użytkownicy nie wiedzą które mody DLL działają z konkretnymi modami FULL. Instalacja niekompatybilnego DLL powoduje crashe.

**Rozwiązanie:**
- Sprawdzanie kompatybilności przy instalacji DLL
- Wizualne oznaczenia statusu:
  - 🟢 **Favorite (F)** - Polecany, działa idealnie
  - 🔵 **Works (W)** - Kompatybilny, działa poprawnie
  - ⚪ **Not Tested (NT)** - Nieprzetestowany
  - 🔴 **Not Work (NW)** - Niekompatybilny, nie działa
- Ostrzeżenia przed instalacją niekompatybilnych
- Zachęta do instalacji polecanych

**Endpoint API:**
```
GET /api/compatibility?dllModId={dllId}&fullModId={fullId}
```

**Przykładowy response:**
```json
{
  "success": true,
  "query": {
    "type": "dll",
    "modId": 5,
    "modName": "AleLuduMod"
  },
  "count": 1,
  "compatibilities": [
    {
      "id": 123,
      "status": "F",
      "fullMod": {
        "id": 1,
        "name": "Town of Us",
        "version": "5.3.1"
      },
      "notes": "Działa bez problemów, wszystkie funkcje OK"
    }
  ]
}
```

---

## 🏗️ Architektura - Przegląd

```
┌─────────────────────────────────────────────────────────────┐
│                     SUSModder (C# WPF)                       │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌─────────────────────────────────────────────────────┐   │
│  │         NOWE KOMPONENTY                              │   │
│  ├─────────────────────────────────────────────────────┤   │
│  │                                                       │   │
│  │  ModVersionService (nowy)                           │   │
│  │  ├─ GetVersionHistory(modId)                        │   │
│  │  └─ InstallSpecificVersion(modId, versionId)        │   │
│  │                                                       │   │
│  │  CompatibilityService (nowy)                        │   │
│  │  ├─ GetCompatibilityInfo(dllId, fullId)            │   │
│  │  └─ GetCompatibilityStatus(dllId, fullId)          │   │
│  │                                                       │   │
│  │  DllUpdateManager (nowy)                             │   │
│  │  ├─ CheckDllUpdates()                               │   │
│  │  ├─ FindInstallLocations(dllMod)                    │   │
│  │  └─ UpdateDllInLocations(dllMod, locations)         │   │
│  │                                                       │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                              │
│  ┌─────────────────────────────────────────────────────┐   │
│  │         MODYFIKACJE ISTNIEJĄCYCH                     │   │
│  ├─────────────────────────────────────────────────────┤   │
│  │                                                       │   │
│  │  ModUpdateChecker (rozszerzony)                     │   │
│  │  ├─ CheckForModUpdatesAsync() [FULL - bez zmian]   │   │
│  │  └─ CheckForDllUpdatesAsync() [DLL - NOWE]         │   │
│  │                                                       │   │
│  │  ModManager (rozszerzony)                           │   │
│  │  └─ ModifyAsync() - przyjmuje konkretną wersję      │   │
│  │                                                       │   │
│  │  DllModSelectionViewModel (rozszerzony)             │   │
│  │  └─ Pokazuje statusy kompatybilności                │   │
│  │                                                       │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                              │
└────────────────────────┬─────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────┐
│                    NOWE ENDPOINTY API                        │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  GET /susmodder-config-versions?modId={id}                  │
│  └─ Zwraca historię wersji moda                             │
│                                                              │
│  GET /api/compatibility?dllModId={id}&fullModId={id}        │
│  └─ Zwraca status kompatybilności                           │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

---

## 📋 Kluczowe Pliki do Modyfikacji

### Nowe Pliki (do stworzenia)

| Plik | Lokalizacja | Opis |
|------|-------------|------|
| `ModVersionService.cs` | `SUSModder.Core/Services/` | Obsługa historii wersji |
| `CompatibilityService.cs` | `SUSModder.Core/Services/` | Obsługa kompatybilności |
| `DllUpdateManager.cs` | `SUSModder.Core/Services/` | Zarządzanie aktualizacjami DLL |
| `ModVersionHistory.cs` | `SUSModder.Core/Models/` | Model wersji moda |
| `CompatibilityInfo.cs` | `SUSModder.Core/Models/` | Model informacji o kompatybilności |
| `VersionSelectionDialog.axaml` | `SUSModder/Views/` | Dialog wyboru wersji |
| `DllUpdateDialog.axaml` | `SUSModder/Views/` | Dialog aktualizacji DLL |

### Pliki do Modyfikacji

| Plik | Lokalizacja | Zmiany |
|------|-------------|--------|
| `ModUpdateChecker.cs` | `SUSModder.Core/GameIntegration/` | Dodać metodę `CheckForDllUpdatesAsync()` |
| `ModManager.cs` | `SUSModder.Core/GameIntegration/` | Rozszerzyć `ModifyAsync()` o parametr wersji |
| `DllModificationService.cs` | `SUSModder.Core/Services/` | Dodać metodę `GetInstalledLocations()` |
| `DllModSelectionViewModel.cs` | `SUSModder/ViewModels/` | Dodać wyświetlanie statusów kompatybilności |
| `MainWindowViewModel.cs` | `SUSModder/ViewModels/` | Dodać obsługę FAB menu |

---

## 💡 Najważniejsze Zasady Implementacji

### 1. Kompatybilność Wstecz
✅ **ZAWSZE** zachowuj kompatybilność z istniejącym kodem
✅ Nowe funkcje są **dodatkami**, nie zastępują istniejących
✅ Istniejące flow instalacji modów **nie zmienia się**

### 2. Obsługa Błędów
✅ Każde wywołanie API **MUSI** mieć try-catch
✅ Timeout dla requestów HTTP: **10 sekund**
✅ Fallback do pustej listy jeśli API nie odpowiada
✅ Przyjazne komunikaty dla użytkownika

### 3. UX/UI
✅ Przejrzyste komunikaty o błędach
✅ Progressbar przy długich operacjach
✅ Domyślne wartości ułatwiające wybór (np. "wszystkie zaznaczone")
✅ Kolorowe oznaczenia dla statusów

### 4. Performance
✅ Cache'owanie historii wersji (5 minut)
✅ Asynchroniczne wywołania API
✅ Batch update dla wielu lokalizacji DLL

---

## 🎓 Kluczowe Koncepcje

### Wersjonowanie Modów
- **Endpoint zwraca historię** wszystkich wersji moda
- **Sortowanie**: od najnowszych do najstarszych (CreatedAt DESC)
- **Unikalna kombinacja**: (ModId, ModVersion, AmongVersion)
- **Tylko 4 pola** są wersjonowane: ModVersion, AmongVersion, GitHubRepoOrLink, EpicGitHubRepoOrLink

### Kompatybilność Modów
- **Każda kombinacja** (FullMod, DllMod, FullVersion, DllVersion) jest osobnym wpisem
- **Statusy**: F (Favorite), W (Works), NT (Not Tested), NW (Not Work)
- **Historia** kompatybilności jest zachowana - starsza wersja może mieć inny status
- **Wersjonowanie ważne**: Town of Us v5.3.1 + AleLuduMod może działać, ale v5.4.0 już nie

### Aktualizacje DLL
- **Problem**: DLL może być w wielu miejscach
- **Wykrycie**: Przeszukaj wszystkie zainstalowane mody FULL
- **Użytkownik decyduje**: które lokalizacje zaktualizować
- **Atomowość**: Jeśli jedna aktualizacja się nie powiedzie, inne kontynuują

---

## ✅ Checklist Przed Rozpoczęciem

- [ ] Przeczytano całą dokumentację w tym katalogu
- [ ] **🔴 WAŻNE**: Przeczytano **07_INSTALLATION_MAP_SYSTEM.md** i zrozumiano problem
- [ ] Zrozumiano architekturę rozwiązania
- [ ] Przeczytano dokumentację API (../02_API_SPECIFICATION.md)
- [ ] Przeczytano dokumentację Compatibility Matrix (../../COMPATIBILITY_MATRIX/)
- [ ] Zapoznano się z istniejącym kodem SUSModder
- [ ] Skonfigurowano środowisko deweloperskie (.NET 8.0, Avalonia)
- [ ] Dostęp do testowego API (lub mock)

---

## 📞 Wsparcie

### Dokumentacja Powiązana
- **Wersjonowanie API**: `../02_API_SPECIFICATION.md`
- **Compatibility Matrix API**: `../../COMPATIBILITY_MATRIX/02_API_SPECIFICATION.md`
- **Przykłady API**: `../../COMPATIBILITY_MATRIX/CODE_EXAMPLES.md`

### W Razie Pytań
1. Sprawdź FAQ w odpowiednim dokumencie
2. Przeczytaj sekcję "Edge Cases" w dokumencie implementacyjnym
3. Sprawdź przykłady kodu w `CODE_EXAMPLES.md`

---

## 📊 Status Projektu

- **Data utworzenia dokumentacji:** 2025-10-22
- **Status:** 📝 Dokumentacja - Gotowa do implementacji
- **Następny krok:** ⚠️ **NAJPIERW: Installation Map System** (07_INSTALLATION_MAP_SYSTEM.md)
- **Wersja dokumentacji:** 1.1
- **Szacowany czas implementacji:**
  - **Installation Map System**: 11h (1.5 dnia) - 🔴 **PRIORYTET**
  - **Nowe funkcje (wersjonowanie + kompatybilność)**: 32h (4 dni)
  - **RAZEM**: 43h (5.5 dni roboczych)

---

**Ostatnia aktualizacja:** 2025-10-22
**Autor:** Development Team, SUSModder
**Wersja:** 1.0
