# Przewodnik Wdrożenia - Wersjonowanie Modów

## 📖 Wprowadzenie

Ten dokument opisuje **krok po kroku** proces wdrożenia systemu wersjonowania modów w środowisku produkcyjnym. Plan zakłada minimalizację downtime i pełne zachowanie kompatybilności wstecz.

**Szacowany czas:** ~5 dni roboczych
**Downtime:** 0 minut (wdrożenie hot-deploy)

---

## 🎯 Przegląd Planu

### Fazy Wdrożenia

| Faza | Czas | Opis | Downtime |
|------|------|------|----------|
| **Faza 0** | 1h | Przygotowanie i backup | ❌ Nie |
| **Faza 1** | 2h | Migracja bazy danych | ❌ Nie |
| **Faza 2** | 4h | Implementacja Backend API | ❌ Nie |
| **Faza 3** | 2h | Modyfikacja save_mod.php | ❌ Nie |
| **Faza 4** | 2h | Modyfikacja Frontend (opcjonalne) | ❌ Nie |
| **Faza 5** | 2h | Testy i weryfikacja | ❌ Nie |
| **Faza 6** | 1h | Deployment produkcyjny | ❌ Nie |

**Razem:** ~14 godzin (rozłożone na 5 dni)

---

## 🔧 Faza 0: Przygotowanie i Backup

### Checklist

- [ ] Backup bazy danych `susfuckr`
- [ ] Backup plików `save_mod.php`, `routes/config.js`
- [ ] Weryfikacja środowiska dev/prod
- [ ] Przygotowanie rollback plan

### Krok 1: Backup Bazy Danych

```bash
# Połącz się z serwerem
ssh user@susmodder.app

# Utwórz katalog na backupy
mkdir -p ~/backups/mod_versioning_$(date +%Y%m%d)
cd ~/backups/mod_versioning_$(date +%Y%m%d)

# Backup całej bazy susfuckr
docker exec nginx-mysql mysqldump \
  -h 193.70.42.86 \
  -u susfuckr \
  -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr > susfuckr_backup_$(date +%Y%m%d_%H%M%S).sql

# Backup tylko tabeli config
docker exec nginx-mysql mysqldump \
  -h 193.70.42.86 \
  -u susfuckr \
  -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr config > config_table_backup_$(date +%Y%m%d_%H%M%S).sql

# Weryfikacja backupu
ls -lh
# Powinieneś zobaczyć 2 pliki .sql
```

### Krok 2: Backup Plików

```bash
# Backup PHP
cp /srv/synapsekit-boracik/nginx/html/susmodder/susadmin/save_mod.php \
   ~/backups/mod_versioning_$(date +%Y%m%d)/save_mod.php.backup

# Backup Node.js routes
cp /srv/synapsekit-boracik/susmodder-api/routes/config.js \
   ~/backups/mod_versioning_$(date +%Y%m%d)/config.js.backup

# Weryfikacja
ls -lh ~/backups/mod_versioning_$(date +%Y%m%d)/
```

### Krok 3: Przygotowanie Rollback Plan

**W razie problemów:**
```bash
# Rollback bazy danych
docker exec -i nginx-mysql mysql \
  -h 193.70.42.86 \
  -u susfuckr \
  -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr < ~/backups/mod_versioning_YYYYMMDD/susfuckr_backup_*.sql

# Rollback plików
cp ~/backups/mod_versioning_YYYYMMDD/save_mod.php.backup \
   /srv/synapsekit-boracik/nginx/html/susmodder/susadmin/save_mod.php

cp ~/backups/mod_versioning_YYYYMMDD/config.js.backup \
   /srv/synapsekit-boracik/susmodder-api/routes/config.js

# Restart API
docker restart nginx-api-susmodder
```

---

## 🗄️ Faza 1: Migracja Bazy Danych

### Checklist

- [ ] Utworzenie tabeli `config_versions`
- [ ] Import obecnych danych jako seed
- [ ] Utworzenie widoku `vw_config_with_version_count`
- [ ] Weryfikacja integralności

### Krok 1: Przygotowanie Skryptu SQL

**Utwórz plik:** `001_create_config_versions.sql`

```sql
-- ============================================================================
-- Migration: Create config_versions Table
-- Version: 001
-- Date: 2025-10-22
-- Description: Tworzy tabelę config_versions do wersjonowania modów
-- ============================================================================

USE susfuckr;

-- ============================================================================
-- Tabela: config_versions
-- ============================================================================

CREATE TABLE IF NOT EXISTS config_versions (
    -- Klucz główny
    VersionId INT AUTO_INCREMENT PRIMARY KEY,

    -- Klucz obcy do config
    ModId INT NOT NULL COMMENT 'FK to config.Id',

    -- 4 wersjonowane parametry (minimalizm)
    ModVersion VARCHAR(50) NOT NULL COMMENT 'Wersja moda',
    AmongVersion VARCHAR(50) NOT NULL COMMENT 'Wersja Among Us',
    GitHubRepoOrLink VARCHAR(255) COMMENT 'Link GitHub (Steam)',
    EpicGitHubRepoOrLink VARCHAR(255) COMMENT 'Link GitHub (Epic)',

    -- Metadata
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Data utworzenia',
    CreatedBy VARCHAR(100) COMMENT 'Kto utworzył',
    Notes TEXT COMMENT 'Notatki o wersji',

    -- Foreign Key Constraint
    CONSTRAINT fk_config_versions_mod
        FOREIGN KEY (ModId) REFERENCES config(Id)
        ON DELETE CASCADE,

    -- Indeksy
    INDEX idx_mod_id (ModId),
    INDEX idx_created_at (CreatedAt),
    INDEX idx_mod_version (ModId, ModVersion),

    -- Unikalność
    UNIQUE KEY unique_mod_version (ModId, ModVersion, AmongVersion)

) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_unicode_ci
  COMMENT='Historia wersji modów';

-- ============================================================================
-- Import Obecnych Danych jako Seed
-- ============================================================================

INSERT INTO config_versions (ModId, ModVersion, AmongVersion, GitHubRepoOrLink, EpicGitHubRepoOrLink, CreatedAt, Notes)
SELECT
    Id AS ModId,
    ModVersion,
    AmongVersion,
    GitHubRepoOrLink,
    EpicGitHubRepoOrLink,
    COALESCE(LastUpdated, NOW()) AS CreatedAt,
    'Initial version imported from config table' AS Notes
FROM config;

-- ============================================================================
-- Widok: vw_config_with_version_count
-- ============================================================================

CREATE OR REPLACE VIEW vw_config_with_version_count AS
SELECT
    c.Id,
    c.ModName,
    c.ModType,
    c.ModVersion AS CurrentVersion,
    c.AmongVersion AS CurrentAmongVersion,
    c.GitHubRepoOrLink AS CurrentGitHubLink,
    c.EpicGitHubRepoOrLink AS CurrentEpicLink,
    COALESCE(v.VersionCount, 0) AS TotalVersions,
    v.FirstVersionDate,
    v.LastVersionDate
FROM config c
LEFT JOIN (
    SELECT
        ModId,
        COUNT(*) AS VersionCount,
        MIN(CreatedAt) AS FirstVersionDate,
        MAX(CreatedAt) AS LastVersionDate
    FROM config_versions
    GROUP BY ModId
) v ON c.Id = v.ModId;

-- ============================================================================
-- Weryfikacja
-- ============================================================================

-- Sprawdź utworzenie tabeli
SELECT 'Table created' AS Status, COUNT(*) AS RowCount FROM config_versions;

-- Sprawdź utworzenie widoku
SELECT 'View created' AS Status, COUNT(*) AS Rows FROM vw_config_with_version_count;

-- Sprawdź strukturę
DESCRIBE config_versions;

-- Sprawdź indeksy
SHOW INDEX FROM config_versions;

-- ============================================================================
-- Rollback Instructions
-- ============================================================================
-- W razie potrzeby rollback:
-- DROP VIEW IF EXISTS vw_config_with_version_count;
-- DROP TABLE IF EXISTS config_versions;

SELECT '✅ Migration 001 completed!' AS Message, NOW() AS CompletedAt;
```

### Krok 2: Wykonanie Migracji

```bash
# Skopiuj plik SQL na serwer
scp 001_create_config_versions.sql user@susmodder.app:~/

# Połącz się z serwerem
ssh user@susmodder.app

# Wykonaj migrację
docker exec -i nginx-mysql mysql \
  -h 193.70.42.86 \
  -u susfuckr \
  -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr < ~/001_create_config_versions.sql
```

**Oczekiwany output:**
```
Status         | RowCount
---------------|----------
Table created  | 6

Status        | Rows
--------------|------
View created  | 6

Message                        | CompletedAt
-------------------------------|-------------------
✅ Migration 001 completed!   | 2025-10-22 14:30:00
```

### Krok 3: Weryfikacja

```bash
# Sprawdź tabelę config_versions
docker exec nginx-mysql mysql \
  -h 193.70.42.86 \
  -u susfuckr \
  -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr -e "SELECT VersionId, ModId, ModVersion, AmongVersion FROM config_versions;"

# Sprawdź widok
docker exec nginx-mysql mysql \
  -h 193.70.42.86 \
  -u susfuckr \
  -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr -e "SELECT * FROM vw_config_with_version_count;"
```

**Oczekiwany wynik:**
```
VersionId | ModId | ModVersion       | AmongVersion
----------|-------|------------------|-------------
1         | 1     | 5.4.0            | 2024.10.29
2         | 2     | 2.0.0            | 2024.10.01
3         | 4     | 4.8.0            | 2024.09.15
4         | 5     | latest           | 2024.10.29
5         | 8     | 1.2.3            | 2024.10.01
6         | 9     | Custom Beta 0.20.4 | 2024.09.01
```

---

## 🔌 Faza 2: Implementacja Backend API

### Checklist

- [ ] Dodanie endpointu `/susmodder-config-versions` do `routes/config.js`
- [ ] Aktualizacja dokumentacji Swagger
- [ ] Test endpointu

### Krok 1: Modyfikacja `routes/config.js`

**Lokalizacja:** `/srv/synapsekit-boracik/susmodder-api/routes/config.js`

**Dodaj na końcu pliku (przed `module.exports = router;`):**

```javascript
/**
 * @swagger
 * /susmodder-config-versions:
 *   get:
 *     summary: Pobierz historię wersji modów
 *     tags: [Config]
 *     parameters:
 *       - in: query
 *         name: modId
 *         schema:
 *           type: integer
 *         required: false
 *         description: ID moda (opcjonalne)
 *     responses:
 *       200:
 *         description: Historia wersji modów
 *       500:
 *         description: Database error
 */
router.get('/susmodder-config-versions', async (req, res) => {
  let connection;
  try {
    connection = await mysql.createConnection(dbConfig);

    const modId = req.query.modId;

    let query;
    let params = [];

    if (modId) {
      query = `
        SELECT
          VersionId, ModId, ModVersion, AmongVersion,
          GitHubRepoOrLink, EpicGitHubRepoOrLink,
          CreatedAt, CreatedBy, Notes
        FROM config_versions
        WHERE ModId = ?
        ORDER BY CreatedAt DESC
      `;
      params = [parseInt(modId, 10)];
    } else {
      query = `
        SELECT
          VersionId, ModId, ModVersion, AmongVersion,
          GitHubRepoOrLink, EpicGitHubRepoOrLink,
          CreatedAt, CreatedBy, Notes
        FROM config_versions
        ORDER BY CreatedAt DESC
      `;
    }

    const [rows] = await connection.execute(query, params);

    const data = rows.map(row => ({
      VersionId: parseInt(row.VersionId, 10),
      ModId: parseInt(row.ModId, 10),
      ModVersion: row.ModVersion,
      AmongVersion: row.AmongVersion,
      GitHubRepoOrLink: row.GitHubRepoOrLink,
      EpicGitHubRepoOrLink: row.EpicGitHubRepoOrLink,
      CreatedAt: row.CreatedAt,
      CreatedBy: row.CreatedBy,
      Notes: row.Notes
    }));

    const response = {
      success: true,
      count: data.length,
      versions: data
    };

    if (modId) {
      response.modId = parseInt(modId, 10);
    }

    res.setHeader('Content-Type', 'application/json');
    res.status(200).json(response);

  } catch (error) {
    console.error('Error retrieving config versions:', error);
    res.status(500).json({
      success: false,
      error: 'Database error while fetching version history'
    });
  } finally {
    if (connection) await connection.end();
  }
});
```

### Krok 2: Restart API

```bash
# Restart kontenera susmodder-api
docker restart nginx-api-susmodder

# Sprawdź logi
docker logs nginx-api-susmodder -f
```

### Krok 3: Test Endpointu

```bash
# Test 1: Pobierz wszystkie wersje
curl http://localhost:3001/susmodder-config-versions | jq

# Test 2: Pobierz wersje dla modId=1
curl "http://localhost:3001/susmodder-config-versions?modId=1" | jq

# Test 3: Sprawdź Swagger
curl http://localhost:3001/api-docs
```

**Oczekiwany output (Test 1):**
```json
{
  "success": true,
  "count": 6,
  "versions": [
    {
      "VersionId": 1,
      "ModId": 1,
      "ModVersion": "5.4.0",
      "AmongVersion": "2024.10.29",
      ...
    },
    ...
  ]
}
```

---

## 💾 Faza 3: Modyfikacja `save_mod.php`

### Checklist

- [ ] Backup oryginalnego `save_mod.php`
- [ ] Implementacja logiki wersjonowania
- [ ] Test zapisu (zmiana wersji vs poprawka)

### Krok 1: Implementacja

**Lokalizacja:** `/srv/synapsekit-boracik/nginx/html/susmodder/susadmin/save_mod.php`

**Pełny kod znajduje się w:** `02_API_SPECIFICATION.md` (sekcja "Modyfikacja save_mod.php")

**Kluczowe zmiany:**

1. **Wykrywanie zmiany wersji:**
```php
// Pobierz obecne wersje
$stmt = $conn->prepare("SELECT ModVersion, AmongVersion FROM config WHERE Id = ?");
$stmt->bind_param("i", $modId);
$stmt->execute();
$currentData = $stmt->get_result()->fetch_assoc();

// Sprawdź zmianę
$versionChanged = ($currentData['ModVersion'] !== $modVersion || $currentData['AmongVersion'] !== $modAmongVersion);
```

2. **Logika warunkowa:**
```php
if ($versionChanged) {
    // INSERT do config_versions + UPDATE config
} else {
    // UPDATE config + UPDATE config_versions (ostatni wpis)
}
```

### Krok 2: Test Zapisu

**Test 1: Zmiana wersji (INSERT)**

1. Otwórz panel admina: `https://susadmin.susmodder.app/edit_config.html`
2. Wybierz mod "Town of Us" (Id=1)
3. Zmień `ModVersion` z "5.4.0" na "5.5.0"
4. Kliknij "Zapisz zmiany"

**Oczekiwany wynik:**
```json
{
  "success": true,
  "message": "Nowa wersja utworzona",
  "versionChanged": true
}
```

**Weryfikacja:**
```bash
# Sprawdź config_versions
docker exec nginx-mysql mysql \
  -h 193.70.42.86 \
  -u susfuckr \
  -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr -e "SELECT * FROM config_versions WHERE ModId = 1 ORDER BY CreatedAt DESC LIMIT 2;"

# Powinno być 2 wpisy: 5.5.0 (nowy) i 5.4.0 (stary)
```

**Test 2: Poprawka linku (UPDATE bez zmiany wersji)**

1. Otwórz panel admina
2. Wybierz mod "Town of Us" (Id=1)
3. NIE zmieniaj `ModVersion` (nadal "5.5.0")
4. Zmień tylko `GitHubRepoOrLink` na nowy URL
5. Kliknij "Zapisz zmiany"

**Oczekiwany wynik:**
```json
{
  "success": true,
  "message": "Mod zaktualizowany (bez zmiany wersji)",
  "versionChanged": false
}
```

**Weryfikacja:**
```bash
# Sprawdź czy link został zaktualizowany w config_versions
docker exec nginx-mysql mysql \
  -h 193.70.42.86 \
  -u susfuckr \
  -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr -e "SELECT VersionId, GitHubRepoOrLink FROM config_versions WHERE ModId = 1 AND ModVersion = '5.5.0';"

# Link powinien być zaktualizowany
```

---

## 🎨 Faza 4: Modyfikacja Frontend (Opcjonalne)

### Checklist

- [ ] Dodanie informacji o wersjonowaniu w `edit_config.html`
- [ ] Opcjonalnie: Podgląd historii wersji

### Krok 1: Informacja o Wersjonowaniu

**Lokalizacja:** `/srv/synapsekit-boracik/nginx/html/susmodder/susadmin/edit_config.html`

**Dodaj po polach `mod-version` i `mod-among-version`:**

```html
<!-- Informacja o wersjonowaniu -->
<div class="form-group" id="version-info" style="display: none;">
    <label></label>
    <div style="background-color: #444; padding: 10px; border-radius: 4px; font-size: 0.9em;">
        <span id="version-info-text"></span>
    </div>
</div>
```

**Dodaj skrypt wykrywania zmiany wersji:**

```javascript
// Przechowuj oryginalne wersje
let originalModVersion = '';
let originalAmongVersion = '';

function loadModDetails(modId) {
    // ... istniejący kod ...

    // Zapisz oryginalne wersje
    originalModVersion = mod.ModVersion;
    originalAmongVersion = mod.AmongVersion;

    // Dodaj listenery na zmianę wersji
    document.getElementById('mod-version').addEventListener('input', checkVersionChange);
    document.getElementById('mod-among-version').addEventListener('input', checkVersionChange);
}

function checkVersionChange() {
    const newModVersion = document.getElementById('mod-version').value;
    const newAmongVersion = document.getElementById('mod-among-version').value;

    const versionChanged = (newModVersion !== originalModVersion || newAmongVersion !== originalAmongVersion);

    const versionInfo = document.getElementById('version-info');
    const versionInfoText = document.getElementById('version-info-text');

    if (versionChanged) {
        versionInfo.style.display = 'flex';
        versionInfoText.innerHTML = '⚠️ <strong>Zmiana wersji</strong>: Zostanie utworzony nowy wpis w historii wersji.';
        versionInfoText.style.color = '#FFA500';
    } else {
        versionInfo.style.display = 'flex';
        versionInfoText.innerHTML = '✅ <strong>Brak zmiany wersji</strong>: Zaktualizuje bieżącą wersję.';
        versionInfoText.style.color = '#00FF00';
    }
}
```

---

## ✅ Faza 5: Testy i Weryfikacja

### Checklist Testów

#### Test 1: Kompatybilność Wstecz
- [ ] Endpoint `/susmodder-config` zwraca identyczną odpowiedź jak przed migracją
- [ ] Frontend aplikacji działa bez zmian
- [ ] Discord boty pobierają dane bez problemów

**Wykonanie:**
```bash
# Przed migracją (z backupu)
curl http://localhost:3001/susmodder-config > before.json

# Po migracji
curl http://localhost:3001/susmodder-config > after.json

# Porównanie
diff before.json after.json
# Powinno być identyczne (różnice tylko w LastUpdated)
```

#### Test 2: Nowy Endpoint
- [ ] `/susmodder-config-versions` zwraca wszystkie wersje
- [ ] `/susmodder-config-versions?modId=1` zwraca tylko wersje moda 1
- [ ] Response ma poprawny format JSON

**Wykonanie:**
```bash
# Test wszystkich wersji
curl http://localhost:3001/susmodder-config-versions | jq '.count'
# Powinno zwrócić liczbę wersji (np. 6)

# Test filtrowania
curl "http://localhost:3001/susmodder-config-versions?modId=1" | jq '.versions | length'
# Powinno zwrócić liczbę wersji dla moda 1
```

#### Test 3: Logika Wersjonowania
- [ ] Zmiana wersji tworzy nowy wpis w `config_versions`
- [ ] Brak zmiany wersji aktualizuje `config` i `config_versions`
- [ ] UNIQUE constraint działa (próba duplikatu zwraca błąd)

**Wykonanie:**
1. Edytuj mod przez panel admina - zmień wersję
2. Sprawdź czy nowy wpis w `config_versions`
3. Edytuj mod ponownie - zmień tylko link
4. Sprawdź czy link zaktualizowany w `config_versions`

#### Test 4: Integracja
- [ ] Panel admina pokazuje informację o wersjonowaniu
- [ ] Swagger dokumentacja zawiera nowy endpoint
- [ ] Logi nie zawierają błędów

---

## 🚀 Faza 6: Deployment Produkcyjny

### Checklist

- [ ] Backup przed deploymentem
- [ ] Deploy bazy danych
- [ ] Deploy backend API
- [ ] Deploy frontend (opcjonalne)
- [ ] Smoke tests
- [ ] Monitoring

### Krok 1: Backup Produkcyjny

```bash
# Wykonaj kroki z Fazy 0 na środowisku produkcyjnym
ssh user@susmodder.app
# ... backup bazy i plików ...
```

### Krok 2: Deploy Bazy Danych

```bash
# Wykonaj migrację SQL z Fazy 1
docker exec -i nginx-mysql mysql \
  -h 193.70.42.86 \
  -u susfuckr \
  -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr < ~/001_create_config_versions.sql
```

### Krok 3: Deploy Backend API

```bash
# Skopiuj zmodyfikowany routes/config.js
# (lub użyj git pull jeśli zmiany są w repo)

cd /srv/synapsekit-boracik/susmodder-api

# Restart API
docker restart nginx-api-susmodder

# Sprawdź logi
docker logs nginx-api-susmodder -f
```

### Krok 4: Deploy Frontend

```bash
# Skopiuj zmodyfikowany save_mod.php i edit_config.html
# (lub użyj git pull)

cd /srv/synapsekit-boracik/nginx/html/susmodder/susadmin

# PHP działa w kontenerze nginx-php, więc restart jeśli potrzeba
docker restart nginx-php-fpm
```

### Krok 5: Smoke Tests

```bash
# Test 1: Endpoint /susmodder-config
curl https://api.susmodder.app/susmodder-config

# Test 2: Endpoint /susmodder-config-versions
curl https://api.susmodder.app/susmodder-config-versions

# Test 3: Panel admina
# Otwórz: https://susadmin.susmodder.app/edit_config.html
# Spróbuj edytować mod
```

### Krok 6: Monitoring

```bash
# Sprawdź logi API
docker logs nginx-api-susmodder -f

# Sprawdź logi PHP
docker logs nginx-php-fpm -f

# Sprawdź logi MySQL
docker logs nginx-mysql -f | grep config_versions
```

---

## 🔄 Rollback Plan

W razie problemów:

### Krok 1: Rollback Bazy Danych

```bash
# DROP nowej tabeli
docker exec nginx-mysql mysql \
  -h 193.70.42.86 \
  -u susfuckr \
  -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr -e "DROP VIEW IF EXISTS vw_config_with_version_count; DROP TABLE IF EXISTS config_versions;"
```

### Krok 2: Rollback Plików

```bash
# Przywróć backupy
cp ~/backups/mod_versioning_YYYYMMDD/save_mod.php.backup \
   /srv/synapsekit-boracik/nginx/html/susmodder/susadmin/save_mod.php

cp ~/backups/mod_versioning_YYYYMMDD/config.js.backup \
   /srv/synapsekit-boracik/susmodder-api/routes/config.js

# Restart
docker restart nginx-api-susmodder
docker restart nginx-php-fpm
```

---

## 📊 Post-Deployment Checklist

- [ ] Wszystkie testy przeszły pomyślnie
- [ ] Brak błędów w logach
- [ ] Użytkownicy mogą korzystać z panelu admina
- [ ] Endpoint `/susmodder-config` działa jak wcześniej
- [ ] Endpoint `/susmodder-config-versions` zwraca dane
- [ ] Dokumentacja zaktualizowana
- [ ] Zespół poinformowany o nowej funkcjonalności

---

## 📝 Następne Kroki

### Krótkoterminowe (1-2 tygodnie)

1. Monitorowanie użycia nowych endpointów
2. Zbieranie feedbacku od adminów
3. Ewentualne poprawki błędów

### Średnioterminowe (1-3 miesiące)

1. Rozszerzenie panelu admina o podgląd historii wersji
2. Eksport historii wersji do CSV/JSON
3. Automatyczne powiadomienia Discord przy zmianie wersji

### Długoterminowe (6+ miesięcy)

1. Integracja z systemem compatibility_matrix
2. Automatyczne testowanie kompatybilności przy nowej wersji
3. API dla community do sprawdzania historii wersji

---

**Ostatnia aktualizacja:** 2025-10-22
**Następny krok:** Implementacja!
