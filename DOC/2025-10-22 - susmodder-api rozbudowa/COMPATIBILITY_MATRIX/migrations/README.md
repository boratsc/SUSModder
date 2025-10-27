# Database Migrations - Compatibility Matrix

## 📋 Przegląd

Ten folder zawiera migracje SQL dla systemu Compatibility Matrix.

## 📁 Pliki

| Plik | Opis |
|------|------|
| `001_create_compatibility_matrix.sql` | Tworzy tabele i widoki |
| `002_populate_initial_data.sql` | Wypełnia początkowe dane |
| `run-migrations.sh` | Skrypt do uruchamiania migracji |

## 🚀 Użycie

### Uruchomienie Wszystkich Migracji

```bash
cd /srv/synapsekit-boracik/migrations
./run-migrations.sh all
```

### Uruchomienie Konkretnej Migracji

```bash
./run-migrations.sh 1    # Uruchomi migrację 001
./run-migrations.sh 2    # Uruchomi migrację 002
```

### Lista Dostępnych Migracji

```bash
./run-migrations.sh list
```

### Rollback (Przywrócenie z Backupu)

```bash
./run-migrations.sh rollback
```

## 🔒 Bezpieczeństwo

- ✅ Automatyczne backupy przed każdą migracją
- ✅ Backupy są kompresowane (gzip)
- ✅ Możliwość rollback w razie problemów
- ✅ Idempotentne migracje (można uruchomić wielokrotnie)

## 📊 Co Robi Każda Migracja?

### Migration 001: Create Compatibility Matrix

**Tworzy:**
- Tabela `compatibility_matrix`
- Widok `vw_current_compatibility`
- Widok `vw_compatibility_matrix_full`
- Indeksy dla wydajności
- Foreign keys do tabeli `config`

**Czas wykonania:** ~1 sekunda

### Migration 002: Populate Initial Data

**Tworzy:**
- Wpisy NT (Not Tested) dla wszystkich kombinacji modów
- Import znanych kompatybilności (legacy data)
- Statystyki początkowe

**Czas wykonania:** ~2-5 sekund (zależnie od liczby modów)

## 🧪 Testowanie Migracji

### Przed Produkcją

Zalecane jest przetestowanie na kopii bazy:

```bash
# 1. Stwórz kopię bazy danych
docker exec nginx-mysql mysqldump \
  -h 193.70.42.86 -u susfuckr -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr > susfuckr_backup.sql

# 2. Stwórz testową bazę
docker exec nginx-mysql mysql \
  -h 193.70.42.86 -u susfuckr -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  -e "CREATE DATABASE susfuckr_test"

# 3. Import danych
docker exec -i nginx-mysql mysql \
  -h 193.70.42.86 -u susfuckr -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr_test < susfuckr_backup.sql

# 4. Uruchom migracje na testowej bazie (zmień DB_NAME w skrypcie)
```

### Weryfikacja Po Migracji

```bash
# Sprawdź czy tabele zostały utworzone
docker exec nginx-mysql mysql \
  -h 193.70.42.86 -u susfuckr -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr -e "SHOW TABLES LIKE 'compatibility%'"

# Sprawdź liczbę wpisów
docker exec nginx-mysql mysql \
  -h 193.70.42.86 -u susfuckr -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr -e "SELECT COUNT(*) FROM compatibility_matrix"

# Sprawdź widoki
docker exec nginx-mysql mysql \
  -h 193.70.42.86 -u susfuckr -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr -e "SELECT COUNT(*) FROM vw_current_compatibility"
```

## 🔄 Rollback Instrukcje

### Automatyczny Rollback

```bash
./run-migrations.sh rollback
```

### Manualny Rollback

```bash
# 1. Znajdź ostatni backup
ls -lt /srv/synapsekit-boracik/backups/

# 2. Przywróć z backupu
gunzip -c /srv/synapsekit-boracik/backups/susfuckr_YYYYMMDD_HHMMSS.sql.gz | \
docker exec -i nginx-mysql mysql \
  -h 193.70.42.86 -u susfuckr -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr
```

### SQL Rollback (dla konkretnych migracji)

**Rollback Migration 002:**
```sql
DELETE FROM compatibility_matrix WHERE CreatedBy = 'migration_002';
```

**Rollback Migration 001:**
```sql
DROP VIEW IF EXISTS vw_compatibility_matrix_full;
DROP VIEW IF EXISTS vw_current_compatibility;
DROP TABLE IF EXISTS compatibility_matrix;
```

## 📝 Tworzenie Nowej Migracji

### Konwencja Nazewnictwa

```
[NUMBER]_[DESCRIPTION].sql

Przykłady:
001_create_compatibility_matrix.sql
002_populate_initial_data.sql
003_add_history_table.sql
```

### Template Migracji

```sql
-- ============================================================================
-- Migration: [Description]
-- Version: [NUMBER]
-- Date: [YYYY-MM-DD]
-- Author: [Your Name]
-- Description: 
--   - [What this migration does]
-- ============================================================================

USE susfuckr;

-- Your SQL here

-- ============================================================================
-- Verification Queries
-- ============================================================================

SELECT 'Migration [NUMBER] completed' AS Status;

-- ============================================================================
-- Rollback Instructions
-- ============================================================================
-- To rollback:
-- [SQL commands to rollback]
```

## ⚠️ Ważne Zasady

### DO:
- ✅ Zawsze twórz backup przed migracją
- ✅ Testuj na kopii bazy przed produkcją
- ✅ Używaj transakcji gdzie to możliwe
- ✅ Dodawaj komentarze do złożonych operacji
- ✅ Dokumentuj rollback instructions
- ✅ Używaj idempotentnych operacji (IF EXISTS, INSERT IGNORE, etc.)

### DON'T:
- ❌ Nie uruchamiaj migracji bez backupu
- ❌ Nie modyfikuj istniejących migracji (twórz nowe)
- ❌ Nie usuwaj starych plików migracji
- ❌ Nie pomijaj numeracji migracji
- ❌ Nie używaj DROP bez IF EXISTS

## 🐛 Troubleshooting

### Problem: "Migration failed"

**Rozwiązanie:**
1. Sprawdź logi błędów
2. Przywróć z backupu: `./run-migrations.sh rollback`
3. Popraw SQL w pliku migracji
4. Uruchom ponownie

### Problem: "Database connection failed"

**Rozwiązanie:**
```bash
# Sprawdź czy MySQL działa
docker ps | grep mysql

# Sprawdź credentials w skrypcie
grep "DB_" run-migrations.sh

# Test połączenia
docker exec nginx-mysql mysql \
  -h 193.70.42.86 -u susfuckr -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  -e "SELECT 1"
```

### Problem: "Backup creation failed"

**Rozwiązanie:**
```bash
# Sprawdź uprawnienia
ls -la /srv/synapsekit-boracik/backups/

# Utwórz folder jeśli nie istnieje
mkdir -p /srv/synapsekit-boracik/backups
chmod 755 /srv/synapsekit-boracik/backups
```

## 📊 Statystyki Migracji

Po uruchomieniu wszystkich migracji:

```sql
-- Sprawdź statystyki
SELECT 
    'Total Combinations' AS Metric,
    COUNT(*) AS Count
FROM compatibility_matrix;

SELECT 
    CompatibilityStatus,
    COUNT(*) AS Count
FROM compatibility_matrix
GROUP BY CompatibilityStatus;
```

Oczekiwane wyniki (dla ~10 FULL × ~12 DLL):
- Total: ~120 wpisów
- NT (Not Tested): ~110-115
- F/W/NW: ~5-10 (z legacy import)

## 🔗 Powiązane Dokumenty

- [DATABASE_DESIGN.md](../DOC/COMPATIBILITY_MATRIX/01_DATABASE_DESIGN.md)
- [MIGRATION_PLAN.md](../DOC/COMPATIBILITY_MATRIX/05_MIGRATION_PLAN.md)

## 📞 Pomoc

W razie problemów:
1. Sprawdź dokumentację: `../DOC/COMPATIBILITY_MATRIX/`
2. Sprawdź logi: `docker logs nginx-mysql`
3. Sprawdź backupy: `ls -la backups/`
4. Kontakt z DevOps team

---

**Ostatnia aktualizacja:** 2025-10-22  
**Wersja:** 1.0
