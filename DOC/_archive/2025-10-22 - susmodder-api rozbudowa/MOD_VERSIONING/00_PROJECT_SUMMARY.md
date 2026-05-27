# Wersjonowanie Modów - Podsumowanie Projektu

## 📖 Wprowadzenie

System **wersjonowania modów** to rozwiązanie umożliwiające śledzenie historii zmian konfiguracji modów (FULL i DLL) w aplikacji SUSModder, przy **pełnym zachowaniu kompatybilności wstecz**.

### Problem Biznesowy

**Obecny stan:**
- Tabela `config` przechowuje tylko bieżące wersje modów
- Przy każdej edycji wykonywany jest UPDATE - historia zmian zostaje utracona
- Brak możliwości śledzenia, kiedy i jakie linki były aktywne dla danej wersji moda
- Administratorzy nie mogą cofnąć się do poprzednich konfiguracji

**Problemy wynikające z tego:**
- ❌ Brak historii zmian linków GitHub/Epic dla różnych wersji modów
- ❌ Niemożność sprawdzenia, jaka konfiguracja była aktywna w przeszłości
- ❌ Ryzyko utraty danych przy omyłkowych zmianach
- ❌ Brak auditowania zmian w konfiguracji

### Rozwiązanie

Stworzenie systemu wersjonowania, który:
- ✅ Śledzi wszystkie wersje modów (ModVersion + AmongVersion) wraz z linkami
- ✅ Automatycznie tworzy nowy wpis przy zmianie wersji
- ✅ Pozwala na edycję najnowszej wersji (poprawki błędów w linkach)
- ✅ **Zachowuje pełną kompatybilność wstecz** - istniejący endpoint `/susmodder-config` działa bez zmian
- ✅ Dodaje nowy endpoint `/susmodder-config-versions` do przeglądania historii
- ✅ Minimalistyczne podejście - wersjonujemy tylko 4 kluczowe parametry

---

## 🎯 Kluczowe Założenia

### 1. Minimalizm

**Wersjonujemy TYLKO 4 parametry:**
- `ModVersion` - wersja moda (np. "5.3.1", "5.3.1 beta 1")
- `AmongVersion` - wersja Among Us (np. "2024.10.29")
- `GitHubRepoOrLink` - link do GitHub (Steam)
- `EpicGitHubRepoOrLink` - link do GitHub (Epic Games)

**Pozostałe pola (ModName, PngFileName, Description, etc.) NIE są wersjonowane** - pozostają w tabeli `config` i są edytowalne zawsze.

### 2. Logika Zapisu

**Zmiana wersji (ModVersion ALBO AmongVersion):**
```
Zmieniono ModVersion z "5.3.1" na "5.4.0"
→ INSERT do config_versions (nowa wersja)
→ UPDATE w config (aktualizacja bieżącej wersji)
```

**Brak zmiany wersji (poprawka linku):**
```
Wersja nadal "5.3.1", zmieniono tylko GitHubRepoOrLink
→ UPDATE w config (poprawka bieżącej wersji)
→ UPDATE ostatniego wpisu w config_versions (synchronizacja)
```

### 3. Wersje jako Stringi

**Ważne:** Wersje to stringi, NIE liczby. Nie porównujemy numerycznie.

**Przykłady poprawnych wersji:**
- `"5.3.1"`
- `"5.3.1 beta 1"`
- `"1.3.1b3"`
- `"latest"`
- `"2024.10.29"`

**Logika:** Rejestrujemy ZMIANĘ wersji, nie sprawdzamy czy jest wyższa/niższa.

### 4. Kompatybilność Wstecz

**Najważniejsza zasada:** Istniejące aplikacje korzystające z API **NIE wymagają żadnych zmian**.

- ✅ Endpoint `/susmodder-config` działa IDENTYCZNIE jak dotychczas
- ✅ Tabela `config` pozostaje niezmieniona strukturalnie
- ✅ Wszystkie obecne integracje (frontend, Discord boty) działają bez modyfikacji
- ✅ Nowy endpoint `/susmodder-config-versions` jest OPCJONALNY

---

## 🏗️ Architektura Rozwiązania

### Schemat Relacji

```
┌─────────────────────────────────────────┐
│           Tabela: config                │
│                                          │
│  Id (PK) | ModName | ModVersion |       │
│  1       | ToU     | 5.4.0      | ...   │ ← Zawsze najnowsza wersja
│  2       | TOR     | 4.8.0      | ...   │
└────────────┬────────────────────────────┘
             │
             │ FK: ModId → config.Id
             ▼
┌─────────────────────────────────────────────────────────┐
│           Tabela: config_versions                        │
│                                                          │
│  VersionId | ModId | ModVersion | AmongVersion |        │
│  1         | 1     | 5.3.0      | 2024.09.01   | ...   │ ← Historia
│  2         | 1     | 5.3.1      | 2024.10.01   | ...   │
│  3         | 1     | 5.4.0      | 2024.10.29   | ...   │ ← Najnowsza
│  4         | 2     | 4.7.0      | 2024.09.01   | ...   │
│  5         | 2     | 4.8.0      | 2024.10.15   | ...   │
└─────────────────────────────────────────────────────────┘
```

### Przepływ Danych

**1. Użytkownik pobiera listę modów:**
```bash
GET /susmodder-config
→ Zwraca tabelę config z najnowszymi wersjami (bez zmian!)
```

**2. Użytkownik chce zobaczyć historię wersji moda:**
```bash
GET /susmodder-config-versions?modId=1
→ Zwraca wszystkie wersje Town of Us (5.3.0, 5.3.1, 5.4.0)
```

**3. Admin aktualizuje wersję moda:**
```php
// save_mod.php
if (modVersionChanged OR amongVersionChanged) {
    INSERT INTO config_versions (...)
    UPDATE config SET ModVersion = ?, AmongVersion = ?
} else {
    UPDATE config SET GitHubRepoOrLink = ?
    UPDATE config_versions (ostatni wpis)
}
```

---

## 📊 Przykład Użycia

### Scenariusz: Aktualizacja Town of Us

**Stan początkowy:**
```
config:
  Id=1, ModName="Town of Us", ModVersion="5.3.1", AmongVersion="2024.10.01"

config_versions:
  VersionId=2, ModId=1, ModVersion="5.3.1", AmongVersion="2024.10.01",
  GitHubRepoOrLink="https://github.com/tou/v5.3.1"
```

**Krok 1: Admin zmienia wersję z 5.3.1 na 5.4.0**
```
save_mod.php wykrywa zmianę wersji
→ INSERT do config_versions:
    VersionId=3, ModId=1, ModVersion="5.4.0", AmongVersion="2024.10.29",
    GitHubRepoOrLink="https://github.com/tou/v5.4.0"

→ UPDATE w config:
    Id=1, ModVersion="5.4.0", AmongVersion="2024.10.29"
```

**Krok 2: Admin zauważa błąd w linku i poprawia (bez zmiany wersji)**
```
save_mod.php wykrywa BRAK zmiany wersji (nadal 5.4.0)
→ UPDATE w config:
    GitHubRepoOrLink="https://github.com/tou/v5.4.0-fixed"

→ UPDATE w config_versions (VersionId=3):
    GitHubRepoOrLink="https://github.com/tou/v5.4.0-fixed"
```

**Stan końcowy:**
```
config_versions:
  VersionId=1, ModId=1, ModVersion="5.3.0", ... (historia)
  VersionId=2, ModId=1, ModVersion="5.3.1", ... (historia)
  VersionId=3, ModId=1, ModVersion="5.4.0", GitHubRepoOrLink="...fixed" (najnowsza)
```

---

## 🎯 Kluczowe Funkcjonalności

### 1. Automatyczne Wersjonowanie

- System automatycznie wykrywa zmianę wersji i tworzy nowy wpis
- Nie wymaga ręcznego zarządzania wersjami przez admina
- Transparentne dla użytkownika końcowego

### 2. Historia Zmian

- Każda zmiana wersji zachowana w `config_versions`
- Możliwość sprawdzenia, jaka konfiguracja była aktywna w przeszłości
- Audit trail dla celów debugowania i administracyjnych

### 3. Edycja Najnowszej Wersji

- Admin może poprawić błędy (np. zły link) bez tworzenia nowej wersji
- Synchronizacja między `config` i `config_versions`
- Zachowanie spójności danych

### 4. Pełna Kompatybilność Wstecz

- Istniejące aplikacje działają BEZ zmian
- Endpoint `/susmodder-config` zachowuje się identycznie
- Nowa funkcjonalność jest addytywna, nie destruktywna

---

## 📈 Metryki Sukcesu

### Przed Wdrożeniem

- ❌ Brak historii wersji modów
- ❌ Utrata informacji przy każdym UPDATE
- ❌ Niemożność cofnięcia zmian
- ❌ Brak auditowania

### Po Wdrożeniu

- ✅ Pełna historia wersji dla każdego moda
- ✅ Możliwość sprawdzenia konfiguracji z przeszłości
- ✅ Audit trail wszystkich zmian wersji
- ✅ Zachowanie kompatybilności wstecz
- ✅ Minimalistyczne rozwiązanie (tylko 4 pola wersjonowane)

---

## 🗂️ Struktura Dokumentacji

| Plik | Opis | Dla Kogo |
|------|------|----------|
| **[README.md](./README.md)** | Główny dokument wprowadzający | Wszyscy |
| **[00_PROJECT_SUMMARY.md](./00_PROJECT_SUMMARY.md)** | To co właśnie czytasz | Wszyscy |
| **[01_DATABASE_SCHEMA.md](./01_DATABASE_SCHEMA.md)** | Schemat bazy danych, migracje SQL | Backend Dev, DBA |
| **[02_API_SPECIFICATION.md](./02_API_SPECIFICATION.md)** | Specyfikacja endpointów API | Backend Dev, Frontend Dev |
| **[03_MIGRATION_GUIDE.md](./03_MIGRATION_GUIDE.md)** | Przewodnik wdrożenia krok po kroku | DevOps, Admin |

---

## 🚀 Plan Wdrożenia

### Faza 1: Baza Danych (1 dzień)
- ✅ Utworzenie tabeli `config_versions`
- ✅ Migracja obecnych danych jako punkt startowy
- ✅ Utworzenie widoków pomocniczych

### Faza 2: Backend API (2 dni)
- ✅ Modyfikacja `save_mod.php` (logika INSERT vs UPDATE)
- ✅ Nowy endpoint `/susmodder-config-versions`
- ✅ Testy jednostkowe

### Faza 3: Frontend (1 dzień)
- ✅ Modyfikacja `edit_config.html` (informacja o wersjonowaniu)
- ✅ Opcjonalnie: podgląd historii wersji

### Faza 4: Testy i Wdrożenie (1 dzień)
- ✅ Testy kompatybilności wstecz
- ✅ Testy integracyjne
- ✅ Deployment na produkcję

**Razem: ~5 dni roboczych**

---

## 🔗 Powiązane Projekty

- **COMPATIBILITY_MATRIX** - System kompatybilności między modami FULL a DLL (osobny projekt)
- **susmodder-api** - Backend API (tu będzie implementacja)
- **susadmin** - Panel administracyjny (tu będzie UI)

---

## 📅 Status Projektu

- **Data utworzenia:** 2025-10-22
- **Status:** 📝 Dokumentacja - W trakcie tworzenia
- **Wersja dokumentacji:** 1.0
- **Następny krok:** Implementacja schematu bazy danych

---

## ✅ Kluczowe Zasady (TL;DR)

1. **Minimalizm:** Wersjonujemy tylko 4 parametry (ModVersion, AmongVersion, 2x GitHubLink)
2. **Kompatybilność:** `/susmodder-config` działa IDENTYCZNIE jak dotychczas
3. **Automatyka:** Zmiana wersji = INSERT, brak zmiany = UPDATE
4. **Stringi:** Wersje to stringi ("5.3.1 beta 1"), nie porównujemy numerycznie
5. **Historia:** Wszystkie wersje zachowane w `config_versions`
6. **Edycja:** Z panelu admina można edytować tylko najnowszą wersję

---

**Ostatnia aktualizacja:** 2025-10-22
**Autor:** SysAdmin Team, susmodder.app
