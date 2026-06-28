# Compatibility Matrix API - Przewodnik Użycia

## 📖 Wprowadzenie

API Compatibility Matrix pozwala na sprawdzanie kompatybilności między modami **FULL** (pełne modyfikacje Among Us) a modami **DLL** (dodatki/rozszerzenia).

**Base URL:**
- Development: `http://localhost:3001`
- Production: `https://api.susmodder.app`

---

## 🔑 Statusy Kompatybilności

| Status | Kod | Znaczenie |
|--------|-----|-----------|
| 🟢 **Favorite** | `F` | Działa idealnie, polecane przez społeczność |
| 🔵 **Works** | `W` | Działa poprawnie, bez większych problemów |
| ⚪ **Not Tested** | `NT` | Nieprzetestowane, nieznany status |
| 🔴 **Not Work** | `NW` | Nie działa, niekompatybilne |

---

## 📡 Endpointy

### 1️⃣ GET `/api/compatibility` - Pobierz kompatybilności dla moda

**Typ:** Publiczny (bez autoryzacji)

**Opis:** Pobiera listę kompatybilności dla konkretnego moda DLL lub FULL.

#### Parametry Query

| Parametr | Typ | Wymagany | Opis |
|----------|-----|----------|------|
| `fullModId` | integer | Tak* | ID moda FULL (z tabeli `config`) |
| `fullModVersion` | string | Nie | Wersja moda FULL (domyślnie: aktualna) |
| `dllModId` | integer | Tak* | ID moda DLL (z tabeli `config`) |
| `dllModVersion` | string | Nie | Wersja moda DLL (domyślnie: aktualna) |
| `status` | string | Nie | Filtruj po statusie (np. `F,W`) |
| `includeUntested` | boolean | Nie | Czy uwzględniać NT (domyślnie: `true`) |

**\*Uwaga:** Wymagany **ALBO** `fullModId` **ALBO** `dllModId` (nie oba naraz).

---

#### Przykład 1: Pobierz kompatybilności dla moda DLL

**Request:**
```http
GET /api/compatibility?dllModId=5
```

**Response:** `200 OK`
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
    },
    {
      "id": 9,
      "status": "W",
      "isCurrentVersion": true,
      "fullMod": {
        "id": 4,
        "name": "The Other Roles",
        "version": "4.8.0",
        "currentVersion": "4.8.0"
      }
    },
    {
      "id": 8,
      "status": "NT",
      "isCurrentVersion": true,
      "fullMod": {
        "id": 6,
        "name": "ToH Enhanced",
        "version": "2.4.0",
        "currentVersion": "2.4.0"
      }
    }
  ]
}
```

---

#### Przykład 2: Pobierz kompatybilności dla moda FULL

**Request:**
```http
GET /api/compatibility?fullModId=1
```

**Response:** `200 OK`
```json
{
  "success": true,
  "query": {
    "type": "full",
    "modId": 1,
    "modName": "Town of Us",
    "modVersion": "5.3.1"
  },
  "count": 5,
  "compatibilities": [
    {
      "id": 11,
      "status": "F",
      "isCurrentVersion": true,
      "dllMod": {
        "id": 5,
        "name": "AleLuduMod",
        "version": "latest",
        "currentVersion": "latest"
      }
    },
    {
      "id": 22,
      "status": "W",
      "isCurrentVersion": true,
      "dllMod": {
        "id": 8,
        "name": "AUnlocker",
        "version": "latest",
        "currentVersion": "latest"
      }
    },
    {
      "id": 33,
      "status": "NW",
      "isCurrentVersion": true,
      "dllMod": {
        "id": 9,
        "name": "LevelImposter",
        "version": "Custom Beta 0.20.4",
        "currentVersion": "Custom Beta 0.20.4"
      }
    }
  ]
}
```

---

#### Przykład 3: Filtrowanie tylko polecanych i działających

**Request:**
```http
GET /api/compatibility?dllModId=5&status=F,W
```

**Response:** `200 OK`
```json
{
  "success": true,
  "query": {
    "type": "dll",
    "modId": 5,
    "modName": "AleLuduMod",
    "modVersion": "latest"
  },
  "count": 2,
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
    },
    {
      "id": 9,
      "status": "W",
      "isCurrentVersion": true,
      "fullMod": {
        "id": 4,
        "name": "The Other Roles",
        "version": "4.8.0",
        "currentVersion": "4.8.0"
      }
    }
  ]
}
```

---

#### Przykład 4: Wykluczenie nieprzetestowanych

**Request:**
```http
GET /api/compatibility?fullModId=1&includeUntested=false
```

**Response:** Tylko mody z statusem `F`, `W`, lub `NW` (bez `NT`).

---

#### Przykład 5: Sprawdzenie starszej wersji

**Request:**
```http
GET /api/compatibility?fullModId=1&fullModVersion=5.2.0
```

**Response:**
```json
{
  "success": true,
  "query": {
    "type": "full",
    "modId": 1,
    "modName": "Town of Us",
    "modVersion": "5.2.0"
  },
  "count": 5,
  "compatibilities": [
    {
      "id": 123,
      "status": "F",
      "isCurrentVersion": false,
      "warning": "Tested on version 5.2.0, current is 5.3.1",
      "dllMod": {
        "id": 5,
        "name": "AleLuduMod",
        "version": "latest",
        "currentVersion": "latest"
      }
    }
  ]
}
```

**⚠️ Uwaga:** Pole `isCurrentVersion: false` i `warning` pojawią się, gdy zapytywana wersja różni się od aktualnej.

---

#### Błędy

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

**500 Internal Server Error** - Błąd bazy danych
```json
{
  "success": false,
  "error": "Database error while fetching compatibility data"
}
```

---

### 2️⃣ GET `/api/compatibility/matrix` - Pełna macierz (admin)

**Typ:** Wymagana autoryzacja

**Opis:** Pobiera pełną macierz wszystkich kombinacji FULL × DLL (dla interfejsu administracyjnego).

#### Headers

```http
Authorization: YOUR_TOKEN_HERE
```

**⚠️ Uwaga:** Token NIE zawiera prefiksu "Bearer". Wysyłaj tylko sam token.

#### Parametry Query

| Parametr | Typ | Opis |
|----------|-----|------|
| `onlyCurrentVersions` | boolean | Tylko aktualne wersje (domyślnie: `true`) |

---

#### Przykład Request

**Request:**
```http
GET /api/compatibility/matrix
Authorization: dev-token-local-123456789
```

**Response:** `200 OK`
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
      "matrixId": 11
    },
    {
      "fullModId": 1,
      "dllModId": 8,
      "status": "W",
      "matrixId": 22
    },
    {
      "fullModId": 1,
      "dllModId": 9,
      "status": "NW",
      "matrixId": 33
    },
    {
      "fullModId": 4,
      "dllModId": 5,
      "status": "NT",
      "matrixId": null
    }
  ]
}
```

**Wyjaśnienie struktury:**
- `fullMods` - lista wszystkich modów FULL
- `dllMods` - lista wszystkich modów DLL
- `matrix` - tablica wszystkich kombinacji:
  - `matrixId: null` oznacza brak wpisu w bazie (nieprzetestowane)
  - `status: "NT"` oznacza wpis z domyślnym statusem

---

#### Błędy

**401 Unauthorized** - Brak lub nieprawidłowy token
```json
{
  "error": "Unauthorized"
}
```

**500 Internal Server Error** - Błąd bazy danych
```json
{
  "success": false,
  "error": "Database error while fetching compatibility matrix"
}
```

---

## 💻 Przykłady Implementacji

### JavaScript (Fetch API)

```javascript
// Pobierz kompatybilności dla DLL
async function getDllCompatibilities(dllModId, onlyWorking = false) {
  const statusFilter = onlyWorking ? '&status=F,W' : '';
  const response = await fetch(
    `https://api.susmodder.app/api/compatibility?dllModId=${dllModId}${statusFilter}`
  );

  if (!response.ok) {
    throw new Error(`API error: ${response.status}`);
  }

  const data = await response.json();
  return data.compatibilities;
}

// Pobierz kompatybilności dla FULL
async function getFullCompatibilities(fullModId) {
  const response = await fetch(
    `https://api.susmodder.app/api/compatibility?fullModId=${fullModId}`
  );

  const data = await response.json();
  return data.compatibilities;
}

// Użycie
try {
  const compatibilities = await getDllCompatibilities(5, true); // Tylko F i W

  compatibilities.forEach(comp => {
    console.log(`${comp.fullMod.name}: ${comp.status}`);
  });
} catch (error) {
  console.error('Błąd pobierania kompatybilności:', error);
}
```

---

### JavaScript (Axios)

```javascript
import axios from 'axios';

const API_BASE = 'https://api.susmodder.app';

// Pobierz kompatybilności
async function getCompatibilities(params) {
  try {
    const response = await axios.get(`${API_BASE}/api/compatibility`, {
      params: params
    });

    return response.data;
  } catch (error) {
    if (error.response?.status === 404) {
      throw new Error('Mod nie znaleziony');
    }
    throw error;
  }
}

// Przykłady użycia
const dllCompat = await getCompatibilities({
  dllModId: 5,
  status: 'F,W'
});

const fullCompat = await getCompatibilities({
  fullModId: 1,
  includeUntested: false
});
```

---

### Python (requests)

```python
import requests

API_BASE = 'https://api.susmodder.app'

def get_dll_compatibilities(dll_mod_id, status_filter=None):
    """Pobierz kompatybilności dla moda DLL"""
    params = {'dllModId': dll_mod_id}

    if status_filter:
        params['status'] = status_filter  # np. 'F,W'

    response = requests.get(
        f'{API_BASE}/api/compatibility',
        params=params
    )
    response.raise_for_status()

    return response.json()['compatibilities']

def get_full_compatibilities(full_mod_id):
    """Pobierz kompatybilności dla moda FULL"""
    response = requests.get(
        f'{API_BASE}/api/compatibility',
        params={'fullModId': full_mod_id}
    )
    response.raise_for_status()

    return response.json()['compatibilities']

# Użycie
try:
    compatibilities = get_dll_compatibilities(5, 'F,W')

    for comp in compatibilities:
        full_mod = comp['fullMod']
        print(f"{full_mod['name']}: {comp['status']}")

except requests.exceptions.RequestException as e:
    print(f"Błąd API: {e}")
```

---

### cURL (terminal)

```bash
# Pobierz kompatybilności dla DLL (AleLuduMod)
curl -s "https://api.susmodder.app/api/compatibility?dllModId=5" | jq .

# Tylko polecane i działające
curl -s "https://api.susmodder.app/api/compatibility?dllModId=5&status=F,W" | jq .

# Pobierz dla FULL (Town of Us)
curl -s "https://api.susmodder.app/api/compatibility?fullModId=1" | jq .

# Pełna macierz (wymaga tokena)
curl -H "Authorization: YOUR_TOKEN_HERE" \
  "https://api.susmodder.app/api/compatibility/matrix" | jq .
```

---

### C# (.NET)

```csharp
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

public class CompatibilityClient
{
    private readonly HttpClient _httpClient;
    private const string API_BASE = "https://api.susmodder.app";

    public CompatibilityClient()
    {
        _httpClient = new HttpClient();
    }

    public async Task<CompatibilityResponse> GetDllCompatibilities(
        int dllModId,
        string statusFilter = null)
    {
        var url = $"{API_BASE}/api/compatibility?dllModId={dllModId}";

        if (!string.IsNullOrEmpty(statusFilter))
        {
            url += $"&status={statusFilter}";
        }

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<CompatibilityResponse>(json);
    }

    public async Task<CompatibilityResponse> GetFullCompatibilities(int fullModId)
    {
        var url = $"{API_BASE}/api/compatibility?fullModId={fullModId}";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<CompatibilityResponse>(json);
    }
}

// Użycie
var client = new CompatibilityClient();
var compatibilities = await client.GetDllCompatibilities(5, "F,W");

foreach (var comp in compatibilities.Compatibilities)
{
    Console.WriteLine($"{comp.FullMod.Name}: {comp.Status}");
}
```

---

## 🎨 Przykłady UI

### Lista kompatybilności (React)

```jsx
import React, { useEffect, useState } from 'react';

function CompatibilityList({ dllModId }) {
  const [compatibilities, setCompatibilities] = useState([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetch(`https://api.susmodder.app/api/compatibility?dllModId=${dllModId}&status=F,W`)
      .then(res => res.json())
      .then(data => {
        setCompatibilities(data.compatibilities);
        setLoading(false);
      });
  }, [dllModId]);

  const getStatusBadge = (status) => {
    const badges = {
      'F': { label: 'Polecane', color: 'bg-green-500' },
      'W': { label: 'Działa', color: 'bg-blue-500' },
      'NT': { label: 'Nieprzetestowane', color: 'bg-gray-400' },
      'NW': { label: 'Nie działa', color: 'bg-red-500' }
    };

    const badge = badges[status];
    return (
      <span className={`${badge.color} text-white px-2 py-1 rounded text-xs`}>
        {badge.label}
      </span>
    );
  };

  if (loading) return <div>Ładowanie...</div>;

  return (
    <div className="space-y-2">
      <h3 className="text-lg font-bold">Kompatybilne mody FULL:</h3>
      {compatibilities.map(comp => (
        <div key={comp.id} className="flex items-center justify-between p-3 border rounded">
          <div>
            <span className="font-medium">{comp.fullMod.name}</span>
            <span className="text-sm text-gray-500 ml-2">v{comp.fullMod.version}</span>
          </div>
          {getStatusBadge(comp.status)}
        </div>
      ))}
    </div>
  );
}
```

---

### Macierz kompatybilności (React)

```jsx
import React, { useEffect, useState } from 'react';

function CompatibilityMatrix({ authToken }) {
  const [matrix, setMatrix] = useState(null);

  useEffect(() => {
    fetch('https://api.susmodder.app/api/compatibility/matrix', {
      headers: { 'Authorization': authToken }
    })
      .then(res => res.json())
      .then(data => setMatrix(data));
  }, [authToken]);

  if (!matrix) return <div>Ładowanie macierzy...</div>;

  const getStatusCell = (fullModId, dllModId) => {
    const entry = matrix.matrix.find(
      m => m.fullModId === fullModId && m.dllModId === dllModId
    );

    const colors = {
      'F': 'bg-green-100 text-green-800',
      'W': 'bg-blue-100 text-blue-800',
      'NT': 'bg-gray-100 text-gray-600',
      'NW': 'bg-red-100 text-red-800'
    };

    return (
      <td key={`${fullModId}-${dllModId}`}
          className={`p-2 text-center ${colors[entry?.status || 'NT']}`}>
        {entry?.status || 'NT'}
      </td>
    );
  };

  return (
    <table className="w-full border-collapse">
      <thead>
        <tr>
          <th className="border p-2">FULL Mod</th>
          {matrix.dllMods.map(dll => (
            <th key={dll.id} className="border p-2">{dll.name}</th>
          ))}
        </tr>
      </thead>
      <tbody>
        {matrix.fullMods.map(full => (
          <tr key={full.id}>
            <td className="border p-2 font-medium">{full.name}</td>
            {matrix.dllMods.map(dll => getStatusCell(full.id, dll.id))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}
```

---

## 📊 Przypadki użycia

### Case 1: Lista rekomendowanych DLL dla konkretnego FULL moda

**Scenariusz:** Użytkownik wybiera mod FULL (Town of Us), aplikacja pokazuje polecane dodatki DLL.

```javascript
async function getRecommendedDlls(fullModId) {
  const response = await fetch(
    `https://api.susmodder.app/api/compatibility?fullModId=${fullModId}&status=F`
  );

  const data = await response.json();

  return data.compatibilities.map(comp => ({
    id: comp.dllMod.id,
    name: comp.dllMod.name,
    version: comp.dllMod.version,
    recommended: true
  }));
}

// Użycie
const recommended = await getRecommendedDlls(1); // Town of Us
console.log('Polecane DLL:', recommended);
```

---

### Case 2: Sprawdzenie czy konkretna para modów jest kompatybilna

**Scenariusz:** Przed instalacją sprawdź czy Town of Us + AleLuduMod działa.

```javascript
async function checkCompatibility(fullModId, dllModId) {
  const response = await fetch(
    `https://api.susmodder.app/api/compatibility?fullModId=${fullModId}`
  );

  const data = await response.json();

  const compatibility = data.compatibilities.find(
    comp => comp.dllMod.id === dllModId
  );

  if (!compatibility) {
    return { compatible: false, reason: 'Brak danych' };
  }

  return {
    compatible: ['F', 'W'].includes(compatibility.status),
    status: compatibility.status,
    recommended: compatibility.status === 'F'
  };
}

// Użycie
const result = await checkCompatibility(1, 5); // Town of Us + AleLuduMod
if (result.compatible) {
  console.log('✅ Mody są kompatybilne!');
  if (result.recommended) {
    console.log('⭐ Polecana kombinacja!');
  }
} else {
  console.log('❌ Mody mogą nie działać razem');
}
```

---

### Case 3: Ostrzeżenie o niekompatybilności

**Scenariusz:** Użytkownik próbuje zainstalować niekompatybilne mody.

```javascript
async function validateModSelection(fullModId, dllModIds) {
  const response = await fetch(
    `https://api.susmodder.app/api/compatibility?fullModId=${fullModId}&includeUntested=false`
  );

  const data = await response.json();
  const warnings = [];

  dllModIds.forEach(dllId => {
    const compat = data.compatibilities.find(c => c.dllMod.id === dllId);

    if (!compat) {
      warnings.push({
        dllId,
        severity: 'warning',
        message: 'Kompatybilność nieprzetestowana'
      });
    } else if (compat.status === 'NW') {
      warnings.push({
        dllId,
        severity: 'error',
        message: `${compat.dllMod.name} nie działa z tym modem!`
      });
    }
  });

  return warnings;
}

// Użycie
const warnings = await validateModSelection(1, [5, 8, 9]);
if (warnings.length > 0) {
  console.warn('Ostrzeżenia:', warnings);
}
```

---

### Case 4: Automatyczne sugestie

**Scenariusz:** Użytkownik ma zainstalowany DLL, pokaż kompatybilne FULL mody.

```javascript
async function suggestFullMods(dllModId) {
  const response = await fetch(
    `https://api.susmodder.app/api/compatibility?dllModId=${dllModId}&status=F,W`
  );

  const data = await response.json();

  // Sortuj: najpierw Favorite, potem Works
  const sorted = data.compatibilities.sort((a, b) => {
    if (a.status === 'F' && b.status !== 'F') return -1;
    if (a.status !== 'F' && b.status === 'F') return 1;
    return 0;
  });

  return sorted.map(comp => ({
    ...comp.fullMod,
    recommended: comp.status === 'F',
    compatible: true
  }));
}

// Użycie
const suggestions = await suggestFullMods(5); // AleLuduMod
console.log('Kompatybilne mody FULL:', suggestions);
```

---

## 🔧 Najlepsze Praktyki

### ✅ DO:

1. **Cachuj odpowiedzi API** - dane zmieniają się rzadko
   ```javascript
   const cache = new Map();
   const CACHE_TTL = 5 * 60 * 1000; // 5 minut

   async function getCachedCompatibility(modId) {
     const cacheKey = `compat-${modId}`;
     const cached = cache.get(cacheKey);

     if (cached && Date.now() - cached.timestamp < CACHE_TTL) {
       return cached.data;
     }

     const data = await fetchCompatibility(modId);
     cache.set(cacheKey, { data, timestamp: Date.now() });
     return data;
   }
   ```

2. **Obsługuj błędy gracefully**
   ```javascript
   try {
     const data = await getCompatibilities(modId);
   } catch (error) {
     if (error.response?.status === 404) {
       console.log('Mod nie znaleziony');
     } else if (error.response?.status === 500) {
       console.log('Błąd serwera, spróbuj ponownie później');
     } else {
       console.log('Błąd sieci, sprawdź połączenie');
     }
   }
   ```

3. **Używaj `status=F,W` dla produkcyjnych sugestii**
   - Pokazuj użytkownikom tylko przetestowane i działające kombinacje

4. **Sprawdzaj `isCurrentVersion`**
   ```javascript
   if (!compatibility.isCurrentVersion) {
     console.warn(compatibility.warning);
   }
   ```

### ❌ DON'T:

1. **Nie wysyłaj nadmiarowych requestów**
   - Nie odpytuj API w pętli bez debounce/throttle

2. **Nie ignoruj statusu NT**
   - Informuj użytkowników o nieprzetestowanych kombinacjach

3. **Nie hardcoduj ID modów**
   - Zawsze pobieraj listę z `/susmodder-config`

4. **Nie zakładaj że endpoint zawsze działa**
   - Implementuj fallback i retry logic

---

## 🐛 Debugowanie

### Problem: `404 - Mod not found`

**Przyczyna:** Nieprawidłowe ID moda

**Rozwiązanie:**
```bash
# Sprawdź dostępne mody
curl "https://api.susmodder.app/susmodder-config" | jq '.[] | {Id, ModName, ModType}'
```

---

### Problem: Puste wyniki

**Przyczyna:** Brak wpisów w bazie dla danej kombinacji

**Rozwiązanie:**
```bash
# Sprawdź bez filtrów
curl "https://api.susmodder.app/api/compatibility?dllModId=5"

# Sprawdź czy includeUntested nie jest false
curl "https://api.susmodder.app/api/compatibility?dllModId=5&includeUntested=true"
```

---

### Problem: `401 Unauthorized` na `/matrix`

**Przyczyna:** Brak tokenu lub nieprawidłowy format

**Rozwiązanie:**
```bash
# POPRAWNIE (bez "Bearer")
curl -H "Authorization: YOUR_TOKEN_HERE" \
  "https://api.susmodder.app/api/compatibility/matrix"

# BŁĘDNIE (z "Bearer")
curl -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  "https://api.susmodder.app/api/compatibility/matrix"
```

---

## 📞 Wsparcie

**Dokumentacja API:**
- Swagger UI: `https://api.susmodder.app/api-docs`
- Pełna dokumentacja: `/DOC/COMPATIBILITY_MATRIX/`

**Health Check:**
```bash
curl https://api.susmodder.app/health
```

**W razie problemów:**
1. Sprawdź Swagger dokumentację
2. Zweryfikuj strukturę requestu
3. Sprawdź logi aplikacji
4. Kontakt z zespołem DevOps

---

## 📝 Changelog

**v1.0.0** (2025-10-22)
- ✅ Endpoint GET `/api/compatibility`
- ✅ Endpoint GET `/api/compatibility/matrix`
- ✅ Filtrowanie po statusie
- ✅ Obsługa wersji modów
- ✅ Minimalistyczna struktura bazy danych

---

**Wersja dokumentu:** 1.0.0
**Data aktualizacji:** 2025-10-22
**Autor:** SysAdmin Team, susmodder.app
