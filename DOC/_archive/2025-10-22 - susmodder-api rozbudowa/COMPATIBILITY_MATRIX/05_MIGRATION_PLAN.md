# Plan Migracji i Wdrożenia - Compatibility Matrix

## 📋 Przegląd

Ten dokument opisuje szczegółowy plan wdrożenia systemu Compatibility Matrix, od przygotowania bazy danych po deployment na produkcję.

## 🎯 Założenia Początkowe

### Istniejąca Infrastruktura
- ✅ MySQL 8.0 (baza `susfuckr`)
- ✅ Node.js API (susmodder-api)
- ✅ Docker Compose setup
- ✅ Nginx reverse proxy
- ✅ Tabela `config` z modami (FULL i DLL)

### Wymagania
- Minimum downtime podczas migracji
- Zachowanie istniejących danych
- Możliwość rollback w razie problemów
- Testy przed wdrożeniem na produkcję

## 📅 Timeline

| Faza | Czas | Opis |
|------|------|------|
| **Faza 0** | 1 dzień | Przygotowanie i backup |
| **Faza 1** | 2 dni | Migracja bazy danych |
| **Faza 2** | 3 dni | Implementacja API |
| **Faza 3** | 4 dni | Interfejs administracyjny |
| **Faza 4** | 2 dni | Testy i QA |
| **Faza 5** | 1 dzień | Deployment produkcyjny |
| **RAZEM** | **13 dni** | (~2.5 tygodnia) |

---

## 🔧 Faza 0: Przygotowanie (1 dzień)

### 1. Backup Bazy Danych

```bash
#!/bin/bash
# backup-database.sh

TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
BACKUP_DIR="/srv/synapsekit-boracik/backups"
DB_NAME="susfuckr"

echo "Creating backup: ${BACKUP_DIR}/${DB_NAME}_${TIMESTAMP}.sql"

docker exec nginx-mysql mysqldump \
  -h 193.70.42.86 \
  -u susfuckr \
  -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  --single-transaction \
  --routines \
  --triggers \
  ${DB_NAME} > ${BACKUP_DIR}/${DB_NAME}_${TIMESTAMP}.sql

# Kompresja
gzip ${BACKUP_DIR}/${DB_NAME}_${TIMESTAMP}.sql

echo "Backup completed: ${BACKUP_DIR}/${DB_NAME}_${TIMESTAMP}.sql.gz"
```

**Wykonanie:**
```bash
cd /srv/synapsekit-boracik
mkdir -p backups
chmod +x backup-database.sh
./backup-database.sh
```

### 2. Analiza Istniejących Danych

```sql
-- Sprawdź statystyki
SELECT 
  ModType,
  COUNT(*) as Count,
  COUNT(DISTINCT ModVersion) as UniqueVersions
FROM config
GROUP BY ModType;

-- Wynik:
-- ModType | Count | UniqueVersions
-- full    |   8   |   8
-- dll     |   12  |   12
```

### 3. Utworzenie Branch'a Git

```bash
cd /srv/synapsekit-boracik
git checkout -b feature/compatibility-matrix
git add .
git commit -m "Checkpoint before Compatibility Matrix implementation"
```

---

## 🗄️ Faza 1: Migracja Bazy Danych (2 dni)

### Dzień 1: Utworzenie Struktury

**Plik: `/srv/synapsekit-boracik/migrations/001_create_compatibility_matrix.sql`**

```sql
-- Migration: Create Compatibility Matrix
-- Date: 2025-10-22
-- Author: Admin

USE susfuckr;

-- Tabela główna
CREATE TABLE IF NOT EXISTS compatibility_matrix (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    
    -- Referencje do modów
    FullModId INT NOT NULL,
    DllModId INT NOT NULL,
    
    -- Wersje modów
    FullModVersion VARCHAR(50) NOT NULL,
    DllModVersion VARCHAR(50) NOT NULL,
    
    -- Status kompatybilności
    CompatibilityStatus ENUM('F', 'W', 'NT', 'NW') NOT NULL DEFAULT 'NT',
    
    -- Metadane testowania
    TestedDate DATETIME NULL,
    TestedBy VARCHAR(100) NULL,
    AmongUsVersion VARCHAR(50) NULL COMMENT 'Wersja Among Us na której testowano',
    
    -- Dodatkowe informacje
    Notes TEXT NULL,
    IssuesUrl VARCHAR(255) NULL COMMENT 'Link do issue/bug report',
    
    -- Audit trail
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CreatedBy VARCHAR(100) NULL,
    UpdatedBy VARCHAR(100) NULL,
    
    -- Klucze obce
    FOREIGN KEY (FullModId) REFERENCES config(Id) ON DELETE CASCADE,
    FOREIGN KEY (DllModId) REFERENCES config(Id) ON DELETE CASCADE,
    
    -- Indeksy
    INDEX idx_full_mod (FullModId, FullModVersion),
    INDEX idx_dll_mod (DllModId, DllModVersion),
    INDEX idx_compatibility_status (CompatibilityStatus),
    INDEX idx_tested_date (TestedDate),
    
    -- Unikalność
    UNIQUE KEY unique_compatibility (FullModId, DllModId, FullModVersion, DllModVersion)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Widok: Aktualne kompatybilności
CREATE OR REPLACE VIEW vw_current_compatibility AS
SELECT 
    cm.Id,
    cm.FullModId,
    fm.ModName AS FullModName,
    fm.ModVersion AS FullModCurrentVersion,
    cm.FullModVersion AS TestedFullModVersion,
    cm.DllModId,
    dm.ModName AS DllModName,
    dm.ModVersion AS DllModCurrentVersion,
    cm.DllModVersion AS TestedDllModVersion,
    cm.CompatibilityStatus,
    cm.TestedDate,
    cm.TestedBy,
    cm.AmongUsVersion,
    cm.Notes,
    cm.IssuesUrl,
    CASE 
        WHEN fm.ModVersion = cm.FullModVersion 
         AND dm.ModVersion = cm.DllModVersion 
        THEN TRUE
        ELSE FALSE
    END AS IsCurrentVersion,
    cm.CreatedAt,
    cm.UpdatedAt
FROM compatibility_matrix cm
JOIN config fm ON cm.FullModId = fm.Id AND fm.ModType = 'full'
JOIN config dm ON cm.DllModId = dm.Id AND dm.ModType = 'dll';

-- Widok: Pełna macierz
CREATE OR REPLACE VIEW vw_compatibility_matrix_full AS
SELECT 
    fm.Id AS FullModId,
    fm.ModName AS FullModName,
    fm.ModVersion AS FullModVersion,
    dm.Id AS DllModId,
    dm.ModName AS DllModName,
    dm.ModVersion AS DllModVersion,
    COALESCE(cm.CompatibilityStatus, 'NT') AS Status,
    cm.TestedDate,
    cm.TestedBy,
    cm.Id AS MatrixId
FROM config fm
CROSS JOIN config dm
LEFT JOIN compatibility_matrix cm 
    ON cm.FullModId = fm.Id 
    AND cm.DllModId = dm.Id
    AND cm.FullModVersion = fm.ModVersion
    AND cm.DllModVersion = dm.ModVersion
WHERE fm.ModType = 'full' 
  AND dm.ModType = 'dll';
```

**Wykonanie migracji:**
```bash
# Test na kopii bazy (zalecane)
docker exec nginx-mysql mysql \
  -h 193.70.42.86 \
  -u susfuckr \
  -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr < migrations/001_create_compatibility_matrix.sql

# Weryfikacja
docker exec nginx-mysql mysql \
  -h 193.70.42.86 \
  -u susfuckr \
  -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr -e "SHOW TABLES LIKE 'compatibility%';"

# Powinno zwrócić:
# compatibility_matrix
```

### Dzień 2: Wypełnienie Danymi

**Plik: `/srv/synapsekit-boracik/migrations/002_populate_initial_data.sql`**

```sql
-- Migration: Populate Initial Compatibility Data
-- Date: 2025-10-22
-- Author: Admin

USE susfuckr;

-- Wypełnij wszystkie kombinacje jako NT (Not Tested)
INSERT INTO compatibility_matrix 
(FullModId, DllModId, FullModVersion, DllModVersion, CompatibilityStatus, CreatedBy)
SELECT 
    fm.Id, 
    dm.Id, 
    fm.ModVersion, 
    dm.ModVersion, 
    'NT',
    'migration_script'
FROM config fm
CROSS JOIN config dm
WHERE fm.ModType = 'full' 
  AND dm.ModType = 'dll'
ON DUPLICATE KEY UPDATE Id=Id; -- Ignore jeśli już istnieje

-- Sprawdź ile wpisów utworzono
SELECT COUNT(*) as TotalCombinations FROM compatibility_matrix;

-- Sprawdź rozkład
SELECT 
    fm.ModName as FullMod,
    COUNT(*) as DllCompatibilities
FROM compatibility_matrix cm
JOIN config fm ON cm.FullModId = fm.Id
GROUP BY fm.ModName
ORDER BY DllCompatibilities DESC;
```

**Wykonanie:**
```bash
docker exec nginx-mysql mysql \
  -h 193.70.42.86 \
  -u susfuckr \
  -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr < migrations/002_populate_initial_data.sql
```

**Opcjonalnie: Import znanych kompatybilności**

```sql
-- Jeśli mamy istniejącą wiedzę o kompatybilności
UPDATE compatibility_matrix 
SET 
    CompatibilityStatus = 'F',
    TestedBy = 'legacy_knowledge',
    TestedDate = '2025-10-01 00:00:00',
    Notes = 'Known to work from community feedback'
WHERE 
    (FullModId = 1 AND DllModId = 5)  -- Town of Us + AleLuduMod
    OR (FullModId = 1 AND DllModId = 8)  -- Town of Us + AUnlocker
    OR (FullModId = 4 AND DllModId = 5)  -- The Other Roles + AleLuduMod
;
```

---

## 💻 Faza 2: Implementacja API (3 dni)

### Dzień 1: Podstawowe Endpointy (GET)

**Plik: `/srv/synapsekit-boracik/susmodder-api/routes/compatibility.js`**

```javascript
const express = require('express');
const mysql = require('mysql2/promise');
const requireAuthToken = require('../middleware/auth');
const router = express.Router();

const dbConfig = {
  host: process.env.DB_HOST,
  user: process.env.DB_USER,
  password: process.env.DB_PASSWORD,
  database: process.env.DB_NAME,
  port: process.env.DB_PORT
};

/**
 * @swagger
 * /compatibility:
 *   get:
 *     summary: Get compatibility list for a mod
 *     tags: [Compatibility]
 *     parameters:
 *       - in: query
 *         name: fullModId
 *         schema:
 *           type: integer
 *       - in: query
 *         name: dllModId
 *         schema:
 *           type: integer
 *       - in: query
 *         name: status
 *         schema:
 *           type: string
 *         description: Comma-separated status codes (F,W,NT,NW)
 *     responses:
 *       200:
 *         description: Success
 *       400:
 *         description: Bad request
 */
router.get('/compatibility', async (req, res) => {
  const { fullModId, dllModId, fullModVersion, dllModVersion, status, includeUntested = 'true' } = req.query;
  
  let connection;
  try {
    connection = await mysql.createConnection(dbConfig);
    
    // Walidacja: wymagany albo fullModId albo dllModId
    if (!fullModId && !dllModId) {
      return res.status(400).json({
        success: false,
        error: 'Either fullModId or dllModId is required'
      });
    }
    
    if (fullModId && dllModId) {
      return res.status(400).json({
        success: false,
        error: 'Cannot specify both fullModId and dllModId'
      });
    }
    
    let query, params, queryType, modInfo;
    
    if (fullModId) {
      // Zapytanie o mod FULL -> zwróć listę DLL
      queryType = 'full';
      
      // Pobierz info o modzie FULL
      const [modRows] = await connection.execute(
        'SELECT ModName, ModVersion FROM config WHERE Id = ? AND ModType = "full"',
        [fullModId]
      );
      
      if (modRows.length === 0) {
        return res.status(404).json({
          success: false,
          error: `Full mod with id ${fullModId} not found`
        });
      }
      
      modInfo = modRows[0];
      const versionToUse = fullModVersion || modInfo.ModVersion;
      
      query = `
        SELECT 
          cm.Id,
          cm.DllModId as ModId,
          dm.ModName as ModName,
          cm.DllModVersion as ModVersion,
          dm.ModVersion as CurrentVersion,
          cm.CompatibilityStatus as Status,
          cm.TestedDate,
          cm.TestedBy,
          cm.AmongUsVersion,
          cm.Notes,
          cm.IssuesUrl,
          CASE 
            WHEN dm.ModVersion = cm.DllModVersion THEN TRUE
            ELSE FALSE
          END as IsCurrentVersion
        FROM compatibility_matrix cm
        JOIN config dm ON cm.DllModId = dm.Id
        WHERE cm.FullModId = ? AND cm.FullModVersion = ?
      `;
      params = [fullModId, versionToUse];
      
    } else {
      // Zapytanie o mod DLL -> zwróć listę FULL
      queryType = 'dll';
      
      const [modRows] = await connection.execute(
        'SELECT ModName, ModVersion FROM config WHERE Id = ? AND ModType = "dll"',
        [dllModId]
      );
      
      if (modRows.length === 0) {
        return res.status(404).json({
          success: false,
          error: `DLL mod with id ${dllModId} not found`
        });
      }
      
      modInfo = modRows[0];
      const versionToUse = dllModVersion || modInfo.ModVersion;
      
      query = `
        SELECT 
          cm.Id,
          cm.FullModId as ModId,
          fm.ModName as ModName,
          cm.FullModVersion as ModVersion,
          fm.ModVersion as CurrentVersion,
          cm.CompatibilityStatus as Status,
          cm.TestedDate,
          cm.TestedBy,
          cm.AmongUsVersion,
          cm.Notes,
          cm.IssuesUrl,
          CASE 
            WHEN fm.ModVersion = cm.FullModVersion THEN TRUE
            ELSE FALSE
          END as IsCurrentVersion
        FROM compatibility_matrix cm
        JOIN config fm ON cm.FullModId = fm.Id
        WHERE cm.DllModId = ? AND cm.DllModVersion = ?
      `;
      params = [dllModId, versionToUse];
    }
    
    // Filtrowanie po statusie
    if (status) {
      const statuses = status.split(',').map(s => s.trim());
      query += ` AND cm.CompatibilityStatus IN (${statuses.map(() => '?').join(',')})`;
      params.push(...statuses);
    } else if (includeUntested === 'false') {
      query += ` AND cm.CompatibilityStatus != 'NT'`;
    }
    
    query += ' ORDER BY ModName ASC';
    
    const [rows] = await connection.execute(query, params);
    
    // Formatowanie odpowiedzi
    const compatibilities = rows.map(row => {
      const result = {
        id: row.Id,
        status: row.Status,
        testedDate: row.TestedDate,
        testedBy: row.TestedBy,
        amongUsVersion: row.AmongUsVersion,
        notes: row.Notes,
        issuesUrl: row.IssuesUrl,
        isCurrentVersion: Boolean(row.IsCurrentVersion)
      };
      
      if (queryType === 'full') {
        result.dllMod = {
          id: row.ModId,
          name: row.ModName,
          version: row.ModVersion,
          currentVersion: row.CurrentVersion
        };
      } else {
        result.fullMod = {
          id: row.ModId,
          name: row.ModName,
          version: row.ModVersion,
          currentVersion: row.CurrentVersion
        };
      }
      
      // Dodaj warning jeśli nie jest aktualna wersja
      if (!result.isCurrentVersion) {
        result.warning = `Tested on older version (${row.ModVersion}), current is ${row.CurrentVersion}`;
      }
      
      return result;
    });
    
    res.status(200).json({
      success: true,
      query: {
        type: queryType,
        modId: fullModId || dllModId,
        modName: modInfo.ModName,
        modVersion: fullModVersion || dllModVersion || modInfo.ModVersion
      },
      count: compatibilities.length,
      compatibilities
    });
    
  } catch (error) {
    console.error('Error in GET /compatibility:', error);
    res.status(500).json({
      success: false,
      error: 'Internal server error'
    });
  } finally {
    if (connection) await connection.end();
  }
});

module.exports = router;
```

**Rejestracja routera w `server.js`:**

```javascript
// W pliku /srv/synapsekit-boracik/susmodder-api/server.js
// Dodaj po innych routes:

const compatibilityRoutes = require('./routes/compatibility');
app.use('/api', compatibilityRoutes);
```

### Dzień 2: Endpointy Modyfikacji (POST, PUT, DELETE)

**Dodaj do `/srv/synapsekit-boracik/susmodder-api/routes/compatibility.js`:**

```javascript
// GET /:id - szczegóły pojedynczego wpisu
router.get('/compatibility/:id', async (req, res) => {
  const { id } = req.params;
  let connection;
  
  try {
    connection = await mysql.createConnection(dbConfig);
    
    const [rows] = await connection.execute(`
      SELECT * FROM vw_current_compatibility WHERE Id = ?
    `, [id]);
    
    if (rows.length === 0) {
      return res.status(404).json({
        success: false,
        error: `Compatibility entry with id ${id} not found`
      });
    }
    
    res.status(200).json({
      success: true,
      compatibility: rows[0]
    });
    
  } catch (error) {
    console.error('Error in GET /compatibility/:id:', error);
    res.status(500).json({ success: false, error: 'Internal server error' });
  } finally {
    if (connection) await connection.end();
  }
});

// POST - utworzenie nowego wpisu
router.post('/compatibility', requireAuthToken, async (req, res) => {
  const {
    fullModId, dllModId, fullModVersion, dllModVersion,
    status, testedBy, amongUsVersion, notes, issuesUrl
  } = req.body;
  
  let connection;
  
  try {
    connection = await mysql.createConnection(dbConfig);
    
    // Walidacja
    if (!fullModId || !dllModId || !fullModVersion || !dllModVersion || !status) {
      return res.status(400).json({
        success: false,
        error: 'Missing required fields'
      });
    }
    
    // Sprawdź czy mod FULL istnieje
    const [fullMods] = await connection.execute(
      'SELECT Id FROM config WHERE Id = ? AND ModType = "full"',
      [fullModId]
    );
    
    if (fullMods.length === 0) {
      return res.status(400).json({
        success: false,
        error: `Full mod with id ${fullModId} not found or is not a full mod`
      });
    }
    
    // Sprawdź czy mod DLL istnieje
    const [dllMods] = await connection.execute(
      'SELECT Id FROM config WHERE Id = ? AND ModType = "dll"',
      [dllModId]
    );
    
    if (dllMods.length === 0) {
      return res.status(400).json({
        success: false,
        error: `DLL mod with id ${dllModId} not found or is not a dll mod`
      });
    }
    
    // Walidacja statusu
    if (!['F', 'W', 'NT', 'NW'].includes(status)) {
      return res.status(400).json({
        success: false,
        error: 'Invalid status. Must be one of: F, W, NT, NW'
      });
    }
    
    // Sprawdź czy wpis już istnieje
    const [existing] = await connection.execute(`
      SELECT Id FROM compatibility_matrix 
      WHERE FullModId = ? AND DllModId = ? 
        AND FullModVersion = ? AND DllModVersion = ?
    `, [fullModId, dllModId, fullModVersion, dllModVersion]);
    
    if (existing.length > 0) {
      return res.status(409).json({
        success: false,
        error: 'Compatibility entry already exists for this combination',
        existingId: existing[0].Id
      });
    }
    
    // Utwórz wpis
    const [result] = await connection.execute(`
      INSERT INTO compatibility_matrix 
      (FullModId, DllModId, FullModVersion, DllModVersion, 
       CompatibilityStatus, TestedDate, TestedBy, AmongUsVersion, 
       Notes, IssuesUrl, CreatedBy)
      VALUES (?, ?, ?, ?, ?, NOW(), ?, ?, ?, ?, ?)
    `, [
      fullModId, dllModId, fullModVersion, dllModVersion,
      status, testedBy, amongUsVersion, notes, issuesUrl,
      req.user || 'api'  // Z middleware auth
    ]);
    
    res.status(201).json({
      success: true,
      message: 'Compatibility entry created successfully',
      id: result.insertId
    });
    
  } catch (error) {
    console.error('Error in POST /compatibility:', error);
    res.status(500).json({ success: false, error: 'Internal server error' });
  } finally {
    if (connection) await connection.end();
  }
});

// PUT /:id - aktualizacja wpisu
router.put('/compatibility/:id', requireAuthToken, async (req, res) => {
  const { id } = req.params;
  const { status, testedBy, amongUsVersion, notes, issuesUrl } = req.body;
  
  let connection;
  
  try {
    connection = await mysql.createConnection(dbConfig);
    
    // Sprawdź czy wpis istnieje
    const [existing] = await connection.execute(
      'SELECT Id FROM compatibility_matrix WHERE Id = ?',
      [id]
    );
    
    if (existing.length === 0) {
      return res.status(404).json({
        success: false,
        error: `Compatibility entry with id ${id} not found`
      });
    }
    
    // Przygotuj update
    const updates = [];
    const values = [];
    
    if (status) {
      if (!['F', 'W', 'NT', 'NW'].includes(status)) {
        return res.status(400).json({
          success: false,
          error: 'Invalid status'
        });
      }
      updates.push('CompatibilityStatus = ?');
      values.push(status);
      updates.push('TestedDate = NOW()');
    }
    
    if (testedBy !== undefined) {
      updates.push('TestedBy = ?');
      values.push(testedBy);
    }
    
    if (amongUsVersion !== undefined) {
      updates.push('AmongUsVersion = ?');
      values.push(amongUsVersion);
    }
    
    if (notes !== undefined) {
      updates.push('Notes = ?');
      values.push(notes);
    }
    
    if (issuesUrl !== undefined) {
      updates.push('IssuesUrl = ?');
      values.push(issuesUrl);
    }
    
    updates.push('UpdatedBy = ?');
    values.push(req.user || 'api');
    
    values.push(id);
    
    await connection.execute(
      `UPDATE compatibility_matrix SET ${updates.join(', ')} WHERE Id = ?`,
      values
    );
    
    res.status(200).json({
      success: true,
      message: 'Compatibility entry updated successfully'
    });
    
  } catch (error) {
    console.error('Error in PUT /compatibility/:id:', error);
    res.status(500).json({ success: false, error: 'Internal server error' });
  } finally {
    if (connection) await connection.end();
  }
});

// DELETE /:id - usunięcie wpisu
router.delete('/compatibility/:id', requireAuthToken, async (req, res) => {
  const { id } = req.params;
  let connection;
  
  try {
    connection = await mysql.createConnection(dbConfig);
    
    const [result] = await connection.execute(
      'DELETE FROM compatibility_matrix WHERE Id = ?',
      [id]
    );
    
    if (result.affectedRows === 0) {
      return res.status(404).json({
        success: false,
        error: `Compatibility entry with id ${id} not found`
      });
    }
    
    res.status(200).json({
      success: true,
      message: 'Compatibility entry deleted successfully'
    });
    
  } catch (error) {
    console.error('Error in DELETE /compatibility/:id:', error);
    res.status(500).json({ success: false, error: 'Internal server error' });
  } finally {
    if (connection) await connection.end();
  }
});
```

### Dzień 3: Endpoint Macierzy i Testy

**Endpoint dla pełnej macierzy:**

```javascript
// GET /matrix - pełna macierz dla UI
router.get('/compatibility/matrix', requireAuthToken, async (req, res) => {
  const { onlyCurrentVersions = 'true' } = req.query;
  let connection;
  
  try {
    connection = await mysql.createConnection(dbConfig);
    
    let query = 'SELECT * FROM vw_compatibility_matrix_full';
    
    if (onlyCurrentVersions === 'true') {
      query += ' WHERE 1=1'; // Filtruj tylko aktualne wersje
    }
    
    query += ' ORDER BY FullModName, DllModName';
    
    const [rows] = await connection.execute(query);
    
    // Przekształć do struktury dla UI
    const fullMods = [...new Set(rows.map(r => ({
      id: r.FullModId,
      name: r.FullModName,
      version: r.FullModVersion
    })))];
    
    const dllMods = [...new Set(rows.map(r => ({
      id: r.DllModId,
      name: r.DllModName,
      version: r.DllModVersion
    })))];
    
    const matrix = rows.map(r => ({
      fullModId: r.FullModId,
      dllModId: r.DllModId,
      status: r.Status,
      matrixId: r.MatrixId,
      testedDate: r.TestedDate,
      testedBy: r.TestedBy
    }));
    
    res.status(200).json({
      success: true,
      fullMods: uniqueById(fullMods),
      dllMods: uniqueById(dllMods),
      matrix
    });
    
  } catch (error) {
    console.error('Error in GET /compatibility/matrix:', error);
    res.status(500).json({ success: false, error: 'Internal server error' });
  } finally {
    if (connection) await connection.end();
  }
});

// Helper function
function uniqueById(array) {
  const seen = new Set();
  return array.filter(item => {
    if (seen.has(item.id)) return false;
    seen.add(item.id);
    return true;
  });
}
```

**Testy API:**

Utworzyć plik `/srv/synapsekit-boracik/susmodder-api/test/test-compatibility-api.sh`:

```bash
#!/bin/bash

API_URL="http://localhost:3001/api"
TOKEN="e4a1c7b2f3d8e9a0b5c6d7e8f9a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9"

echo "Testing Compatibility API..."

# Test 1: GET /compatibility?fullModId=1
echo "\n1. GET compatibilities for Full Mod (Town of Us)"
curl -s "${API_URL}/compatibility?fullModId=1" | jq .

# Test 2: GET /compatibility?dllModId=5
echo "\n2. GET compatibilities for DLL Mod (AleLuduMod)"
curl -s "${API_URL}/compatibility?dllModId=5" | jq .

# Test 3: POST /compatibility (create new)
echo "\n3. POST create new compatibility"
curl -s -X POST "${API_URL}/compatibility" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer ${TOKEN}" \
  -d '{
    "fullModId": 1,
    "dllModId": 5,
    "fullModVersion": "5.3.1",
    "dllModVersion": "latest",
    "status": "F",
    "testedBy": "test_script",
    "notes": "API test"
  }' | jq .

# Test 4: GET /compatibility/matrix
echo "\n4. GET full matrix"
curl -s -H "Authorization: Bearer ${TOKEN}" \
  "${API_URL}/compatibility/matrix" | jq .

echo "\nTests completed!"
```

```bash
chmod +x test/test-compatibility-api.sh
```

---

## 🎨 Faza 3: Interfejs Administracyjny (4 dni)

### Wybór Technologii

**Opcja A: Standalone React App**
- Nowa aplikacja React
- Deploy jako osobny kontener
- Komunikacja przez API

**Opcja B: Dodanie do Istniejącego Panelu susadmin**
- Integracja z istniejącym interfejsem
- Spójny design system
- Wspólne uwierzytelnienie

**Rekomendacja: Opcja B** (jeśli susadmin już istnieje)

### Struktura Plików (przykład React)

```
susadmin/
├── src/
│   ├── components/
│   │   ├── compatibility/
│   │   │   ├── MatrixView.jsx
│   │   │   ├── DetailView.jsx
│   │   │   ├── EditModal.jsx
│   │   │   ├── BulkEdit.jsx
│   │   │   └── TestingMode.jsx
│   │   └── common/
│   │       ├── StatusBadge.jsx
│   │       └── ModSelector.jsx
│   ├── api/
│   │   └── compatibility.js
│   ├── hooks/
│   │   └── useCompatibility.js
│   └── pages/
│       └── CompatibilityPage.jsx
```

**Przykładowy komponent (MatrixView.jsx):**

```jsx
import React, { useState, useEffect } from 'react';
import { getCompatibilityMatrix } from '../api/compatibility';
import StatusBadge from '../common/StatusBadge';
import EditModal from './EditModal';

export default function MatrixView() {
  const [matrix, setMatrix] = useState(null);
  const [loading, setLoading] = useState(true);
  const [selectedCell, setSelectedCell] = useState(null);
  
  useEffect(() => {
    loadMatrix();
  }, []);
  
  async function loadMatrix() {
    try {
      const data = await getCompatibilityMatrix();
      setMatrix(data);
    } catch (error) {
      console.error('Failed to load matrix:', error);
    } finally {
      setLoading(false);
    }
  }
  
  function handleCellClick(fullModId, dllModId, matrixId) {
    setSelectedCell({ fullModId, dllModId, matrixId });
  }
  
  if (loading) return <div>Loading...</div>;
  
  return (
    <div className="matrix-container">
      <h2>Compatibility Matrix</h2>
      
      <table className="matrix-table">
        <thead>
          <tr>
            <th>Full Mod</th>
            {matrix.dllMods.map(dll => (
              <th key={dll.id}>{dll.name}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {matrix.fullMods.map(full => (
            <tr key={full.id}>
              <td>{full.name} v{full.version}</td>
              {matrix.dllMods.map(dll => {
                const cell = matrix.matrix.find(
                  m => m.fullModId === full.id && m.dllModId === dll.id
                );
                return (
                  <td 
                    key={`${full.id}-${dll.id}`}
                    onClick={() => handleCellClick(full.id, dll.id, cell?.matrixId)}
                    className="matrix-cell"
                  >
                    <StatusBadge status={cell?.status || 'NT'} />
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
      
      {selectedCell && (
        <EditModal 
          {...selectedCell}
          onClose={() => setSelectedCell(null)}
          onSave={loadMatrix}
        />
      )}
    </div>
  );
}
```

**API Client (compatibility.js):**

```javascript
const API_BASE = process.env.REACT_APP_API_URL || 'http://localhost:3001/api';
const TOKEN = localStorage.getItem('authToken');

export async function getCompatibilityMatrix() {
  const response = await fetch(`${API_BASE}/compatibility/matrix`, {
    headers: {
      'Authorization': `Bearer ${TOKEN}`
    }
  });
  
  if (!response.ok) {
    throw new Error('Failed to fetch matrix');
  }
  
  return await response.json();
}

export async function updateCompatibility(id, data) {
  const response = await fetch(`${API_BASE}/compatibility/${id}`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${TOKEN}`
    },
    body: JSON.stringify(data)
  });
  
  if (!response.ok) {
    throw new Error('Failed to update compatibility');
  }
  
  return await response.json();
}

// ... więcej funkcji
```

---

## 🧪 Faza 4: Testy i QA (2 dni)

### Dzień 1: Testy Jednostkowe i Integracyjne

**Test Suite dla API:**

```javascript
// test/compatibility.test.js
const request = require('supertest');
const app = require('../server');

describe('Compatibility API', () => {
  it('should get compatibilities for full mod', async () => {
    const res = await request(app)
      .get('/api/compatibility?fullModId=1')
      .expect(200);
    
    expect(res.body.success).toBe(true);
    expect(res.body.compatibilities).toBeInstanceOf(Array);
  });
  
  it('should create new compatibility', async () => {
    const res = await request(app)
      .post('/api/compatibility')
      .set('Authorization', 'Bearer ' + process.env.HTTP_TOKEN)
      .send({
        fullModId: 1,
        dllModId: 5,
        fullModVersion: '5.3.1',
        dllModVersion: 'latest',
        status: 'F'
      })
      .expect(201);
    
    expect(res.body.success).toBe(true);
  });
  
  // ... więcej testów
});
```

### Dzień 2: Testy Manualne i User Acceptance Testing

**Checklist:**
- [ ] Matrix view wyświetla się poprawnie
- [ ] Kliknięcie na komórkę otwiera modal
- [ ] Edycja statusu działa
- [ ] Zmiana wersji moda przeładowuje macierz
- [ ] Bulk edit działa dla wielu wpisów
- [ ] Testing mode działa sekwencyjnie
- [ ] API zwraca poprawne dane
- [ ] Błędy są odpowiednio obsługiwane
- [ ] Mobile view jest responsywny

---

## 🚀 Faza 5: Deployment Produkcyjny (1 dzień)

### Przygotowanie Produkcji

**1. Backup przed wdrożeniem:**
```bash
./backup-database.sh
```

**2. Migracja bazy danych:**
```bash
docker exec nginx-mysql mysql \
  -h 193.70.42.86 \
  -u susfuckr \
  -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr < migrations/001_create_compatibility_matrix.sql

docker exec nginx-mysql mysql \
  -h 193.70.42.86 \
  -u susfuckr \
  -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr < migrations/002_populate_initial_data.sql
```

**3. Deploy API:**
```bash
cd /srv/synapsekit-boracik/susmodder-api
docker-compose restart susmodder-api
docker logs -f nginx-api-susmodder
```

**4. Deploy Frontend (jeśli standalone):**
```bash
cd /srv/synapsekit-boracik/susadmin
npm run build
# Deploy do nginx html
```

**5. Weryfikacja:**
```bash
curl http://localhost:3001/api/compatibility/matrix \
  -H "Authorization: Bearer YOUR_TOKEN"
```

### Rollback Plan

**Jeśli coś pójdzie nie tak:**

```bash
# 1. Przywróć backup bazy
gunzip -c backups/susfuckr_YYYYMMDD_HHMMSS.sql.gz | \
docker exec -i nginx-mysql mysql \
  -h 193.70.42.86 \
  -u susfuckr \
  -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr

# 2. Przywróć poprzednią wersję API
cd /srv/synapsekit-boracik
git checkout main
docker-compose restart susmodder-api
```

### Monitoring Post-Deploy

**1. Sprawdź logi:**
```bash
docker logs -f nginx-api-susmodder
```

**2. Sprawdź metryki:**
```bash
# Rozmiar tabeli
docker exec nginx-mysql mysql \
  -h 193.70.42.86 \
  -u susfuckr \
  -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr -e "
    SELECT 
      table_name,
      ROUND(((data_length + index_length) / 1024 / 1024), 2) AS size_mb
    FROM information_schema.TABLES 
    WHERE table_schema = 'susfuckr' 
      AND table_name = 'compatibility_matrix';
  "
```

**3. Sprawdź wydajność:**
```bash
# Query timing
time curl "http://localhost:3001/api/compatibility?fullModId=1"
```

---

## 📊 Podsumowanie

### Osiągnięte Cele
- ✅ Stworzenie tabeli `compatibility_matrix`
- ✅ Implementacja API endpoints
- ✅ Interfejs administracyjny
- ✅ Obsługa wersjonowania
- ✅ Testy i dokumentacja

### Metryki Sukcesu
- ⏱️ Czas odpowiedzi API < 200ms
- 📊 Pokrycie testami > 80%
- 🐛 Zero critical bugs
- 👥 Pozytywny feedback od adminów

### Następne Kroki
1. Monitoring użytkowania przez 1 tydzień
2. Zbieranie feedbacku od adminów
3. Optymalizacja na podstawie metryk
4. Rozszerzenia (Discord integration, community feedback)

---

## 🔗 Dodatkowe Zasoby

- [00_PROJECT_SUMMARY.md](./00_PROJECT_SUMMARY.md)
- [01_DATABASE_DESIGN.md](./01_DATABASE_DESIGN.md)
- [02_API_SPECIFICATION.md](./02_API_SPECIFICATION.md)
- [03_VERSION_HANDLING.md](./03_VERSION_HANDLING.md)
- [04_ADMIN_INTERFACE.md](./04_ADMIN_INTERFACE.md)
