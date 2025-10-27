# Wersjonowanie Modów - Dokumentacja

## 🎯 Szybki Start

**Cel projektu:** Dodanie systemu wersjonowania modów (FULL i DLL) w SUSModder API z zachowaniem pełnej kompatybilności wstecz.

**Status:** 📝 Dokumentacja - Gotowa do implementacji
**Data utworzenia:** 2025-10-22

---

## 📚 Struktura Dokumentacji

### 📄 Główne Dokumenty

| Plik | Opis | Czas czytania | Dla Kogo |
|------|------|---------------|----------|
| **[README.md](./README.md)** | Ten dokument - wprowadzenie i nawigacja | 5 min | Wszyscy |
| **[00_PROJECT_SUMMARY.md](./00_PROJECT_SUMMARY.md)** | Podsumowanie projektu, cele, założenia | 15 min | PM, Admin, Dev |
| **[01_DATABASE_SCHEMA.md](./01_DATABASE_SCHEMA.md)** | Schemat bazy danych, migracje SQL | 20 min | Backend Dev, DBA |
| **[02_API_SPECIFICATION.md](./02_API_SPECIFICATION.md)** | Specyfikacja API, endpointy, przykłady | 25 min | Backend Dev, Frontend Dev |
| **[03_MIGRATION_GUIDE.md](./03_MIGRATION_GUIDE.md)** | Przewodnik wdrożenia krok po kroku | 30 min | DevOps, Admin |

---

## 🤔 Jak Czytać Dokumentację?

### Jesteś Project Managerem / Adminem?

1. Zacznij od **README.md** (to co właśnie czytasz) ✅
2. Przeczytaj **00_PROJECT_SUMMARY.md** - zrozumiesz biznesowy problem i rozwiązanie
3. Przejrzyj **03_MIGRATION_GUIDE.md** - poznasz plan wdrożenia i timeline

**Czas:** ~30 minut

### Jesteś Backend Developerem?

1. Przeczytaj **README.md** ✅
2. Przeczytaj **00_PROJECT_SUMMARY.md** - zrozumiesz założenia
3. **01_DATABASE_SCHEMA.md** - poznasz strukturę bazy danych
4. **02_API_SPECIFICATION.md** - zaimplementujesz endpointy i logikę
5. **03_MIGRATION_GUIDE.md** - wdrożysz na produkcję

**Czas:** ~90 minut

### Jesteś Frontend Developerem?

1. Przeczytaj **README.md** ✅
2. Przeczytaj **00_PROJECT_SUMMARY.md** - zrozumiesz koncepcję
3. **02_API_SPECIFICATION.md** (sekcja "Przykłady Użycia API") - nauczysz się korzystać z API

**Czas:** ~30 minut

### Jesteś DevOps / DBA?

1. Przeczytaj **README.md** ✅
2. Przejrzyj **01_DATABASE_SCHEMA.md** - przygotuj migrację
3. **03_MIGRATION_GUIDE.md** - wykonaj deployment

**Czas:** ~60 minut

---

## 🎯 Najważniejsze Informacje (TL;DR)

### Problem

- Obecny system **nadpisuje** dane przy każdej edycji (UPDATE)
- Brak historii zmian wersji modów
- Niemożność cofnięcia się do poprzedniej konfiguracji

### Rozwiązanie

- **Nowa tabela `config_versions`** - przechowuje wszystkie wersje modów
- **Tabela `config`** - nadal zawiera najnowsze wersje (BEZ ZMIAN!)
- **Automatyczne wersjonowanie** - zmiana wersji = nowy wpis, brak zmiany = UPDATE
- **Pełna kompatybilność wstecz** - endpoint `/susmodder-config` działa identycznie

### Wersjonowane Parametry (TYLKO 4)

1. `ModVersion` - wersja moda (np. "5.3.1", "latest")
2. `AmongVersion` - wersja Among Us (np. "2024.10.29")
3. `GitHubRepoOrLink` - link GitHub (Steam)
4. `EpicGitHubRepoOrLink` - link GitHub (Epic Games)

**Pozostałe pola NIE są wersjonowane** (minimalizm!)

### Endpointy API

| Endpoint | Metoda | Opis | Status |
|----------|--------|------|--------|
| `/susmodder-config` | GET | Najnowsze wersje modów | ✅ BEZ ZMIAN |
| `/susmodder-config-versions` | GET | Historia wersji (opcjonalnie filtrowana) | 🆕 NOWY |

### Logika Zapisu

```
Jeśli zmieniono ModVersion ALBO AmongVersion:
  → INSERT do config_versions (nowa wersja)
  → UPDATE w config (aktualizacja najnowszej)

Jeśli NIE zmieniono wersji (tylko link):
  → UPDATE w config (poprawka)
  → UPDATE ostatniego wpisu w config_versions (synchronizacja)
```

---

## 📊 Schemat Architektury

```
┌─────────────────────────────────────────────────┐
│         Użytkownik (Frontend / Bot)              │
└────────────────────┬────────────────────────────┘
                     │
          ┌──────────┴──────────┐
          ▼                     ▼
┌──────────────────┐   ┌──────────────────────────┐
│ /susmodder-config│   │ /susmodder-config-versions│
│   (BEZ ZMIAN!)   │   │         (NOWY)            │
└────────┬─────────┘   └────────┬─────────────────┘
         │                      │
         ▼                      ▼
┌─────────────────────────────────────────────────┐
│           MySQL Database (susfuckr)              │
├─────────────────────────────────────────────────┤
│                                                  │
│  ┌─────────────────────────────────────────┐   │
│  │  Tabela: config (ISTNIEJĄCA)             │   │
│  │  Zawiera: Najnowsze wersje modów         │   │
│  │  Endpoint: /susmodder-config             │   │
│  └────────────┬────────────────────────────┘   │
│               │ FK: ModId                       │
│               ▼                                 │
│  ┌─────────────────────────────────────────┐   │
│  │  Tabela: config_versions (NOWA)          │   │
│  │  Zawiera: Historia wszystkich wersji     │   │
│  │  Pola: VersionId, ModId, ModVersion,     │   │
│  │         AmongVersion, GitHubRepoOrLink,  │   │
│  │         EpicGitHubRepoOrLink, CreatedAt  │   │
│  │  Endpoint: /susmodder-config-versions    │   │
│  └─────────────────────────────────────────┘   │
│                                                  │
└─────────────────────────────────────────────────┘
```

---

## 🚀 Przykład Użycia

### Scenariusz: Aktualizacja Town of Us

**Stan początkowy:**
```
config:
  Id=1, ModName="Town of Us", ModVersion="5.3.1"

config_versions:
  VersionId=1, ModId=1, ModVersion="5.3.1"
```

**Krok 1: Admin zmienia wersję na 5.4.0**
```
save_mod.php wykrywa: 5.3.1 !== 5.4.0 → versionChanged = true

→ INSERT do config_versions:
    VersionId=2, ModId=1, ModVersion="5.4.0", ...

→ UPDATE w config:
    ModVersion="5.4.0"
```

**Stan po zmianie:**
```
config:
  Id=1, ModName="Town of Us", ModVersion="5.4.0"

config_versions:
  VersionId=1, ModId=1, ModVersion="5.3.1" (historia)
  VersionId=2, ModId=1, ModVersion="5.4.0" (najnowsza)
```

**Użytkownik sprawdza historię:**
```bash
curl "https://api.susmodder.app/susmodder-config-versions?modId=1"
```

**Response:**
```json
{
  "success": true,
  "modId": 1,
  "count": 2,
  "versions": [
    {"VersionId": 2, "ModVersion": "5.4.0", "CreatedAt": "2024-10-29T09:15:00Z"},
    {"VersionId": 1, "ModVersion": "5.3.1", "CreatedAt": "2024-10-01T14:30:00Z"}
  ]
}
```

---

## ✅ Zalety Rozwiązania

### 1. Pełna Kompatybilność Wstecz

- ✅ Endpoint `/susmodder-config` działa IDENTYCZNIE
- ✅ Istniejące aplikacje (frontend, boty) nie wymagają zmian
- ✅ Zero downtime podczas wdrożenia
- ✅ Tabela `config` zachowuje strukturę

### 2. Minimalizm

- ✅ Wersjonujemy TYLKO 4 parametry
- ✅ Prosta struktura bazy danych
- ✅ Mały rozmiar tabeli (~25 KB dla 100 wersji)
- ✅ Szybkie zapytania (indeksy)

### 3. Automatyka

- ✅ System automatycznie wykrywa zmianę wersji
- ✅ Nie wymaga ręcznego zarządzania przez admina
- ✅ Transparentne dla użytkownika końcowego

### 4. Historia i Audit

- ✅ Zachowanie historii wszystkich wersji
- ✅ Możliwość sprawdzenia konfiguracji z przeszłości
- ✅ Audit trail dla celów debugowania

---

## 🎯 Kluczowe Założenia

### 1. Minimalizm

Wersjonujemy **TYLKO 4 parametry**:
- `ModVersion`
- `AmongVersion`
- `GitHubRepoOrLink`
- `EpicGitHubRepoOrLink`

Pozostałe pola (ModName, PngFileName, Description, etc.) **NIE są wersjonowane**.

### 2. Wersje jako Stringi

**Ważne:** Wersje to stringi, NIE liczby. Nie porównujemy numerycznie.

**Przykłady poprawnych wersji:**
- `"5.3.1"`
- `"5.3.1 beta 1"`
- `"1.3.1b3"`
- `"latest"`

**Logika:** Rejestrujemy ZMIANĘ wersji, nie sprawdzamy czy jest wyższa/niższa.

### 3. Kompatybilność Wstecz

**Najważniejsza zasada:** Istniejące aplikacje **NIE wymagają żadnych zmian**.

- ✅ Endpoint `/susmodder-config` zachowuje się identycznie
- ✅ Tabela `config` pozostaje niezmieniona strukturalnie
- ✅ Nowy endpoint `/susmodder-config-versions` jest opcjonalny

### 4. Edycja Najnowszej Wersji

- Z poziomu panelu admina można edytować **tylko najnowszą wersję**
- Jeśli wersja nie jest zmieniana → UPDATE (poprawka błędu)
- Jeśli wersja jest zmieniana → INSERT (nowa wersja)

---

## 📅 Timeline Implementacji

| Faza | Czas | Opis | Downtime |
|------|------|------|----------|
| **Faza 0** | 1h | Przygotowanie i backup | ❌ Nie |
| **Faza 1** | 2h | Migracja bazy danych | ❌ Nie |
| **Faza 2** | 4h | Implementacja Backend API | ❌ Nie |
| **Faza 3** | 2h | Modyfikacja save_mod.php | ❌ Nie |
| **Faza 4** | 2h | Modyfikacja Frontend (opcjonalne) | ❌ Nie |
| **Faza 5** | 2h | Testy i weryfikacja | ❌ Nie |
| **Faza 6** | 1h | Deployment produkcyjny | ❌ Nie |
| **RAZEM** | **~14h** | **Rozłożone na 5 dni** | **0 minut** |

---

## 🔗 Quick Links

### Dla Developerów

- **Schemat bazy:** [01_DATABASE_SCHEMA.md](./01_DATABASE_SCHEMA.md)
- **Specyfikacja API:** [02_API_SPECIFICATION.md](./02_API_SPECIFICATION.md)
- **Przewodnik wdrożenia:** [03_MIGRATION_GUIDE.md](./03_MIGRATION_GUIDE.md)

### Dla Adminów

- **Podsumowanie:** [00_PROJECT_SUMMARY.md](./00_PROJECT_SUMMARY.md)
- **Plan wdrożenia:** [03_MIGRATION_GUIDE.md](./03_MIGRATION_GUIDE.md)

### Powiązane Projekty

- **COMPATIBILITY_MATRIX:** System kompatybilności między modami FULL a DLL (osobny projekt)
- **susmodder-api:** Backend API (lokalizacja implementacji)
- **susadmin:** Panel administracyjny (lokalizacja UI)

---

## 📝 Przykłady API

### GET - Najnowsze Wersje Modów (BEZ ZMIAN!)

```bash
curl https://api.susmodder.app/susmodder-config
```

**Response:**
```json
[
  {
    "Id": 1,
    "ModName": "Town of Us",
    "ModVersion": "5.4.0",
    "AmongVersion": "2024.10.29",
    "GitHubRepoOrLink": "https://github.com/tou/v5.4.0",
    ...
  }
]
```

### GET - Historia Wersji (NOWY!)

```bash
# Wszystkie wersje
curl https://api.susmodder.app/susmodder-config-versions

# Wersje konkretnego moda
curl "https://api.susmodder.app/susmodder-config-versions?modId=1"
```

**Response:**
```json
{
  "success": true,
  "modId": 1,
  "count": 3,
  "versions": [
    {
      "VersionId": 3,
      "ModId": 1,
      "ModVersion": "5.4.0",
      "AmongVersion": "2024.10.29",
      "GitHubRepoOrLink": "https://github.com/tou/v5.4.0",
      "CreatedAt": "2024-10-29T09:15:00.000Z"
    },
    {
      "VersionId": 2,
      "ModId": 1,
      "ModVersion": "5.3.1",
      "AmongVersion": "2024.10.01",
      "GitHubRepoOrLink": "https://github.com/tou/v5.3.1",
      "CreatedAt": "2024-10-01T14:30:00.000Z"
    },
    {
      "VersionId": 1,
      "ModId": 1,
      "ModVersion": "5.3.0",
      "AmongVersion": "2024.09.01",
      "GitHubRepoOrLink": "https://github.com/tou/v5.3.0",
      "CreatedAt": "2024-09-01T10:00:00.000Z"
    }
  ]
}
```

---

## ❓ FAQ

### Czy muszę zmienić coś w istniejącej aplikacji?

**Nie!** Jeśli korzystasz tylko z `/susmodder-config`, wszystko działa bez zmian.

### Czy dane zostaną utracone?

**Nie!** Wszystkie obecne dane zostaną zaimportowane do `config_versions` jako punkt startowy.

### Czy mogę cofnąć się do poprzedniej wersji?

**Tak!** Historia jest zachowana w `config_versions`. Możesz sprawdzić poprzednie linki i wersje.

### Czy mogę edytować starsze wersje?

**Nie z panelu admina.** Z poziomu panelu można edytować tylko najnowszą wersję. Starsze wersje są readonly (historia).

### Co jeśli chcę usunąć mod?

Usunięcie moda z `config` automatycznie usuwa wszystkie jego wersje z `config_versions` (CASCADE DELETE).

### Jak działa wersjonowanie dla modów DLL z "latest"?

Tak samo jak dla innych. Jeśli zmienisz "latest" na konkretną wersję (np. "1.2.3"), zostanie utworzony nowy wpis.

---

## 📊 Status Projektu

- **Data utworzenia dokumentacji:** 2025-10-22
- **Status:** 📝 Dokumentacja - Gotowa do implementacji
- **Następny krok:** Implementacja Fazy 1 (migracja bazy danych)
- **Wersja dokumentacji:** 1.0

---

## 🎓 Kluczowe Zasady (Podsumowanie)

1. **Minimalizm:** Wersjonujemy tylko 4 parametry
2. **Kompatybilność:** `/susmodder-config` działa IDENTYCZNIE jak dotychczas
3. **Automatyka:** Zmiana wersji = INSERT, brak zmiany = UPDATE
4. **Stringi:** Wersje to stringi, nie porównujemy numerycznie
5. **Historia:** Wszystkie wersje zachowane w `config_versions`
6. **Edycja:** Z panelu admina można edytować tylko najnowszą wersję
7. **Zero downtime:** Wdrożenie bez przerwy w działaniu aplikacji

---

## 📞 Kontakt i Wsparcie

### W Razie Pytań

1. Przeczytaj dokumentację w tym katalogu
2. Sprawdź FAQ powyżej
3. Przejrzyj przykłady w `02_API_SPECIFICATION.md`

### Zgłaszanie Problemów

- Sprawdź logi: `docker logs nginx-api-susmodder`
- Sprawdź status bazy: `SHOW TABLES LIKE 'config%'`
- Sprawdź widok: `SELECT * FROM vw_config_with_version_count`

---

**Ostatnia aktualizacja:** 2025-10-22
**Autor:** SysAdmin Team, susmodder.app
**Wersja dokumentacji:** 1.0
