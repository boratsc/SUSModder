# Specyfikacja API - Compatibility Matrix

## 🌐 Przegląd Endpointów

| Metoda | Endpoint | Opis | Uwierzytelnienie |
|--------|----------|------|------------------|
| GET | `/api/compatibility` | Pobierz kompatybilności | ❌ Publiczny |
| GET | `/api/compatibility/:id` | Pobierz szczegóły wpisu | ❌ Publiczny |
| POST | `/api/compatibility` | Utwórz nowy wpis | ✅ Wymagane (admin) |
| PUT | `/api/compatibility/:id` | Zaktualizuj wpis | ✅ Wymagane (admin) |
| DELETE | `/api/compatibility/:id` | Usuń wpis | ✅ Wymagane (admin) |
| GET | `/api/compatibility/matrix` | Pełna macierz (UI) | ✅ Wymagane (admin) |
| GET | `/api/compatibility/history/:id` | Historia zmian | ❌ Publiczny |

## 📖 Szczegółowa Dokumentacja

---

## 1️⃣ GET `/api/compatibility`

**Opis**: Pobierz listę kompatybilności dla konkretnego moda (DLL lub FULL)

### Query Parameters

| Parametr | Typ | Wymagany | Opis |
|----------|-----|----------|------|
| `fullModId` | integer | Nie* | ID moda FULL |
| `fullModVersion` | string | Nie | Wersja moda FULL (domyślnie: aktualna) |
| `dllModId` | integer | Nie* | ID moda DLL |
| `dllModVersion` | string | Nie | Wersja moda DLL (domyślnie: aktualna) |
| `status` | string | Nie | Filtruj po statusie (F,W,NT,NW) - może być lista: `F,W` |
| `includeUntested` | boolean | Nie | Czy uwzględniać nieprzetestowane (domyślnie: true) |

**\*Uwaga**: Wymagany **ALBO** `fullModId` **ALBO** `dllModId` (nie oba naraz)

### Przykład 1: Zapytanie o mod DLL

**Request:**
```http
GET /api/compatibility?dllModId=5&status=F,W
```

**Response:** (200 OK)
```json
{
  "success": true,
  "query": {
    "type": "dll",
    "modId": 5,
    "modName": "AleLuduMod",
    "modVersion": "latest"
  },
  "count": 3,
  "compatibilities": [
    {
      "id": 123,
      "fullMod": {
        "id": 1,
        "name": "Town of Us",
        "version": "5.3.1",
        "currentVersion": "5.3.1"
      },
      "status": "F",
      "testedDate": "2025-10-15T10:30:00Z",
      "testedBy": "admin",
      "amongUsVersion": "2024.3.5",
      "notes": "Działa bez problemów, wszystkie funkcje OK",
      "isCurrentVersion": true
    },
    {
      "id": 124,
      "fullMod": {
        "id": 4,
        "name": "The Other Roles",
        "version": "4.8.0",
        "currentVersion": "4.9.0"
      },
      "status": "W",
      "testedDate": "2025-10-14T15:20:00Z",
      "testedBy": "tester123",
      "amongUsVersion": "2024.3.5",
      "notes": "Działa poprawnie z małymi lagami",
      "isCurrentVersion": false,
      "warning": "Tested on older version (4.8.0), current is 4.9.0"
    },
    {
      "id": 125,
      "fullMod": {
        "id": 6,
        "name": "ToH Enhanced",
        "version": "2.4.0",
        "currentVersion": "2.4.0"
      },
      "status": "W",
      "testedDate": "2025-10-16T09:00:00Z",
      "testedBy": "admin",
      "amongUsVersion": "2024.3.5",
      "notes": null,
      "isCurrentVersion": true
    }
  ]
}
```

### Przykład 2: Zapytanie o mod FULL

**Request:**
```http
GET /api/compatibility?fullModId=1&fullModVersion=5.3.1
```

**Response:** (200 OK)
```json
{
  "success": true,
  "query": {
    "type": "full",
    "modId": 1,
    "modName": "Town of Us",
    "modVersion": "5.3.1"
  },
  "count": 4,
  "compatibilities": [
    {
      "id": 123,
      "dllMod": {
        "id": 5,
        "name": "AleLuduMod",
        "version": "latest",
        "currentVersion": "latest"
      },
      "status": "F",
      "testedDate": "2025-10-15T10:30:00Z",
      "testedBy": "admin",
      "amongUsVersion": "2024.3.5",
      "notes": "Działa bez problemów, wszystkie funkcje OK",
      "isCurrentVersion": true
    },
    {
      "id": 126,
      "dllMod": {
        "id": 8,
        "name": "AUnlocker",
        "version": "latest",
        "currentVersion": "latest"
      },
      "status": "W",
      "testedDate": "2025-10-15T11:00:00Z",
      "testedBy": "admin",
      "amongUsVersion": "2024.3.5",
      "notes": "Działa, ale czasami laguje przy wielu graczach",
      "isCurrentVersion": true
    },
    {
      "id": 127,
      "dllMod": {
        "id": 9,
        "name": "LevelImposter",
        "version": "Custom Beta 0.20.4",
        "currentVersion": "Custom Beta 0.20.4"
      },
      "status": "NW",
      "testedDate": "2025-10-16T14:20:00Z",
      "testedBy": "tester123",
      "amongUsVersion": "2024.3.5",
      "notes": "Crash przy ładowaniu custom map",
      "issuesUrl": "https://github.com/LevelImposter/issues/123",
      "isCurrentVersion": true
    },
    {
      "id": 128,
      "dllMod": {
        "id": 10,
        "name": "CrowdedMod",
        "version": "2.10.0",
        "currentVersion": "2.10.0"
      },
      "status": "NT",
      "testedDate": null,
      "testedBy": null,
      "amongUsVersion": null,
      "notes": null,
      "isCurrentVersion": true
    }
  ]
}
```

### Przykład 3: Filtrowanie tylko polecanych i działających

**Request:**
```http
GET /api/compatibility?dllModId=5&status=F,W&includeUntested=false
```

### Błędy

**400 Bad Request** - Brak wymaganego parametru
```json
{
  "success": false,
  "error": "Either fullModId or dllModId is required"
}
```

**404 Not Found** - Mod nie istnieje
```json
{
  "success": false,
  "error": "Mod with id 999 not found"
}
```

---

## 2️⃣ GET `/api/compatibility/:id`

**Opis**: Pobierz szczegóły konkretnego wpisu kompatybilności

### Path Parameters

| Parametr | Typ | Opis |
|----------|-----|------|
| `id` | integer | ID wpisu w `compatibility_matrix` |

### Response

**200 OK**
```json
{
  "success": true,
  "compatibility": {
    "id": 123,
    "fullMod": {
      "id": 1,
      "name": "Town of Us",
      "version": "5.3.1",
      "currentVersion": "5.3.1"
    },
    "dllMod": {
      "id": 5,
      "name": "AleLuduMod",
      "version": "latest",
      "currentVersion": "latest"
    },
    "status": "F",
    "testedDate": "2025-10-15T10:30:00Z",
    "testedBy": "admin",
    "amongUsVersion": "2024.3.5",
    "notes": "Działa bez problemów, wszystkie funkcje OK",
    "issuesUrl": null,
    "createdAt": "2025-10-15T10:30:00Z",
    "updatedAt": "2025-10-15T10:30:00Z",
    "createdBy": "admin",
    "updatedBy": "admin"
  }
}
```

**404 Not Found**
```json
{
  "success": false,
  "error": "Compatibility entry with id 999 not found"
}
```

---

## 3️⃣ POST `/api/compatibility`

**Opis**: Utwórz nowy wpis kompatybilności

**Uwierzytelnienie**: ✅ Wymagane (Bearer Token)

### Request Body

```json
{
  "fullModId": 1,
  "dllModId": 5,
  "fullModVersion": "5.3.1",
  "dllModVersion": "latest",
  "status": "F",
  "testedBy": "admin",
  "amongUsVersion": "2024.3.5",
  "notes": "Działa bez problemów, wszystkie funkcje OK",
  "issuesUrl": null
}
```

### Walidacja

| Pole | Wymagane | Walidacja |
|------|----------|-----------|
| `fullModId` | ✅ | Musi istnieć w `config` z `ModType='full'` |
| `dllModId` | ✅ | Musi istnieć w `config` z `ModType='dll'` |
| `fullModVersion` | ✅ | String, max 50 znaków |
| `dllModVersion` | ✅ | String, max 50 znaków |
| `status` | ✅ | Enum: F, W, NT, NW |
| `testedBy` | ❌ | String, max 100 znaków |
| `amongUsVersion` | ❌ | String, max 50 znaków |
| `notes` | ❌ | Text |
| `issuesUrl` | ❌ | URL, max 255 znaków |

### Response

**201 Created**
```json
{
  "success": true,
  "message": "Compatibility entry created successfully",
  "id": 129,
  "compatibility": {
    "id": 129,
    "fullModId": 1,
    "dllModId": 5,
    "fullModVersion": "5.3.1",
    "dllModVersion": "latest",
    "status": "F",
    "testedDate": "2025-10-22T10:00:00Z",
    "testedBy": "admin",
    "amongUsVersion": "2024.3.5",
    "notes": "Działa bez problemów, wszystkie funkcje OK",
    "issuesUrl": null
  }
}
```

**400 Bad Request** - Błędne dane
```json
{
  "success": false,
  "error": "Invalid status. Must be one of: F, W, NT, NW"
}
```

**409 Conflict** - Wpis już istnieje
```json
{
  "success": false,
  "error": "Compatibility entry already exists for this combination",
  "existingId": 123
}
```

---

## 4️⃣ PUT `/api/compatibility/:id`

**Opis**: Zaktualizuj istniejący wpis kompatybilności

**Uwierzytelnienie**: ✅ Wymagane (Bearer Token)

### Request Body

```json
{
  "status": "W",
  "notes": "Zaktualizowano po nowych testach - drobne lagi",
  "issuesUrl": "https://github.com/example/issues/456"
}
```

### Pola do aktualizacji

| Pole | Możliwość zmiany | Uwagi |
|------|------------------|-------|
| `fullModId` | ❌ | Nie można zmienić |
| `dllModId` | ❌ | Nie można zmienić |
| `fullModVersion` | ❌ | Nie można zmienić |
| `dllModVersion` | ❌ | Nie można zmienić |
| `status` | ✅ | Można zmienić |
| `testedBy` | ✅ | Można zmienić |
| `amongUsVersion` | ✅ | Można zmienić |
| `notes` | ✅ | Można zmienić |
| `issuesUrl` | ✅ | Można zmienić |

**Uwaga**: Aby zmienić wersje modów, trzeba utworzyć nowy wpis.

### Response

**200 OK**
```json
{
  "success": true,
  "message": "Compatibility entry updated successfully",
  "compatibility": {
    "id": 123,
    "status": "W",
    "notes": "Zaktualizowano po nowych testach - drobne lagi",
    "updatedAt": "2025-10-22T11:30:00Z",
    "updatedBy": "admin"
  }
}
```

**404 Not Found**
```json
{
  "success": false,
  "error": "Compatibility entry with id 999 not found"
}
```

---

## 5️⃣ DELETE `/api/compatibility/:id`

**Opis**: Usuń wpis kompatybilności

**Uwierzytelnienie**: ✅ Wymagane (Bearer Token)

### Response

**200 OK**
```json
{
  "success": true,
  "message": "Compatibility entry deleted successfully"
}
```

**404 Not Found**
```json
{
  "success": false,
  "error": "Compatibility entry with id 999 not found"
}
```

---

## 6️⃣ GET `/api/compatibility/matrix`

**Opis**: Pobierz pełną macierz kompatybilności (dla interfejsu administracyjnego)

**Uwierzytelnienie**: ✅ Wymagane (Bearer Token)

### Query Parameters

| Parametr | Typ | Opis |
|----------|-----|------|
| `onlyCurrentVersions` | boolean | Tylko aktualne wersje (domyślnie: true) |

### Response

**200 OK**
```json
{
  "success": true,
  "fullMods": [
    {
      "id": 1,
      "name": "Town of Us",
      "version": "5.3.1"
    },
    {
      "id": 4,
      "name": "The Other Roles",
      "version": "4.8.0"
    },
    {
      "id": 6,
      "name": "ToH Enhanced",
      "version": "2.4.0"
    }
  ],
  "dllMods": [
    {
      "id": 5,
      "name": "AleLuduMod",
      "version": "latest"
    },
    {
      "id": 8,
      "name": "AUnlocker",
      "version": "latest"
    },
    {
      "id": 9,
      "name": "LevelImposter",
      "version": "Custom Beta 0.20.4"
    }
  ],
  "matrix": [
    {
      "fullModId": 1,
      "dllModId": 5,
      "status": "F",
      "matrixId": 123,
      "testedDate": "2025-10-15T10:30:00Z"
    },
    {
      "fullModId": 1,
      "dllModId": 8,
      "status": "W",
      "matrixId": 126,
      "testedDate": "2025-10-15T11:00:00Z"
    },
    {
      "fullModId": 1,
      "dllModId": 9,
      "status": "NW",
      "matrixId": 127,
      "testedDate": "2025-10-16T14:20:00Z"
    },
    {
      "fullModId": 4,
      "dllModId": 5,
      "status": "NT",
      "matrixId": null,
      "testedDate": null
    }
    // ... więcej kombinacji
  ]
}
```

**Struktura dla UI (tabela)**:
```
              | AleLuduMod | AUnlocker | LevelImposter |
--------------|------------|-----------|---------------|
Town of Us    |     F      |     W     |      NW       |
The Other R.  |    NT      |     F     |       W       |
ToH Enhanced  |     W      |    NT     |      NT       |
```

---

## 7️⃣ GET `/api/compatibility/history/:id`

**Opis**: Pobierz historię zmian dla konkretnego wpisu (przyszłościowe)

### Response

**200 OK**
```json
{
  "success": true,
  "history": [
    {
      "id": 1,
      "oldStatus": "NT",
      "newStatus": "F",
      "changedAt": "2025-10-15T10:30:00Z",
      "changedBy": "admin",
      "reason": "Przetestowane pozytywnie"
    },
    {
      "id": 2,
      "oldStatus": "F",
      "newStatus": "W",
      "changedAt": "2025-10-22T11:30:00Z",
      "changedBy": "admin",
      "reason": "Wykryto drobne lagi"
    }
  ]
}
```

---

## 🔒 Uwierzytelnienie

### Bearer Token
```http
Authorization: Bearer e4a1c7b2f3d8e9a0b5c6d7e8f9a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9
```

### Middleware
```javascript
const requireAuthToken = require('../middleware/auth');
router.post('/compatibility', requireAuthToken, async (req, res) => {
  // ...
});
```

---

## 📊 Kody Odpowiedzi HTTP

| Kod | Znaczenie |
|-----|-----------|
| 200 | OK - Operacja zakończona sukcesem |
| 201 | Created - Zasób utworzony |
| 400 | Bad Request - Błędne dane wejściowe |
| 401 | Unauthorized - Brak lub nieprawidłowy token |
| 404 | Not Found - Zasób nie znaleziony |
| 409 | Conflict - Konflikt (np. duplikat) |
| 500 | Internal Server Error - Błąd serwera |

---

## 🚀 Przykładowe Użycie w Kliencie

### JavaScript (Fetch API)

```javascript
// Pobierz kompatybilności dla DLL
async function getDllCompatibilities(dllModId) {
  const response = await fetch(
    `https://api.susmodder.app/api/compatibility?dllModId=${dllModId}&status=F,W`
  );
  const data = await response.json();
  return data.compatibilities;
}

// Utwórz nowy wpis (wymaga tokena)
async function createCompatibility(compatibilityData) {
  const response = await fetch(
    'https://api.susmodder.app/api/compatibility',
    {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': 'Bearer YOUR_TOKEN_HERE'
      },
      body: JSON.stringify(compatibilityData)
    }
  );
  return await response.json();
}

// Użycie
const compatibilities = await getDllCompatibilities(5);
console.log(compatibilities);
```

### cURL

```bash
# Pobierz kompatybilności
curl "https://api.susmodder.app/api/compatibility?dllModId=5"

# Utwórz nowy wpis
curl -X POST "https://api.susmodder.app/api/compatibility" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -d '{
    "fullModId": 1,
    "dllModId": 5,
    "fullModVersion": "5.3.1",
    "dllModVersion": "latest",
    "status": "F",
    "notes": "Działa bez problemów"
  }'
```

---

## 📝 Notatki Implementacyjne

### Rate Limiting
- GET endpoints: 100 req/min
- POST/PUT/DELETE: 20 req/min

### Caching
- GET `/api/compatibility`: Cache 5 minut
- GET `/api/compatibility/matrix`: Cache 10 minut
- Invalidacja przy POST/PUT/DELETE

### Logging
- Loguj wszystkie operacje POST/PUT/DELETE
- Zapisuj kto, kiedy, co zmienił
- Przechowuj przez 90 dni
