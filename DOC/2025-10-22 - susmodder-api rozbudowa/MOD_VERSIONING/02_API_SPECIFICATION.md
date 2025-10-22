# Specyfikacja API - Wersjonowanie Modów

## 📖 Wprowadzenie

Specyfikacja opisuje endpointy API dla systemu wersjonowania modów. Kluczowe zasady:
- ✅ **Kompatybilność wstecz:** Endpoint `/susmodder-config` działa IDENTYCZNIE jak dotychczas
- ✅ **Nowy endpoint:** `/susmodder-config-versions` do przeglądania historii
- ✅ **Publiczny dostęp:** Oba endpointy bez autoryzacji (jak obecny `/susmodder-config`)
- ✅ **Minimalizm:** Zwracamy tylko niezbędne dane

---

## 🌐 Endpointy

### Przegląd

| Endpoint | Metoda | Opis | Auth |
|----------|--------|------|------|
| `/susmodder-config` | GET | Pobierz najnowsze wersje modów (BEZ ZMIAN!) | ❌ Nie |
| `/susmodder-config-versions` | GET | Pobierz historię wersji (wszystkie lub filtrowane) | ❌ Nie |

**Uwaga:** Endpoint POST/PUT do zapisu zmian pozostaje w `save_mod.php` (nie jest częścią REST API).

---

## 📍 GET `/susmodder-config`

### Opis

**BEZ ZMIAN!** Zwraca tabelę `config` z najnowszymi wersjami wszystkich modów.

### Request

```http
GET /susmodder-config HTTP/1.1
Host: api.susmodder.app
```

**Parametry:** Brak

### Response

**Status:** `200 OK`

**Format:** JSON Array

```json
[
  {
    "Id": 1,
    "ModName": "Town of Us",
    "PngFileName": "tou.png",
    "InstallPath": null,
    "GitHubRepoOrLink": "https://github.com/tou/releases/v5.4.0",
    "EpicGitHubRepoOrLink": "https://github.com/tou/epic/v5.4.0",
    "ModType": "full",
    "DllInstallPath": null,
    "ModVersion": "5.4.0",
    "LastUpdated": null,
    "AmongVersion": "2024.10.29",
    "Description": "Town of Us mod description"
  },
  {
    "Id": 2,
    "ModName": "ToU - Wygon",
    "PngFileName": "wygon.png",
    "InstallPath": null,
    "GitHubRepoOrLink": "https://github.com/wygon/releases/v2.0.0",
    "EpicGitHubRepoOrLink": null,
    "ModType": "full",
    "DllInstallPath": null,
    "ModVersion": "2.0.0",
    "LastUpdated": null,
    "AmongVersion": "2024.10.01",
    "Description": "Wygon mod description"
  },
  {
    "Id": 5,
    "ModName": "AleLuduMod",
    "PngFileName": "alelud.png",
    "InstallPath": null,
    "GitHubRepoOrLink": "https://github.com/alelud/latest",
    "EpicGitHubRepoOrLink": null,
    "ModType": "dll",
    "DllInstallPath": "BepInEx/plugins/AleLuduMod.dll",
    "ModVersion": "latest",
    "LastUpdated": null,
    "AmongVersion": "2024.10.29",
    "Description": "AleLuduMod description"
  }
]
```

### Implementacja (routes/config.js)

**BEZ ZMIAN!** Kod pozostaje identyczny:

```javascript
router.get('/susmodder-config', async (req, res) => {
  let connection;
  try {
    connection = await mysql.createConnection(dbConfig);

    // Pobierz wszystkie dane z tabeli config
    const [rows] = await connection.execute('SELECT * FROM config');

    // Rzutuj Id na integer
    const data = rows.map(row => ({
      ...row,
      Id: parseInt(row.Id, 10)
    }));

    res.setHeader('Content-Type', 'application/json');
    res.status(200).send(JSON.stringify(data, null, 2));
  } catch (error) {
    console.error('Error retrieving susmodder config:', error);
    res.status(500).send('Error retrieving configuration');
  } finally {
    if (connection) await connection.end();
  }
});
```

**Dlaczego bez zmian?**
- Zachowanie kompatybilności wstecz
- Istniejące aplikacje (frontend, boty) działają bez modyfikacji
- Użytkownicy widzą tylko najnowsze wersje (najczęstszy przypadek użycia)

---

## 📍 GET `/susmodder-config-versions` (NOWY)

### Opis

Zwraca historię wersji modów z tabeli `config_versions`. Może zwrócić wszystkie wersje lub filtrowane po `modId`.

### Request

```http
GET /susmodder-config-versions HTTP/1.1
Host: api.susmodder.app
```

**Parametry zapytania (query params):**

| Parametr | Typ | Wymagany | Opis |
|----------|-----|----------|------|
| `modId` | integer | ❌ Nie | ID moda z tabeli `config`. Jeśli podany, zwraca tylko wersje tego moda. |

### Response (wszystkie wersje)

**Request:**
```http
GET /susmodder-config-versions
```

**Status:** `200 OK`

**Format:** JSON Object

```json
{
  "success": true,
  "count": 6,
  "versions": [
    {
      "VersionId": 3,
      "ModId": 1,
      "ModVersion": "5.4.0",
      "AmongVersion": "2024.10.29",
      "GitHubRepoOrLink": "https://github.com/tou/releases/v5.4.0",
      "EpicGitHubRepoOrLink": "https://github.com/tou/epic/v5.4.0",
      "CreatedAt": "2024-10-29T09:15:00.000Z",
      "CreatedBy": null,
      "Notes": null
    },
    {
      "VersionId": 2,
      "ModId": 1,
      "ModVersion": "5.3.1",
      "AmongVersion": "2024.10.01",
      "GitHubRepoOrLink": "https://github.com/tou/releases/v5.3.1",
      "EpicGitHubRepoOrLink": "https://github.com/tou/epic/v5.3.1",
      "CreatedAt": "2024-10-01T14:30:00.000Z",
      "CreatedBy": null,
      "Notes": null
    },
    {
      "VersionId": 1,
      "ModId": 1,
      "ModVersion": "5.3.0",
      "AmongVersion": "2024.09.01",
      "GitHubRepoOrLink": "https://github.com/tou/releases/v5.3.0",
      "EpicGitHubRepoOrLink": null,
      "CreatedAt": "2024-09-01T10:00:00.000Z",
      "CreatedBy": null,
      "Notes": "Initial version imported from config table"
    },
    {
      "VersionId": 5,
      "ModId": 2,
      "ModVersion": "2.0.0",
      "AmongVersion": "2024.10.01",
      "GitHubRepoOrLink": "https://github.com/wygon/releases/v2.0.0",
      "EpicGitHubRepoOrLink": null,
      "CreatedAt": "2024-10-01T16:45:00.000Z",
      "CreatedBy": null,
      "Notes": "Initial version imported from config table"
    },
    {
      "VersionId": 4,
      "ModId": 2,
      "ModVersion": "1.0.0",
      "AmongVersion": "2024.08.01",
      "GitHubRepoOrLink": "https://github.com/wygon/releases/v1.0.0",
      "EpicGitHubRepoOrLink": null,
      "CreatedAt": "2024-08-01T12:00:00.000Z",
      "CreatedBy": null,
      "Notes": null
    },
    {
      "VersionId": 6,
      "ModId": 5,
      "ModVersion": "latest",
      "AmongVersion": "2024.10.29",
      "GitHubRepoOrLink": "https://github.com/alelud/latest",
      "EpicGitHubRepoOrLink": null,
      "CreatedAt": "2024-10-29T11:00:00.000Z",
      "CreatedBy": null,
      "Notes": "Initial version imported from config table"
    }
  ]
}
```

**Sortowanie:** Domyślnie po `CreatedAt DESC` (najnowsze najpierw)

### Response (filtrowane po modId)

**Request:**
```http
GET /susmodder-config-versions?modId=1
```

**Status:** `200 OK`

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
      "GitHubRepoOrLink": "https://github.com/tou/releases/v5.4.0",
      "EpicGitHubRepoOrLink": "https://github.com/tou/epic/v5.4.0",
      "CreatedAt": "2024-10-29T09:15:00.000Z",
      "CreatedBy": null,
      "Notes": null
    },
    {
      "VersionId": 2,
      "ModId": 1,
      "ModVersion": "5.3.1",
      "AmongVersion": "2024.10.01",
      "GitHubRepoOrLink": "https://github.com/tou/releases/v5.3.1",
      "EpicGitHubRepoOrLink": "https://github.com/tou/epic/v5.3.1",
      "CreatedAt": "2024-10-01T14:30:00.000Z",
      "CreatedBy": null,
      "Notes": null
    },
    {
      "VersionId": 1,
      "ModId": 1,
      "ModVersion": "5.3.0",
      "AmongVersion": "2024.09.01",
      "GitHubRepoOrLink": "https://github.com/tou/releases/v5.3.0",
      "EpicGitHubRepoOrLink": null,
      "CreatedAt": "2024-09-01T10:00:00.000Z",
      "CreatedBy": null,
      "Notes": "Initial version imported from config table"
    }
  ]
}
```

### Response (błąd - nieprawidłowy modId)

**Request:**
```http
GET /susmodder-config-versions?modId=999
```

**Status:** `200 OK`

**Uwaga:** Nie zwracamy 404, tylko pustą listę (zgodnie z REST best practices dla GET z filtrem)

```json
{
  "success": true,
  "modId": 999,
  "count": 0,
  "versions": []
}
```

### Response (błąd serwera)

**Status:** `500 Internal Server Error`

```json
{
  "success": false,
  "error": "Database error while fetching version history"
}
```

### Implementacja (routes/config.js)

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
 *         description: ID moda (opcjonalne). Jeśli podany, zwraca tylko wersje tego moda.
 *     responses:
 *       200:
 *         description: Historia wersji modów
 *         content:
 *           application/json:
 *             schema:
 *               type: object
 *               properties:
 *                 success:
 *                   type: boolean
 *                 modId:
 *                   type: integer
 *                   description: ID moda (jeśli filtrowano)
 *                 count:
 *                   type: integer
 *                   description: Liczba wersji
 *                 versions:
 *                   type: array
 *                   items:
 *                     type: object
 *                     properties:
 *                       VersionId:
 *                         type: integer
 *                       ModId:
 *                         type: integer
 *                       ModVersion:
 *                         type: string
 *                       AmongVersion:
 *                         type: string
 *                       GitHubRepoOrLink:
 *                         type: string
 *                       EpicGitHubRepoOrLink:
 *                         type: string
 *                       CreatedAt:
 *                         type: string
 *                         format: date-time
 *                       CreatedBy:
 *                         type: string
 *                       Notes:
 *                         type: string
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
      // Filtruj po modId
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
      // Pobierz wszystkie wersje
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

    // Rzutuj VersionId i ModId na integer
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

    // Dodaj modId do response jeśli filtrowano
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

---

## 📝 Modyfikacja `save_mod.php`

### Obecny Kod (przed zmianami)

```php
if (empty($modId)) {
    // INSERT nowego moda
    $stmt = $conn->prepare("INSERT INTO config (...) VALUES (...)");
} else {
    // UPDATE istniejącego moda
    $stmt = $conn->prepare("UPDATE config SET ... WHERE Id = ?");
}
```

**Problem:** Zawsze UPDATE - historia zostaje utracona.

### Nowy Kod (z wersjonowaniem)

```php
<?php
session_start();
mysqli_report(MYSQLI_REPORT_ERROR | MYSQLI_REPORT_STRICT);
require __DIR__ . '/db/db_config.php';
header('Content-Type: application/json');

if (!isset($_SESSION['loggedin']) || !$_SESSION['loggedin']) {
    exit(json_encode(["success" => false, "message" => "Brak dostępu!"]));
}

$conn = new mysqli(DB_SERVER, DB_USERNAME, DB_PASSWORD, DB_DATABASE, DB_PORT);

if ($conn->connect_error) {
    error_log("Connection failed: " . $conn->connect_error);
    exit(json_encode(["success" => false, "message" => "Błąd połączenia z bazą!"]));
}

// Pobierz dane z POST
$modId = $_POST['id'];
$modName = $_POST['name'] ?? null;
$modPng = $_POST['png'] ?? null;
$modPath = null;
$modUpdated = null;
$modGithub = $_POST['github'] ?? null;
$modEpicGithub = $_POST['epic_github'] ?? null;
$modType = $_POST['type'] ?? null;
$modDll = $_POST['dll'] ?? null;
$modVersion = $_POST['version'] ?? null;
$modAmongVersion = $_POST['among_version'] ?? null;
$modDescription = $_POST['description'] ?? null;

if (empty($modName)) {
    exit(json_encode(["success" => false, "message" => "Niektóre wymagane dane są nieobecne."]));
}

if (empty($modId)) {
    // ===== NOWY MOD =====
    // Pobierz nowy ID
    $stmt = $conn->prepare("SELECT MAX(Id) as max_id FROM config");
    $stmt->execute();
    $result = $stmt->get_result();
    $row = $result->fetch_assoc();
    $newModId = $row['max_id'] + 1;

    // INSERT do config
    $stmt = $conn->prepare("INSERT INTO config (Id, ModName, PngFileName, InstallPath, GitHubRepoOrLink, EpicGitHubRepoOrLink, ModType, DllInstallPath, ModVersion, LastUpdated, AmongVersion, Description)
                        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)");
    $stmt->bind_param("isssssssssss", $newModId, $modName, $modPng, $modPath, $modGithub, $modEpicGithub, $modType, $modDll, $modVersion, $modUpdated, $modAmongVersion, $modDescription);

    try {
        $stmt->execute();

        // INSERT do config_versions (pierwsza wersja nowego moda)
        $stmtVersion = $conn->prepare("INSERT INTO config_versions (ModId, ModVersion, AmongVersion, GitHubRepoOrLink, EpicGitHubRepoOrLink, Notes)
                                        VALUES (?, ?, ?, ?, ?, ?)");
        $notes = "Initial version of new mod";
        $stmtVersion->bind_param("isssss", $newModId, $modVersion, $modAmongVersion, $modGithub, $modEpicGithub, $notes);
        $stmtVersion->execute();

        echo json_encode(["success" => true, "message" => "Nowy mod utworzony", "modId" => $newModId]);
    } catch (Exception $e) {
        echo json_encode(["success" => false, "message" => "Błąd: " . $e->getMessage()]);
    }

} else {
    // ===== EDYCJA ISTNIEJĄCEGO MODA =====

    // Pobierz obecne wartości ModVersion i AmongVersion
    $stmt = $conn->prepare("SELECT ModVersion, AmongVersion FROM config WHERE Id = ?");
    $stmt->bind_param("i", $modId);
    $stmt->execute();
    $result = $stmt->get_result();
    $currentData = $result->fetch_assoc();

    if (!$currentData) {
        exit(json_encode(["success" => false, "message" => "Mod nie istnieje"]));
    }

    $oldModVersion = $currentData['ModVersion'];
    $oldAmongVersion = $currentData['AmongVersion'];

    // Sprawdź czy zmieniono wersję
    $versionChanged = ($oldModVersion !== $modVersion || $oldAmongVersion !== $modAmongVersion);

    if ($versionChanged) {
        // ===== ZMIANA WERSJI → INSERT do config_versions + UPDATE config =====

        // 1. Sprawdź czy taka kombinacja już istnieje
        $stmtCheck = $conn->prepare("SELECT VersionId FROM config_versions WHERE ModId = ? AND ModVersion = ? AND AmongVersion = ?");
        $stmtCheck->bind_param("iss", $modId, $modVersion, $modAmongVersion);
        $stmtCheck->execute();
        if ($stmtCheck->get_result()->num_rows > 0) {
            exit(json_encode(["success" => false, "message" => "Ta kombinacja wersji już istnieje w historii"]));
        }

        // 2. INSERT do config_versions (nowa wersja)
        $stmtVersion = $conn->prepare("INSERT INTO config_versions (ModId, ModVersion, AmongVersion, GitHubRepoOrLink, EpicGitHubRepoOrLink, Notes)
                                        VALUES (?, ?, ?, ?, ?, ?)");
        $notes = "Version changed from $oldModVersion to $modVersion";
        $stmtVersion->bind_param("isssss", $modId, $modVersion, $modAmongVersion, $modGithub, $modEpicGithub, $notes);
        $stmtVersion->execute();

        // 3. UPDATE w config (aktualizacja najnowszej wersji)
        $stmt = $conn->prepare("UPDATE config SET ModName = ?, PngFileName = ?, InstallPath = ?, GitHubRepoOrLink = ?, EpicGitHubRepoOrLink = ?, ModType = ?, DllInstallPath = ?, ModVersion = ?, LastUpdated = ?, AmongVersion = ?, Description = ?
                            WHERE Id = ?");
        $stmt->bind_param("sssssssssssi", $modName, $modPng, $modPath, $modGithub, $modEpicGithub, $modType, $modDll, $modVersion, $modUpdated, $modAmongVersion, $modDescription, $modId);

        try {
            $stmt->execute();
            echo json_encode(["success" => true, "message" => "Nowa wersja utworzona", "versionChanged" => true]);
        } catch (Exception $e) {
            echo json_encode(["success" => false, "message" => "Błąd: " . $e->getMessage()]);
        }

    } else {
        // ===== BRAK ZMIANY WERSJI → UPDATE config + UPDATE ostatniego wpisu w config_versions =====

        // 1. UPDATE w config (poprawka)
        $stmt = $conn->prepare("UPDATE config SET ModName = ?, PngFileName = ?, InstallPath = ?, GitHubRepoOrLink = ?, EpicGitHubRepoOrLink = ?, ModType = ?, DllInstallPath = ?, ModVersion = ?, LastUpdated = ?, AmongVersion = ?, Description = ?
                            WHERE Id = ?");
        $stmt->bind_param("sssssssssssi", $modName, $modPng, $modPath, $modGithub, $modEpicGithub, $modType, $modDll, $modVersion, $modUpdated, $modAmongVersion, $modDescription, $modId);

        try {
            $stmt->execute();

            // 2. UPDATE ostatniego wpisu w config_versions (synchronizacja linków)
            $stmtVersion = $conn->prepare("UPDATE config_versions
                                            SET GitHubRepoOrLink = ?, EpicGitHubRepoOrLink = ?
                                            WHERE ModId = ? AND ModVersion = ? AND AmongVersion = ?");
            $stmtVersion->bind_param("ssiss", $modGithub, $modEpicGithub, $modId, $modVersion, $modAmongVersion);
            $stmtVersion->execute();

            echo json_encode(["success" => true, "message" => "Mod zaktualizowany (bez zmiany wersji)", "versionChanged" => false]);
        } catch (Exception $e) {
            echo json_encode(["success" => false, "message" => "Błąd: " . $e->getMessage()]);
        }
    }
}

$conn->close();
?>
```

### Kluczowe Zmiany

**1. Wykrywanie zmiany wersji:**
```php
$versionChanged = ($oldModVersion !== $modVersion || $oldAmongVersion !== $modAmongVersion);
```
- Porównanie stringów (nie numeryczne!)
- Zmiana ModVersion ALBO AmongVersion = zmiana wersji

**2. Logika INSERT vs UPDATE:**
```php
if ($versionChanged) {
    // INSERT do config_versions + UPDATE config
} else {
    // UPDATE config + UPDATE config_versions (ostatni wpis)
}
```

**3. Walidacja duplikatów:**
```php
// Sprawdź czy kombinacja (ModId, ModVersion, AmongVersion) już istnieje
SELECT VersionId FROM config_versions WHERE ModId = ? AND ModVersion = ? AND AmongVersion = ?
```

---

## 🔄 Przepływ Danych

### Scenariusz 1: Admin Zmienia Wersję

**Request (save_mod.php):**
```
POST /save_mod.php
id=1&name=Town of Us&version=5.4.0&among_version=2024.10.29&github=...
```

**Operacje:**
1. `SELECT ModVersion, AmongVersion FROM config WHERE Id = 1`
   - Pobierz obecne wersje: `5.3.1`, `2024.10.01`
2. Wykrycie zmiany: `5.3.1 !== 5.4.0` → `versionChanged = true`
3. `INSERT INTO config_versions (ModId=1, ModVersion='5.4.0', ...)`
4. `UPDATE config SET ModVersion='5.4.0', AmongVersion='2024.10.29' WHERE Id=1`

**Response:**
```json
{
  "success": true,
  "message": "Nowa wersja utworzona",
  "versionChanged": true
}
```

**Użytkownik sprawdza historię:**
```
GET /susmodder-config-versions?modId=1
```

**Response:**
```json
{
  "success": true,
  "modId": 1,
  "count": 2,
  "versions": [
    {"VersionId": 2, "ModVersion": "5.4.0", ...},  // Nowa
    {"VersionId": 1, "ModVersion": "5.3.1", ...}   // Stara
  ]
}
```

### Scenariusz 2: Admin Poprawia Link (bez zmiany wersji)

**Request (save_mod.php):**
```
POST /save_mod.php
id=1&name=Town of Us&version=5.4.0&among_version=2024.10.29&github=...fixed
```

**Operacje:**
1. `SELECT ModVersion, AmongVersion FROM config WHERE Id = 1`
   - Pobierz obecne wersje: `5.4.0`, `2024.10.29`
2. Brak zmiany: `5.4.0 === 5.4.0` → `versionChanged = false`
3. `UPDATE config SET GitHubRepoOrLink='...fixed' WHERE Id=1`
4. `UPDATE config_versions SET GitHubRepoOrLink='...fixed' WHERE ModId=1 AND ModVersion='5.4.0'`

**Response:**
```json
{
  "success": true,
  "message": "Mod zaktualizowany (bez zmiany wersji)",
  "versionChanged": false
}
```

---

## 📊 Przykłady Użycia API

### JavaScript (Frontend)

```javascript
// Pobierz najnowsze wersje modów
async function getLatestMods() {
  const response = await fetch('/susmodder-config');
  const mods = await response.json();
  return mods;
}

// Pobierz historię wersji konkretnego moda
async function getModVersionHistory(modId) {
  const response = await fetch(`/susmodder-config-versions?modId=${modId}`);
  const data = await response.json();
  return data.versions;
}

// Użycie
const mods = await getLatestMods();
console.log('Najnowsze mody:', mods);

const history = await getModVersionHistory(1);
console.log('Historia Town of Us:', history);
```

### Python

```python
import requests

# Pobierz najnowsze wersje
response = requests.get('https://api.susmodder.app/susmodder-config')
mods = response.json()

# Pobierz historię wersji
response = requests.get('https://api.susmodder.app/susmodder-config-versions?modId=1')
history = response.json()['versions']

for version in history:
    print(f"{version['ModVersion']} - {version['CreatedAt']}")
```

### cURL

```bash
# Pobierz najnowsze wersje
curl https://api.susmodder.app/susmodder-config

# Pobierz wszystkie wersje
curl https://api.susmodder.app/susmodder-config-versions

# Pobierz wersje Town of Us
curl https://api.susmodder.app/susmodder-config-versions?modId=1
```

---

## 🎯 Kluczowe Zasady

1. **Kompatybilność wstecz:** `/susmodder-config` bez zmian
2. **Publiczny dostęp:** Oba endpointy bez autoryzacji
3. **Minimalizm:** Tylko 4 wersjonowane pola w `config_versions`
4. **Automatyka:** `save_mod.php` automatycznie wykrywa zmianę wersji
5. **Synchronizacja:** UPDATE bez zmiany wersji aktualizuje config + config_versions

---

**Ostatnia aktualizacja:** 2025-10-22
**Następny krok:** Przewodnik migracji (03_MIGRATION_GUIDE.md)
