# Quick Reference - Compatibility Matrix

## 🚀 Szybki Start

### Dla Developerów

**Sprawdź kompatybilność DLL z FULL:**
```bash
curl "https://api.susmodder.app/api/compatibility?dllModId=5"
```

**Sprawdź kompatybilność FULL z DLL:**
```bash
curl "https://api.susmodder.app/api/compatibility?fullModId=1"
```

**Utwórz nowy wpis:**
```bash
curl -X POST "https://api.susmodder.app/api/compatibility" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
    "fullModId": 1,
    "dllModId": 5,
    "fullModVersion": "5.3.1",
    "dllModVersion": "latest",
    "status": "F"
  }'
```

---

## 📋 Statusy Kompatybilności

| Kod | Nazwa | Znaczenie | Emoji |
|-----|-------|-----------|-------|
| **F** | Favorite | Działa idealnie, polecane | ✅ 🟢 |
| **W** | Works | Działa poprawnie | ✅ 🔵 |
| **NT** | Not Tested | Nieprzetestowane | ⚠️ ⚪ |
| **NW** | Not Work | Nie działa | ❌ 🔴 |

---

## 🗄️ Struktura Bazy Danych

### Główna Tabela

```sql
compatibility_matrix (
  Id                   INT PRIMARY KEY
  FullModId            INT               -- FK do config
  DllModId             INT               -- FK do config  
  FullModVersion       VARCHAR(50)       -- Wersja FULL
  DllModVersion        VARCHAR(50)       -- Wersja DLL
  CompatibilityStatus  ENUM('F','W','NT','NW')
  TestedDate           DATETIME
  TestedBy             VARCHAR(100)
  AmongUsVersion       VARCHAR(50)
  Notes                TEXT
  IssuesUrl            VARCHAR(255)
)
```

### Widoki

**vw_current_compatibility** - Aktualne kompatybilności
**vw_compatibility_matrix_full** - Pełna macierz dla UI

---

## 🌐 API Endpoints

| Metoda | Endpoint | Auth | Opis |
|--------|----------|------|------|
| GET | `/api/compatibility` | ❌ | Lista kompatybilności |
| GET | `/api/compatibility/:id` | ❌ | Szczegóły wpisu |
| POST | `/api/compatibility` | ✅ | Utwórz wpis |
| PUT | `/api/compatibility/:id` | ✅ | Zaktualizuj wpis |
| DELETE | `/api/compatibility/:id` | ✅ | Usuń wpis |
| GET | `/api/compatibility/matrix` | ✅ | Pełna macierz |

---

## 💻 Przykłady Użycia

### JavaScript (Fetch)

```javascript
// Pobierz kompatybilności dla DLL
async function getDllCompatibilities(dllModId) {
  const response = await fetch(
    `https://api.susmodder.app/api/compatibility?dllModId=${dllModId}`
  );
  return await response.json();
}

// Użycie
const data = await getDllCompatibilities(5);
console.log(data.compatibilities);
```

### React Hook

```javascript
import { useState, useEffect } from 'react';

function useCompatibility(modId, type = 'dll') {
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(true);
  
  useEffect(() => {
    const param = type === 'dll' ? 'dllModId' : 'fullModId';
    fetch(`/api/compatibility?${param}=${modId}`)
      .then(res => res.json())
      .then(data => {
        setData(data);
        setLoading(false);
      });
  }, [modId, type]);
  
  return { data, loading };
}

// Użycie
function MyComponent() {
  const { data, loading } = useCompatibility(5, 'dll');
  
  if (loading) return <div>Loading...</div>;
  
  return (
    <div>
      {data.compatibilities.map(c => (
        <div key={c.id}>{c.fullMod.name}: {c.status}</div>
      ))}
    </div>
  );
}
```

---

## 🗂️ Struktura Dokumentacji

```
DOC/COMPATIBILITY_MATRIX/
├── 00_PROJECT_SUMMARY.md          # Podsumowanie projektu
├── 01_DATABASE_DESIGN.md          # Projekt bazy danych
├── 02_API_SPECIFICATION.md        # Specyfikacja API
├── 03_VERSION_HANDLING.md         # Obsługa wersji
├── 04_ADMIN_INTERFACE.md          # Interfejs admina
├── 05_MIGRATION_PLAN.md           # Plan wdrożenia
└── QUICK_REFERENCE.md             # Ten dokument
```

---

## 🔧 Typowe Zadania

### Dodaj Nową Kompatybilność

```sql
INSERT INTO compatibility_matrix 
(FullModId, DllModId, FullModVersion, DllModVersion, 
 CompatibilityStatus, TestedBy, Notes)
VALUES 
(1, 5, '5.3.1', 'latest', 'F', 'admin', 'Działa bez problemów');
```

### Zaktualizuj Status

```sql
UPDATE compatibility_matrix 
SET CompatibilityStatus = 'W',
    Notes = 'Drobne lagi przy dużych mapach'
WHERE FullModId = 1 AND DllModId = 5 
  AND FullModVersion = '5.3.1';
```

### Sprawdź Statystyki

```sql
SELECT 
  CompatibilityStatus,
  COUNT(*) as Count,
  ROUND(COUNT(*) * 100.0 / SUM(COUNT(*)) OVER(), 2) as Percentage
FROM compatibility_matrix
GROUP BY CompatibilityStatus;
```

---

## 🐛 Debugging

### Sprawdź Logi API

```bash
docker logs -f nginx-api-susmodder | grep compatibility
```

### Sprawdź Zapytania SQL

```bash
docker exec nginx-mysql mysql \
  -h 193.70.42.86 \
  -u susfuckr \
  -pTXyF7re10wo2JlTYBzcp8t3b9PDtbRLX \
  susfuckr -e "
    SELECT * FROM compatibility_matrix LIMIT 5;
  "
```

### Test API

```bash
# Test GET
curl -s "http://localhost:3001/api/compatibility?fullModId=1" | jq .

# Test POST (wymaga tokena)
curl -s -X POST "http://localhost:3001/api/compatibility" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{"fullModId":1,"dllModId":5,"fullModVersion":"5.3.1","dllModVersion":"latest","status":"F"}' \
  | jq .
```

---

## 📊 Metryki i Monitoring

### Sprawdź Rozmiar Tabeli

```sql
SELECT 
  table_name,
  ROUND(((data_length + index_length) / 1024 / 1024), 2) AS size_mb,
  table_rows
FROM information_schema.TABLES 
WHERE table_schema = 'susfuckr' 
  AND table_name = 'compatibility_matrix';
```

### Sprawdź Wydajność

```sql
EXPLAIN SELECT * FROM compatibility_matrix 
WHERE FullModId = 1 AND FullModVersion = '5.3.1';
```

---

## 🔑 Zmienne Środowiskowe

```env
# API
DB_HOST=193.70.42.86
DB_USER=susfuckr
DB_PASSWORD=TXyF7re10wo2JlTYBzcp8t3b9PDtbRLX
DB_NAME=susfuckr
DB_PORT=3306
HTTP_TOKEN=e4a1c7b2f3d8e9a0b5c6d7e8f9a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9
```

---

## 🎯 Kolejne Kroki

### Po Wdrożeniu
1. ✅ Sprawdź czy API działa
2. ✅ Przetestuj interfejs admina
3. ✅ Wypełnij pierwsze dane testowe
4. ✅ Zbierz feedback od adminów

### Przyszłe Rozszerzenia
- 🔔 Discord notifications
- 👥 Community feedback system
- 📈 Statystyki użytkowania
- 🤖 Auto-testing przy update'ach
- 📤 Export/Import CSV

---

## 📞 Kontakt i Pomoc

### Jeśli Coś Nie Działa

1. Sprawdź logi: `docker logs nginx-api-susmodder`
2. Sprawdź bazę danych: `docker exec nginx-mysql mysql ...`
3. Sprawdź dokumentację: `DOC/COMPATIBILITY_MATRIX/`
4. Sprawdź testy: `cd susmodder-api && npm test`

### Najczęstsze Problemy

**Problem:** API zwraca 404
**Rozwiązanie:** Sprawdź czy router jest zarejestrowany w `server.js`

**Problem:** Brak danych w macierzy
**Rozwiązanie:** Sprawdź czy tabela została wypełniona: `SELECT COUNT(*) FROM compatibility_matrix;`

**Problem:** Token authentication nie działa
**Rozwiązanie:** Sprawdź czy middleware `requireAuthToken` jest poprawnie skonfigurowany

---

## 🎓 Przykładowy Workflow

### Typowy Dzień Admina

1. **Rano:** Sprawdź powiadomienia Discord o nowych wersjach modów
2. **10:00:** Otwórz panel admina → Compatibility Matrix
3. **10:05:** Kliknij "Testing Mode" dla nowej wersji
4. **10:30:** Przetestuj 12 kombinacji (po 2 minuty każda)
5. **11:00:** Zapisz wyniki, automatyczne powiadomienie Discord
6. **11:05:** Użytkownicy widzą zaktualizowane statusy

### Miesięczne Zadania

- Przejrzyj wszystkie wpisy NT (Not Tested)
- Zaktualizuj przestarzałe wpisy
- Sprawdź statystyki użytkowania
- Zarchiwizuj stare wersje (>6 miesięcy)

---

## 📚 Dodatkowe Zasoby

- **API Docs:** `/api-docs` (Swagger UI)
- **Source Code:** `susmodder-api/routes/compatibility.js`
- **Database Schema:** `migrations/001_create_compatibility_matrix.sql`
- **Tests:** `susmodder-api/test/compatibility.test.js`

---

**Ostatnia aktualizacja:** 2025-10-22  
**Wersja dokumentacji:** 1.0  
**Autor:** SysAdmin Team
