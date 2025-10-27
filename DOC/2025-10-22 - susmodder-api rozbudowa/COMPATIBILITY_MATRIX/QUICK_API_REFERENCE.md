# Compatibility Matrix API - Quick Reference

## 🚀 Quick Start

**Base URL:** `https://api.susmodder.app`

---

## Endpoints

### 1. GET `/api/compatibility` (Public)

Pobierz kompatybilności dla moda.

**Wymagany parametr:** `fullModId` ALBO `dllModId`

```bash
# Dla DLL
curl "https://api.susmodder.app/api/compatibility?dllModId=5"

# Dla FULL
curl "https://api.susmodder.app/api/compatibility?fullModId=1"

# Tylko działające (F,W)
curl "https://api.susmodder.app/api/compatibility?dllModId=5&status=F,W"

# Bez nieprzetestowanych
curl "https://api.susmodder.app/api/compatibility?fullModId=1&includeUntested=false"
```

---

### 2. GET `/api/compatibility/matrix` (Auth Required)

Pobierz pełną macierz.

```bash
curl -H "Authorization: YOUR_TOKEN" \
  "https://api.susmodder.app/api/compatibility/matrix"
```

---

## Parametry Query

| Parametr | Wartość | Opis |
|----------|---------|------|
| `fullModId` | integer | ID moda FULL |
| `dllModId` | integer | ID moda DLL |
| `status` | `F,W,NT,NW` | Filtruj po statusie |
| `includeUntested` | `true/false` | Uwzględnij NT (default: true) |
| `fullModVersion` | string | Konkretna wersja FULL |
| `dllModVersion` | string | Konkretna wersja DLL |

---

## Statusy

| Kod | Znaczenie |
|-----|-----------|
| `F` | 🟢 Favorite (polecane) |
| `W` | 🔵 Works (działa) |
| `NT` | ⚪ Not Tested (nieprzetestowane) |
| `NW` | 🔴 Not Work (nie działa) |

---

## Response Example

```json
{
  "success": true,
  "query": {
    "type": "dll",
    "modId": 5,
    "modName": "AleLuduMod",
    "modVersion": "latest"
  },
  "count": 11,
  "compatibilities": [
    {
      "id": 11,
      "status": "F",
      "isCurrentVersion": true,
      "fullMod": {
        "id": 1,
        "name": "Town of Us",
        "version": "5.3.1",
        "currentVersion": "5.3.1"
      }
    }
  ]
}
```

---

## JavaScript Quick Examples

```javascript
// Pobierz kompatybilności
const response = await fetch(
  'https://api.susmodder.app/api/compatibility?dllModId=5&status=F,W'
);
const data = await response.json();

// Sprawdź czy kompatybilne
const isCompatible = data.compatibilities.some(
  comp => comp.dllMod.id === targetDllId && ['F','W'].includes(comp.status)
);

// Pobierz macierz (z auth)
const matrix = await fetch(
  'https://api.susmodder.app/api/compatibility/matrix',
  { headers: { 'Authorization': token } }
);
```

---

## Error Codes

| Code | Znaczenie |
|------|-----------|
| `400` | Brak wymaganego parametru |
| `401` | Brak autoryzacji |
| `404` | Mod nie znaleziony |
| `500` | Błąd serwera |

---

## Tips

✅ Cachuj odpowiedzi (5-10 min)
✅ Używaj `status=F,W` dla produkcji
✅ Sprawdzaj `isCurrentVersion`
✅ Obsługuj błędy 404/500

❌ Nie hardcoduj ID modów
❌ Nie ignoruj statusu NT
❌ Nie wysyłaj nadmiarowych requestów

---

## Support

- **Swagger:** `https://api.susmodder.app/api-docs`
- **Health:** `https://api.susmodder.app/health`
- **Full Docs:** `/DOC/COMPATIBILITY_MATRIX/API_USAGE_GUIDE.md`
