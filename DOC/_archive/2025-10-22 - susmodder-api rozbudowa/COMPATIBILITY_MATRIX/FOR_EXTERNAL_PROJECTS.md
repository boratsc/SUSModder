# Compatibility Matrix API - Dla Projektów Zewnętrznych

**Integracja z SUSModder Compatibility Matrix API**

---

## 🎯 Co to jest?

API pozwalające na sprawdzanie kompatybilności między modami Among Us:
- **FULL mods** (pełne modyfikacje gry)
- **DLL mods** (dodatki/rozszerzenia)

---

## 🚀 Quick Start (5 minut)

### 1. Podstawowe zapytanie

```bash
curl "https://api.susmodder.app/api/compatibility?dllModId=5"
```

### 2. Tylko działające mody

```bash
curl "https://api.susmodder.app/api/compatibility?dllModId=5&status=F,W"
```

### 3. W JavaScript

```javascript
const response = await fetch(
  'https://api.susmodder.app/api/compatibility?dllModId=5&status=F,W'
);
const data = await response.json();

data.compatibilities.forEach(comp => {
  console.log(`${comp.fullMod.name}: ${comp.status}`);
});
```

**To wszystko! API jest publiczne, nie wymaga autoryzacji.**

---

## 📚 Dokumentacja

### Dla szybkiego startu:
1. **[QUICK_API_REFERENCE.md](./QUICK_API_REFERENCE.md)** - ściąga, podstawy (2 strony)
2. **[API_USAGE_GUIDE.md](./API_USAGE_GUIDE.md)** - pełny przewodnik (45 stron)
3. **[CODE_EXAMPLES.md](./CODE_EXAMPLES.md)** - gotowe przykłady kodu

### Wybierz swój język:
- JavaScript (Vanilla, Axios, TypeScript, React)
- Python
- C# / .NET
- PHP
- Go
- Java

**Wszystkie przykłady są gotowe do skopiowania i użycia!**

---

## 🔑 Statusy Kompatybilności

| Status | Znaczenie |
|--------|-----------|
| `F` | 🟢 **Favorite** - Działa idealnie, polecane |
| `W` | 🔵 **Works** - Działa poprawnie |
| `NT` | ⚪ **Not Tested** - Nieprzetestowane |
| `NW` | 🔴 **Not Work** - Nie działa |

---

## 📡 Endpointy

### GET `/api/compatibility`
Pobierz kompatybilności dla moda.

**Parametry:**
- `fullModId` - ID moda FULL (wymagany ALBO dllModId)
- `dllModId` - ID moda DLL (wymagany ALBO fullModId)
- `status` - Filtruj po statusie (opcjonalny, np. `F,W`)
- `includeUntested` - Uwzględnij NT (opcjonalny, default: true)

**Przykład response:**
```json
{
  "success": true,
  "count": 2,
  "compatibilities": [
    {
      "id": 11,
      "status": "F",
      "fullMod": {
        "id": 1,
        "name": "Town of Us",
        "version": "5.3.1"
      }
    }
  ]
}
```

---

## 💡 Przykładowe Use Cases

### Use Case 1: Lista rekomendowanych DLL dla FULL moda

```javascript
async function getRecommendedDlls(fullModId) {
  const response = await fetch(
    `https://api.susmodder.app/api/compatibility?fullModId=${fullModId}&status=F`
  );
  const data = await response.json();
  return data.compatibilities.map(c => c.dllMod);
}
```

### Use Case 2: Sprawdź czy konkretna para jest kompatybilna

```javascript
async function checkCompatibility(fullModId, dllModId) {
  const response = await fetch(
    `https://api.susmodder.app/api/compatibility?fullModId=${fullModId}`
  );
  const data = await response.json();

  const compat = data.compatibilities.find(c => c.dllMod.id === dllModId);

  return {
    compatible: ['F', 'W'].includes(compat?.status),
    recommended: compat?.status === 'F'
  };
}
```

### Use Case 3: Ostrzeżenie o niekompatybilności

```javascript
async function validateSelection(fullModId, dllModIds) {
  const response = await fetch(
    `https://api.susmodder.app/api/compatibility?fullModId=${fullModId}`
  );
  const data = await response.json();

  const warnings = [];

  dllModIds.forEach(dllId => {
    const compat = data.compatibilities.find(c => c.dllMod.id === dllId);

    if (compat?.status === 'NW') {
      warnings.push({
        dllId,
        message: `${compat.dllMod.name} nie działa z tym modem!`
      });
    }
  });

  return warnings;
}
```

---

## ✅ Best Practices

### DO:
- ✅ Cachuj odpowiedzi API (5-10 minut)
- ✅ Używaj `status=F,W` dla produkcyjnych sugestii
- ✅ Sprawdzaj pole `isCurrentVersion`
- ✅ Obsługuj błędy HTTP (404, 500)

### DON'T:
- ❌ Nie wysyłaj nadmiarowych requestów
- ❌ Nie ignoruj statusu NT
- ❌ Nie hardcoduj ID modów

---

## 🐛 Troubleshooting

### Problem: `404 - Mod not found`
**Rozwiązanie:** Sprawdź dostępne mody:
```bash
curl "https://api.susmodder.app/susmodder-config" | jq '.[] | {Id, ModName, ModType}'
```

### Problem: Puste wyniki
**Rozwiązanie:** Sprawdź bez filtrów:
```bash
curl "https://api.susmodder.app/api/compatibility?dllModId=5&includeUntested=true"
```

### Problem: Wolne zapytania
**Rozwiązanie:** Implementuj caching:
```javascript
const cache = new Map();
const CACHE_TTL = 5 * 60 * 1000; // 5 minut

async function getCachedData(url) {
  const cached = cache.get(url);
  if (cached && Date.now() - cached.time < CACHE_TTL) {
    return cached.data;
  }

  const response = await fetch(url);
  const data = await response.json();

  cache.set(url, { data, time: Date.now() });
  return data;
}
```

---

## 📞 Wsparcie

**Dokumentacja:**
- 📖 Pełny przewodnik: [API_USAGE_GUIDE.md](./API_USAGE_GUIDE.md)
- ⚡ Szybka ściąga: [QUICK_API_REFERENCE.md](./QUICK_API_REFERENCE.md)
- 💻 Przykłady kodu: [CODE_EXAMPLES.md](./CODE_EXAMPLES.md)
- 🌐 Swagger UI: `https://api.susmodder.app/api-docs`

**Health Check:**
```bash
curl https://api.susmodder.app/health
```

**W razie problemów:**
1. Sprawdź Swagger dokumentację
2. Zweryfikuj strukturę requestu
3. Sprawdź przykłady w CODE_EXAMPLES.md
4. Kontakt z zespołem DevOps

---

## 🎁 Gotowe Przykłady

Mamy gotowe, działające przykłady w:
- **JavaScript** (Vanilla, Axios, TypeScript)
- **React** (Hooks, Components)
- **Python** (requests, dataclasses)
- **C#** (.NET, HttpClient)
- **PHP** (file_get_contents, curl)
- **Go** (net/http)
- **Java** (HttpClient)

**Wszystkie w:** [CODE_EXAMPLES.md](./CODE_EXAMPLES.md)

---

## 📊 API Info

**Base URL:** `https://api.susmodder.app`

**Limity:**
- Rate limit: 100 req/min (GET)
- Timeout: 10 sekund
- Max response: ~50 KB

**Wersjonowanie:**
- Aktualna wersja: v1.0.0
- Backward compatibility: Gwarantowana

**Status:**
- Uptime: 99.9%
- Response time: < 200ms
- Health check: `https://api.susmodder.app/health`

---

## 🚀 Zacznij teraz!

1. **Przeczytaj:** [QUICK_API_REFERENCE.md](./QUICK_API_REFERENCE.md) (2 min)
2. **Skopiuj:** Przykład z [CODE_EXAMPLES.md](./CODE_EXAMPLES.md) (1 min)
3. **Test:** Wyślij pierwsze zapytanie (1 min)
4. **Integruj:** Dodaj do swojej aplikacji (10 min)

**Total: ~15 minut do pełnej integracji! 🎉**

---

## 📝 Changelog API

**v1.0.0** (2025-10-22) - Initial Release
- ✅ GET `/api/compatibility` - publiczny endpoint
- ✅ GET `/api/compatibility/matrix` - endpoint dla adminów
- ✅ Filtrowanie po statusie
- ✅ Obsługa wersji modów
- ✅ 55 kombinacji (11 FULL × 5 DLL)

---

**Pytania? Zobacz pełną dokumentację w [API_USAGE_GUIDE.md](./API_USAGE_GUIDE.md)**

**Wersja:** 1.0.0
**Status:** ✅ Production Ready
**Data:** 2025-10-22
