# Strategia Obsługi Wersji - Compatibility Matrix

## 🎯 Problem do Rozwiązania

### Scenariusz Problematyczny

1. **Stan początkowy:**
   - Town of Us v5.3.1 + AleLuduMod "latest" = **F** (Favorite) ✅
   - Wpis w bazie: `(FullModId=1, DllModId=5, FullVersion="5.3.1", DllVersion="latest", Status="F")`

2. **Aktualizacja moda FULL:**
   - Town of Us zostaje zaktualizowany do **v5.4.0**
   - W tabeli `config`: `ModVersion` dla Town of Us zmienia się na "5.4.0"

3. **Rozwiązanie:**
   - Wszystkie wpisy z F/W dla starej wersji zostają **skopiowane jako NT** dla nowej wersji
   - Stary wpis (5.3.1) pozostaje jako historia
   - Nowy wpis (5.4.0) wymaga ponownego przetestowania

### Dlaczego To Krytyczne?

- **Kompatybilność może się zmienić** między wersjami
- Użytkownicy muszą wiedzieć czy ich kombinacja działa
- Historia musi być zachowana dla starszych wersji
- Nie możemy nadpisywać starych wpisów

## 🏗️ Architektura Rozwiązania

### Koncepcja: Wersjonowanie per Kombinacja

Każda kombinacja `(FullMod, DllMod, FullVersion, DllVersion)` jest **unikalnym wpisem**:

```sql
UNIQUE KEY unique_compatibility (FullModId, DllModId, FullModVersion, DllModVersion)
```

### Przykładowe Dane

```
Id  | FullModId | DllModId | FullVer | DllVer  | Status | TestedDate
----|-----------|----------|---------|---------|--------|------------
1   | 1 (ToU)   | 5 (AleL) | 5.3.0   | latest  | F      | 2025-09-01
2   | 1 (ToU)   | 5 (AleL) | 5.3.1   | latest  | F      | 2025-10-15
3   | 1 (ToU)   | 5 (AleL) | 5.4.0   | latest  | NT     | NULL
4   | 1 (ToU)   | 8 (AUnl) | 5.3.1   | latest  | W      | 2025-10-15
5   | 1 (ToU)   | 8 (AUnl) | 5.4.0   | latest  | NT     | NULL
```

**Kluczowe obserwacje:**
- Wpis #2: ToU 5.3.1 + AleL = **F** (przetestowane) ✅
- Wpis #3: ToU 5.4.0 + AleL = **NT** (nieprzetestowane) ⚠️
- **Historia zachowana**: możemy wrócić do 5.3.1 i wiedzieć że działało

## 📋 Przepływy Obsługi Wersji

### Scenariusz 1: Aktualizacja Moda FULL

**Stan początkowy:**
```
config.Id=1: ModName="Town of Us", ModVersion="5.3.1"
compatibility_matrix: (1, 5, "5.3.1", "latest", "F")
```

**Krok 1: Admin aktualizuje mod FULL w panelu**
```sql
UPDATE config 
SET ModVersion = "5.4.0", 
    LastUpdated = NOW()
WHERE Id = 1;
```

**Krok 2: System automatycznie tworzy nowe wpisy NT**

**Automatyczne tworzenie przy aktualizacji wersji:**
```javascript
// Logika aplikacji przy update wersji moda
async function onModVersionUpdate(modId, newVersion, oldVersion, modType) {
  if (modType === 'full') {
    // Znajdź wszystkie DLL kompatybilne ze starą wersją (tylko F i W)
    const oldCompatibilities = await db.query(`
      SELECT DllModId, DllModVersion 
      FROM compatibility_matrix 
      WHERE FullModId = ? 
        AND FullModVersion = ?
        AND CompatibilityStatus IN ('F', 'W')
    `, [modId, oldVersion]);
    
    // Utwórz nowe wpisy NT dla nowej wersji
    for (const compat of oldCompatibilities) {
      await db.query(`
        INSERT IGNORE INTO compatibility_matrix 
        (FullModId, DllModId, FullModVersion, DllModVersion, CompatibilityStatus)
        VALUES (?, ?, ?, ?, 'NT')
      `, [modId, compat.DllModId, newVersion, compat.DllModVersion]);
    }
  } else if (modType === 'dll') {
    // Znajdź wszystkie FULL kompatybilne ze starą wersją (tylko F i W)
    const oldCompatibilities = await db.query(`
      SELECT FullModId, FullModVersion 
      FROM compatibility_matrix 
      WHERE DllModId = ? 
        AND DllModVersion = ?
        AND CompatibilityStatus IN ('F', 'W')
    `, [modId, oldVersion]);
    
    // Utwórz nowe wpisy NT dla nowej wersji
    for (const compat of oldCompatibilities) {
      await db.query(`
        INSERT IGNORE INTO compatibility_matrix 
        (FullModId, DllModId, FullModVersion, DllModVersion, CompatibilityStatus)
        VALUES (?, ?, ?, ?, 'NT')
      `, [compat.FullModId, modId, compat.FullModVersion, newVersion]);
    }
  }
}
```

**Wynik:**
```
Id  | FullModId | DllModId | FullVer | DllVer  | Status | TestedDate
----|-----------|----------|---------|---------|--------|------------
2   | 1         | 5        | 5.3.1   | latest  | F      | 2025-10-15  ← Stary, zachowany
3   | 1         | 5        | 5.4.0   | latest  | NT     | NULL        ← Nowy, do przetestowania
4   | 1         | 8        | 5.3.1   | latest  | W      | 2025-10-15  ← Stary, zachowany
5   | 1         | 8        | 5.4.0   | latest  | NT     | NULL        ← Nowy, do przetestowania
```

**Krok 3: Admin testuje i aktualizuje statusy**
```http
PUT /api/compatibility/3
{
  "status": "F",
  "testedBy": "admin",
  "testedDate": "2025-10-22",
  "notes": "Działa bez problemów z nową wersją"
}
```

### Scenariusz 2: Aktualizacja Moda DLL

**Stan początkowy:**
```
config.Id=5: ModName="AleLuduMod", ModVersion="latest"
compatibility_matrix: (1, 5, "5.3.1", "latest", "F")
```

**Problem:** DLL używa "latest" zamiast konkretnej wersji

**Rozwiązanie A: Wprowadzenie Konkretnych Wersji**
```javascript
// Przy dodawaniu/testowaniu, zapisujemy faktyczną wersję
{
  "dllModVersion": "1.2.3",  // zamiast "latest"
  "notes": "Tested with AleLuduMod v1.2.3"
}
```

**Rozwiązanie B: Snapshot przy Testowaniu**
```sql
ALTER TABLE compatibility_matrix 
ADD COLUMN DllModVersionSnapshot VARCHAR(50) NULL COMMENT 'Faktyczna wersja DLL podczas testu';
```

```javascript
// Przy tworzeniu wpisu
{
  "dllModVersion": "latest",
  "dllModVersionSnapshot": "1.2.3",  // Faktyczna wersja podczas testu
  "testedBy": "admin"
}
```

### Scenariusz 3: Zapytanie API - Obsługa Wersji

**Zapytanie o najnowszą wersję:**
```http
GET /api/compatibility?fullModId=1
```

**Logika:**
```javascript
// Pobierz aktualną wersję z config
const currentVersion = await getModCurrentVersion(1); // "5.4.0"

// Pobierz kompatybilności dla tej wersji
const compatibilities = await db.query(`
  SELECT * FROM compatibility_matrix 
  WHERE FullModId = ? AND FullModVersion = ?
`, [1, currentVersion]);
```

**Zapytanie o konkretną wersję:**
```http
GET /api/compatibility?fullModId=1&fullModVersion=5.3.1
```

**Zapytanie o wszystkie wersje:**
```http
GET /api/compatibility?fullModId=1&allVersions=true
```

**Response:**
```json
{
  "success": true,
  "versions": [
    {
      "version": "5.4.0",
      "isCurrent": true,
      "compatibilities": [
        {"dllModId": 5, "status": "NT"}
      ]
    },
    {
      "version": "5.3.1",
      "isCurrent": false,
      "compatibilities": [
        {"dllModId": 5, "status": "F"},
        {"dllModId": 8, "status": "W"}
      ]
    }
  ]
}
```

## 🔔 Powiadomienia o Zmianach

### Webhook/Discord Bot Integration

Gdy mod zostaje zaktualizowany, powiadamiaj Discord:

```javascript
async function notifyVersionUpdate(mod, oldVersion, newVersion) {
  const message = {
    embeds: [{
      title: `🔄 ${mod.ModName} Updated`,
      description: `Version ${oldVersion} → ${newVersion}`,
      fields: [
        {
          name: "⚠️ Compatibility Status",
          value: "All DLL compatibilities reset to **Not Tested** for new version"
        },
        {
          name: "📋 Action Required",
          value: "Please test and update compatibility statuses"
        }
      ],
      color: 0xFFA500, // Orange
      timestamp: new Date()
    }]
  };
  
  await sendDiscordWebhook(ADMIN_CHANNEL_WEBHOOK, message);
}
```

## 🎨 UI dla Admina - Obsługa Wersji

### Widok Macierzy z Wyborem Wersji

```
Mod: Town of Us
Version: [v5.4.0 (current) ▼] [v5.3.1] [v5.3.0] [+ Show All]

              | AleLuduMod | AUnlocker | LevelImposter |
--------------|------------|-----------|---------------|
v5.4.0 (now)  |    NT ⚠️   |   NT ⚠️   |     NT ⚠️     |
v5.3.1        |     F ✅   |    W ✅   |     NW ❌     |
v5.3.0        |     F ✅   |   NT ⚪   |     NT ⚪     |
```

**Legenda:**
- ⚠️ = Not Tested (wymaga przetestowania)
- ✅ = Works/Favorite
- ❌ = Not Work
- ⚪ = Not Tested (stara wersja)

### Quick Actions

**Przycisk: "Copy from Previous Version"**
```javascript
async function copyCompatibilitiesFromPreviousVersion() {
  // Kopiuj statusy z v5.3.1 do v5.4.0
  // Ale ustaw wszystkie jako NT do weryfikacji
  await db.query(`
    INSERT INTO compatibility_matrix 
    (FullModId, DllModId, FullModVersion, DllModVersion, CompatibilityStatus, Notes)
    SELECT 
      FullModId, 
      DllModId, 
      '5.4.0', 
      DllModVersion, 
      'NT',
      CONCAT('Copied from v5.3.1 (was: ', CompatibilityStatus, '). Needs retesting.')
    FROM compatibility_matrix
    WHERE FullModId = 1 AND FullModVersion = '5.3.1'
  `);
}
```

## 📊 Analityka i Statystyki

### Dashboard dla Adminów

```javascript
// Statystyki kompatybilności
const stats = {
  currentVersion: "5.4.0",
  totalCombinations: 12,
  tested: 3,          // F, W, NW
  notTested: 9,       // NT
  working: 2,         // F, W
  notWorking: 1,      // NW
  testingProgress: "25%"  // 3/12
};
```

**Widok UI:**
```
Town of Us v5.4.0 - Compatibility Status
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Total Combinations: 12
Tested: 3 (25%) [████░░░░░░░░░░░░]
  ✅ Working: 2
  ❌ Not Working: 1
  ⚠️ Needs Testing: 9

⚡ Quick Action: [Test All] [Copy from v5.3.1]
```

## 🔄 Migracja Istniejących Danych

### Krok 1: Wypełnienie Braków

```sql
-- Utwórz wpisy NT dla wszystkich istniejących modów
INSERT IGNORE INTO compatibility_matrix 
(FullModId, DllModId, FullModVersion, DllModVersion, CompatibilityStatus)
SELECT 
  fm.Id, 
  dm.Id, 
  fm.ModVersion, 
  dm.ModVersion, 
  'NT'
FROM config fm
CROSS JOIN config dm
WHERE fm.ModType = 'full' 
  AND dm.ModType = 'dll';
```

### Krok 2: Import Ręcznych Danych

Jeśli mamy istniejącą wiedzę o kompatybilności:

```sql
-- Town of Us 5.3.1 + AleLuduMod = działa
UPDATE compatibility_matrix 
SET CompatibilityStatus = 'F',
    TestedBy = 'legacy_import',
    TestedDate = NOW(),
    Notes = 'Imported from existing knowledge'
WHERE FullModId = 1 
  AND DllModId = 5 
  AND FullModVersion = '5.3.1'
  AND DllModVersion = 'latest';
```

## 🚀 Best Practices

### 1. Zawsze Używaj Konkretnych Wersji
```javascript
// ❌ ZŁE
{ dllModVersion: "latest" }

// ✅ DOBRE
{ 
  dllModVersion: "1.2.3",
  notes: "Current latest version as of 2025-10-22"
}
```

### 2. Automatyczne Snapshoty
```javascript
// Przy każdym teście, zapisz faktyczne wersje
const snapshot = {
  fullModVersion: await getActualVersion(fullModId),
  dllModVersion: await getActualVersion(dllModId),
  amongUsVersion: await getAmongUsVersion(),
  testEnvironment: "Windows 10, Steam"
};
```

### 3. Regularne Przypomnienia
```javascript
// Co tydzień sprawdzaj nieprzetestowane kombinacje
async function sendWeeklyReminder() {
  const untested = await db.query(`
    SELECT COUNT(*) as count 
    FROM compatibility_matrix 
    WHERE CompatibilityStatus = 'NT'
      AND FullModVersion = (SELECT ModVersion FROM config WHERE Id = FullModId)
  `);
  
  if (untested.count > 0) {
    await sendDiscordMessage(`⚠️ ${untested.count} combinations need testing!`);
  }
}
```

### 4. Archiwizacja Starych Wersji
```sql
-- Po 6 miesiącach, oznacz jako archived
ALTER TABLE compatibility_matrix 
ADD COLUMN IsArchived BOOLEAN DEFAULT FALSE;

-- Automatycznie archiwizuj stare wersje
UPDATE compatibility_matrix cm
JOIN config c ON cm.FullModId = c.Id
SET cm.IsArchived = TRUE
WHERE cm.FullModVersion != c.ModVersion
  AND cm.TestedDate < DATE_SUB(NOW(), INTERVAL 6 MONTH);
```

## 📈 Roadmap Funkcji

### v1.0 (MVP)
- ✅ Podstawowe wersjonowanie
- ✅ Ręczne testowanie i aktualizacja
- ✅ Historia zachowana

### v2.0
- 🔄 Automatyczne tworzenie wpisów NT przy aktualizacji
- 🔔 Powiadomienia Discord
- 📊 Dashboard ze statystykami

### v3.0
- 🤖 Sugerowanie kompatybilności na podstawie poprzednich wersji
- 👥 Community feedback
- 📈 Predykcja problemów kompatybilności

## 🎓 Przykład Pełnego Cyklu

```javascript
// 1. Admin aktualizuje Town of Us
await updateMod(1, { version: "5.4.0" });
  → Trigger tworzy wpisy NT dla wszystkich DLL

// 2. System powiadamia Discord
await notifyVersionUpdate("Town of Us", "5.3.1", "5.4.0");
  → Admini dostają powiadomienie o potrzebie testów

// 3. Admin testuje AleLuduMod
await testCompatibility(1, 5, "5.4.0", "1.2.3");
  → Status zmienia się z NT na W

// 4. Admin aktualizuje w panelu
await updateCompatibility(matrixId, { 
  status: "W", 
  notes: "Działa, ale drobne lagi przy dużych mapach" 
});

// 5. Użytkownik sprawdza kompatybilność
const result = await getCompatibility({ fullModId: 1 });
  → Widzi aktualne statusy dla v5.4.0
```
