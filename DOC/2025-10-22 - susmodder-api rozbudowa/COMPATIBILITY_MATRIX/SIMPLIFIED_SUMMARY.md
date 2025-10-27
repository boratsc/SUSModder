# ✅ Compatibility Matrix - Uproszczona Dokumentacja

## 🎯 Kluczowe Zmiany

### Uproszczenie Struktury Bazy Danych

**Usunięto zbędne pola:**
- ❌ `TestedDate` - niepotrzebne, tylko dwie osoby zarządzają
- ❌ `TestedBy` - niepotrzebne, mały zespół
- ❌ `AmongUsVersion` - wersja vanilla wpływa na moda FULL, nie na kompatybilność
- ❌ `Notes` - niepotrzebne szczegóły
- ❌ `IssuesUrl` - niepotrzebne
- ❌ `UpdatedAt` - niepotrzebne
- ❌ `CreatedBy` / `UpdatedBy` - niepotrzebne

**Finalna struktura:**
```sql
CREATE TABLE compatibility_matrix (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    FullModId INT NOT NULL,           -- FK do config (ModType='full')
    DllModId INT NOT NULL,            -- FK do config (ModType='dll')
    FullModVersion VARCHAR(50) NOT NULL,
    DllModVersion VARCHAR(50) NOT NULL,
    CompatibilityStatus ENUM('F', 'W', 'NT', 'NW') NOT NULL DEFAULT 'NT',
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    FOREIGN KEY (FullModId) REFERENCES config(Id) ON DELETE CASCADE,
    FOREIGN KEY (DllModId) REFERENCES config(Id) ON DELETE CASCADE,
    INDEX idx_full_mod (FullModId, FullModVersion),
    INDEX idx_dll_mod (DllModId, DllModVersion),
    UNIQUE KEY unique_compatibility (FullModId, DllModId, FullModVersion, DllModVersion)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

## 🔄 Obsługa Update Wersji

### Co się dzieje przy aktualizacji moda?

**Scenariusz:**
1. Town of Us v5.3.1 + AleLuduMod = **F** (Favorite) ✅
2. Update Town of Us → v5.4.0
3. **Automatycznie:** Tworzy się nowy wpis: Town of Us v5.4.0 + AleLuduMod = **NT** ⚠️
4. Stary wpis (5.3.1) pozostaje jako historia

**Reguła:**
> **Przy update moda FULL lub DLL → wszystkie F/W automatycznie kopiują się jako NT dla nowej wersji**

### Implementacja

```javascript
// Przy update wersji moda w config
async function onModVersionUpdate(modId, newVersion, oldVersion, modType) {
  if (modType === 'full') {
    // Kopiuj wszystkie F i W jako NT dla nowej wersji FULL
    await db.query(`
      INSERT IGNORE INTO compatibility_matrix 
      (FullModId, DllModId, FullModVersion, DllModVersion, CompatibilityStatus)
      SELECT FullModId, DllModId, ?, DllModVersion, 'NT'
      FROM compatibility_matrix 
      WHERE FullModId = ? 
        AND FullModVersion = ?
        AND CompatibilityStatus IN ('F', 'W')
    `, [newVersion, modId, oldVersion]);
    
  } else if (modType === 'dll') {
    // Kopiuj wszystkie F i W jako NT dla nowej wersji DLL
    await db.query(`
      INSERT IGNORE INTO compatibility_matrix 
      (FullModId, DllModId, FullModVersion, DllModVersion, CompatibilityStatus)
      SELECT FullModId, DllModId, FullModVersion, ?, 'NT'
      FROM compatibility_matrix 
      WHERE DllModId = ? 
        AND DllModVersion = ?
        AND CompatibilityStatus IN ('F', 'W')
    `, [newVersion, modId, oldVersion]);
  }
}
```

## 📊 Przykłady Użycia

### Sprawdź kompatybilności dla DLL
```bash
curl "https://api.susmodder.app/api/compatibility?dllModId=5"
```

### Dodaj nową kompatybilność
```bash
curl -X POST "https://api.susmodder.app/api/compatibility" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer TOKEN" \
  -d '{
    "fullModId": 1,
    "dllModId": 5,
    "fullModVersion": "5.3.1",
    "dllModVersion": "latest",
    "status": "F"
  }'
```

### Zaktualizuj status
```bash
curl -X PUT "https://api.susmodder.app/api/compatibility/123" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer TOKEN" \
  -d '{"status": "W"}'
```

## 🚀 Wdrożenie

### Krok 1: Uruchom migracje
```bash
cd /srv/synapsekit-boracik/migrations
./run-migrations.sh all
```

### Krok 2: Zaimplementuj API
Plik: `/srv/synapsekit-boracik/susmodder-api/routes/compatibility.js`

Endpointy:
- `GET /api/compatibility?fullModId=X` lub `?dllModId=X`
- `POST /api/compatibility`
- `PUT /api/compatibility/:id`
- `DELETE /api/compatibility/:id`

### Krok 3: Integracja z susmodder desktop
Aplikacja desktop może teraz:
- ✅ Sprawdzić które DLL są kompatybilne z wybranym modem FULL
- ✅ Zobaczyć rekomendacje (status F = Favorite)
- ✅ Ostrzec użytkownika o niekompatybilnych kombinacjach (status NW)

## 📝 API Request/Response

### GET /api/compatibility?dllModId=5

**Response:**
```json
{
  "success": true,
  "compatibilities": [
    {
      "id": 123,
      "fullMod": {
        "id": 1,
        "name": "Town of Us",
        "version": "5.3.1"
      },
      "status": "F",
      "isCurrentVersion": true
    },
    {
      "id": 124,
      "fullMod": {
        "id": 4,
        "name": "The Other Roles",
        "version": "4.8.0"
      },
      "status": "W",
      "isCurrentVersion": true
    }
  ]
}
```

### POST /api/compatibility

**Request:**
```json
{
  "fullModId": 1,
  "dllModId": 5,
  "fullModVersion": "5.3.1",
  "dllModVersion": "latest",
  "status": "F"
}
```

**Response:**
```json
{
  "success": true,
  "message": "Compatibility entry created",
  "id": 125
}
```

## 🎯 Statusy

| Kod | Nazwa | Opis | Kolor |
|-----|-------|------|-------|
| **F** | Favorite | Działa idealnie, polecane | 🟢 |
| **W** | Works | Działa poprawnie | 🔵 |
| **NT** | Not Tested | Nieprzetestowane | ⚪ |
| **NW** | Not Work | Nie działa | 🔴 |

## ⚡ Najważniejsze Zasady

1. **Każda kombinacja (FullMod + DllMod + wersje) = osobny wpis**
2. **Przy update → F/W kopiuje się jako NT**
3. **Historia pozostaje zachowana**
4. **Proste, bez zbędnych szczegółów**

## 📂 Struktura Plików

```
/srv/synapsekit-boracik/
├── migrations/
│   ├── 001_create_compatibility_matrix.sql  ← Uproszczona struktura
│   ├── 002_populate_initial_data.sql        ← Uproszczone dane
│   └── run-migrations.sh                    ← Skrypt wdrożenia
│
├── DOC/COMPATIBILITY_MATRIX/
│   ├── 00_PROJECT_SUMMARY.md                ← Zaktualizowane
│   ├── 01_DATABASE_DESIGN.md                ← Uproszczone
│   ├── 02_API_SPECIFICATION.md              ← Uproszczone
│   ├── 03_VERSION_HANDLING.md               ← Kluczowe: auto NT przy update
│   └── SIMPLIFIED_SUMMARY.md                ← Ten plik
│
└── susmodder-api/routes/
    └── compatibility.js                     ← Do implementacji
```

## ✅ Gotowe do Wdrożenia

Dokumentacja została uproszczona zgodnie z wymaganiami:
- ✅ Tylko niezbędne pola w bazie
- ✅ Automatyczne kopiowanie jako NT przy update
- ✅ Brak zbędnych metadanych (who, when, notes, etc.)
- ✅ Prosty flow dla małego zespołu
- ✅ Skupienie na funkcjonalności dla susmodder desktop

---

**Wersja:** 2.0 (Simplified)  
**Data:** 2025-10-22  
**Status:** ✅ Ready for Implementation
