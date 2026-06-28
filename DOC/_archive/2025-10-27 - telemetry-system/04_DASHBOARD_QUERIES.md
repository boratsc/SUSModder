# Dashboard Queries - Telemetry Analytics

## 📋 Przegląd

Dokumentacja query'ów i endpointów dla dashboardu analityki telemetrii SUSModder.

## 🌐 API Endpoints dla Dashboardu

### Base URL:
- Development: `http://localhost:3001/api/telemetry`
- Production: `https://susmodder.app/api/telemetry`

### Authentication:
Wszystkie endpointy analytics wymagają tokenu admin.

```http
GET /api/telemetry/analytics/...
Authorization: Bearer {ADMIN_TOKEN}
```

---

## 📊 Endpoint 1: Daily Active Users

### GET `/api/telemetry/analytics/dau`

Zwraca liczbę unikalnych użytkowników dla wybranego dnia.

#### Query Parameters:
- `date` (optional) - YYYY-MM-DD format (domyślnie: dzisiaj)

#### Example Request:
```bash
curl -X GET "https://susmodder.app/api/telemetry/analytics/dau?date=2025-10-27" \
  -H "Authorization: Bearer {ADMIN_TOKEN}"
```

#### Response:
```json
{
  "success": true,
  "data": {
    "date": "2025-10-27",
    "dau": 1234,
    "change": {
      "yesterday": 1150,
      "percentChange": "+7.3%"
    }
  }
}
```

#### Implementation:
```javascript
router.get('/analytics/dau', authenticateAdmin, async (req, res) => {
  try {
    const date = req.query.date || new Date().toISOString().split('T')[0];
    const yesterday = getYesterday(date);
    
    const dau = await redis.scard(`telemetry:daily:users:${date}`);
    const yesterdayCount = await redis.scard(`telemetry:daily:users:${yesterday}`);
    
    const percentChange = yesterdayCount > 0
      ? (((dau - yesterdayCount) / yesterdayCount) * 100).toFixed(1)
      : 0;
    
    res.json({
      success: true,
      data: {
        date,
        dau,
        change: {
          yesterday: yesterdayCount,
          percentChange: `${percentChange > 0 ? '+' : ''}${percentChange}%`
        }
      }
    });
  } catch (err) {
    res.status(500).json({ success: false, error: err.message });
  }
});
```

---

## 📊 Endpoint 2: Weekly/Monthly Active Users

### GET `/api/telemetry/analytics/active-users`

Zwraca DAU, WAU, MAU dla wybranego okresu.

#### Query Parameters:
- `period` (required) - `day`, `week`, `month`
- `date` (optional) - YYYY-MM-DD (domyślnie: dzisiaj)

#### Example Request:
```bash
curl -X GET "https://susmodder.app/api/telemetry/analytics/active-users?period=week" \
  -H "Authorization: Bearer {ADMIN_TOKEN}"
```

#### Response:
```json
{
  "success": true,
  "data": {
    "period": "week",
    "startDate": "2025-10-21",
    "endDate": "2025-10-27",
    "activeUsers": 3456,
    "dailyBreakdown": [
      { "date": "2025-10-21", "users": 450 },
      { "date": "2025-10-22", "users": 520 },
      { "date": "2025-10-23", "users": 480 },
      { "date": "2025-10-24", "users": 510 },
      { "date": "2025-10-25", "users": 495 },
      { "date": "2025-10-26", "users": 470 },
      { "date": "2025-10-27", "users": 531 }
    ]
  }
}
```

#### Implementation:
```javascript
router.get('/analytics/active-users', authenticateAdmin, async (req, res) => {
  try {
    const { period } = req.query;
    const endDate = req.query.date || new Date().toISOString().split('T')[0];
    
    let days;
    if (period === 'day') days = 1;
    else if (period === 'week') days = 7;
    else if (period === 'month') days = 30;
    else return res.status(400).json({ error: 'Invalid period' });
    
    const dates = getDateRange(endDate, days);
    const keys = dates.map(d => `telemetry:daily:users:${d}`);
    
    // Union wszystkich setów
    const tempKey = `telemetry:temp:union:${Date.now()}`;
    await redis.sunionstore(tempKey, ...keys);
    const activeUsers = await redis.scard(tempKey);
    await redis.del(tempKey);
    
    // Daily breakdown
    const dailyBreakdown = [];
    for (const date of dates) {
      const count = await redis.scard(`telemetry:daily:users:${date}`);
      dailyBreakdown.push({ date, users: count });
    }
    
    res.json({
      success: true,
      data: {
        period,
        startDate: dates[0],
        endDate: dates[dates.length - 1],
        activeUsers,
        dailyBreakdown
      }
    });
  } catch (err) {
    res.status(500).json({ success: false, error: err.message });
  }
});
```

---

## 📊 Endpoint 3: Platform Distribution

### GET `/api/telemetry/analytics/platforms`

Zwraca dystrybucję użytkowników Steam vs Epic.

#### Query Parameters:
- `date` (optional) - YYYY-MM-DD (domyślnie: dzisiaj)

#### Example Request:
```bash
curl -X GET "https://susmodder.app/api/telemetry/analytics/platforms?date=2025-10-27" \
  -H "Authorization: Bearer {ADMIN_TOKEN}"
```

#### Response:
```json
{
  "success": true,
  "data": {
    "date": "2025-10-27",
    "total": 1234,
    "platforms": {
      "steam": {
        "count": 987,
        "percentage": 80.0
      },
      "epic": {
        "count": 247,
        "percentage": 20.0
      }
    }
  }
}
```

#### Implementation:
```javascript
router.get('/analytics/platforms', authenticateAdmin, async (req, res) => {
  try {
    const date = req.query.date || new Date().toISOString().split('T')[0];
    const statsKey = `telemetry:daily:stats:${date}`;
    
    const steamCount = parseInt(await redis.hget(statsKey, 'platform:steam') || '0');
    const epicCount = parseInt(await redis.hget(statsKey, 'platform:epic') || '0');
    const total = steamCount + epicCount;
    
    res.json({
      success: true,
      data: {
        date,
        total,
        platforms: {
          steam: {
            count: steamCount,
            percentage: total > 0 ? parseFloat(((steamCount / total) * 100).toFixed(1)) : 0
          },
          epic: {
            count: epicCount,
            percentage: total > 0 ? parseFloat(((epicCount / total) * 100).toFixed(1)) : 0
          }
        }
      }
    });
  } catch (err) {
    res.status(500).json({ success: false, error: err.message });
  }
});
```

---

## 📊 Endpoint 4: Version Adoption

### GET `/api/telemetry/analytics/versions`

Zwraca rozkład wersji aplikacji.

#### Query Parameters:
- `date` (optional) - YYYY-MM-DD (domyślnie: dzisiaj)

#### Example Request:
```bash
curl -X GET "https://susmodder.app/api/telemetry/analytics/versions" \
  -H "Authorization: Bearer {ADMIN_TOKEN}"
```

#### Response:
```json
{
  "success": true,
  "data": {
    "date": "2025-10-27",
    "latestVersion": "2.0.0",
    "versions": [
      {
        "version": "2.0.0",
        "count": 1100,
        "percentage": 89.1,
        "isLatest": true
      },
      {
        "version": "1.9.9",
        "count": 100,
        "percentage": 8.1,
        "isLatest": false
      },
      {
        "version": "1.9.8",
        "count": 34,
        "percentage": 2.8,
        "isLatest": false
      }
    ],
    "adoptionRate": 89.1
  }
}
```

#### Implementation:
```javascript
router.get('/analytics/versions', authenticateAdmin, async (req, res) => {
  try {
    const date = req.query.date || new Date().toISOString().split('T')[0];
    const statsKey = `telemetry:daily:stats:${date}`;
    
    const stats = await redis.hgetall(statsKey);
    const versions = [];
    let total = 0;
    
    // Parse version counts
    for (const [key, value] of Object.entries(stats)) {
      if (key.startsWith('version:')) {
        const version = key.replace('version:', '');
        const count = parseInt(value);
        versions.push({ version, count });
        total += count;
      }
    }
    
    // Sort by count (descending)
    versions.sort((a, b) => b.count - a.count);
    
    // Calculate percentages and mark latest
    const latestVersion = versions[0]?.version || 'unknown';
    const versionsData = versions.map(v => ({
      version: v.version,
      count: v.count,
      percentage: parseFloat(((v.count / total) * 100).toFixed(1)),
      isLatest: v.version === latestVersion
    }));
    
    const adoptionRate = versionsData[0]?.percentage || 0;
    
    res.json({
      success: true,
      data: {
        date,
        latestVersion,
        versions: versionsData,
        adoptionRate
      }
    });
  } catch (err) {
    res.status(500).json({ success: false, error: err.message });
  }
});
```

---

## 📊 Endpoint 5: Top Mods

### GET `/api/telemetry/analytics/top-mods`

Zwraca najpopularniejsze mody.

#### Query Parameters:
- `date` (optional) - YYYY-MM-DD (domyślnie: dzisiaj)
- `limit` (optional) - liczba modów (domyślnie: 10)

#### Example Request:
```bash
curl -X GET "https://susmodder.app/api/telemetry/analytics/top-mods?limit=10" \
  -H "Authorization: Bearer {ADMIN_TOKEN}"
```

#### Response:
```json
{
  "success": true,
  "data": {
    "date": "2025-10-27",
    "totalMods": 25,
    "topMods": [
      {
        "modId": 1,
        "modName": "Town of Us",
        "installations": 1020,
        "percentage": 82.6
      },
      {
        "modId": 3,
        "modName": "The Other Roles",
        "installations": 950,
        "percentage": 77.0
      },
      {
        "modId": 7,
        "modName": "Stellar Roles",
        "installations": 780,
        "percentage": 63.2
      }
    ]
  }
}
```

#### Implementation:
```javascript
router.get('/analytics/top-mods', authenticateAdmin, async (req, res) => {
  try {
    const date = req.query.date || new Date().toISOString().split('T')[0];
    const limit = parseInt(req.query.limit || '10');
    
    const popularityKey = `telemetry:mods:popularity:${date}`;
    const dau = await redis.scard(`telemetry:daily:users:${date}`);
    
    // Pobierz top N modów
    const results = await redis.zrevrange(popularityKey, 0, limit - 1, 'WITHSCORES');
    
    // Parse results
    const topMods = [];
    for (let i = 0; i < results.length; i += 2) {
      const modId = parseInt(results[i]);
      const installations = parseInt(results[i + 1]);
      
      // Pobierz nazwę moda z config (opcjonalne - możesz mieć cache)
      const modName = await getModName(modId); // Helper function
      
      topMods.push({
        modId,
        modName,
        installations,
        percentage: dau > 0 ? parseFloat(((installations / dau) * 100).toFixed(1)) : 0
      });
    }
    
    const totalMods = await redis.zcard(popularityKey);
    
    res.json({
      success: true,
      data: {
        date,
        totalMods,
        topMods
      }
    });
  } catch (err) {
    res.status(500).json({ success: false, error: err.message });
  }
});

// Helper: Get mod name from config API or cache
async function getModName(modId) {
  try {
    // Cache mod names w Redis
    const cacheKey = `telemetry:cache:modname:${modId}`;
    let modName = await redis.get(cacheKey);
    
    if (!modName) {
      // Fetch from config API
      const config = await fetchModConfig(); // Your existing API
      const mod = config.find(m => m.id === modId);
      modName = mod?.modName || `Mod #${modId}`;
      
      // Cache for 24h
      await redis.setex(cacheKey, 86400, modName);
    }
    
    return modName;
  } catch (err) {
    return `Mod #${modId}`;
  }
}
```

---

## 📊 Endpoint 6: Language Distribution

### GET `/api/telemetry/analytics/languages`

Zwraca dystrybucję języków UI.

#### Query Parameters:
- `date` (optional) - YYYY-MM-DD (domyślnie: dzisiaj)

#### Example Request:
```bash
curl -X GET "https://susmodder.app/api/telemetry/analytics/languages" \
  -H "Authorization: Bearer {ADMIN_TOKEN}"
```

#### Response:
```json
{
  "success": true,
  "data": {
    "date": "2025-10-27",
    "total": 1234,
    "languages": [
      {
        "code": "pl",
        "name": "Polski",
        "count": 1050,
        "percentage": 85.1
      },
      {
        "code": "en",
        "name": "English",
        "count": 184,
        "percentage": 14.9
      }
    ]
  }
}
```

#### Implementation:
```javascript
const LANGUAGE_NAMES = {
  'pl': 'Polski',
  'en': 'English',
  'de': 'Deutsch',
  'fr': 'Français',
  'es': 'Español'
};

router.get('/analytics/languages', authenticateAdmin, async (req, res) => {
  try {
    const date = req.query.date || new Date().toISOString().split('T')[0];
    const statsKey = `telemetry:daily:stats:${date}`;
    
    const stats = await redis.hgetall(statsKey);
    const languages = [];
    let total = 0;
    
    for (const [key, value] of Object.entries(stats)) {
      if (key.startsWith('language:')) {
        const code = key.replace('language:', '');
        const count = parseInt(value);
        languages.push({ code, count });
        total += count;
      }
    }
    
    // Sort by count
    languages.sort((a, b) => b.count - a.count);
    
    // Add language names and percentages
    const languagesData = languages.map(l => ({
      code: l.code,
      name: LANGUAGE_NAMES[l.code] || l.code.toUpperCase(),
      count: l.count,
      percentage: parseFloat(((l.count / total) * 100).toFixed(1))
    }));
    
    res.json({
      success: true,
      data: {
        date,
        total,
        languages: languagesData
      }
    });
  } catch (err) {
    res.status(500).json({ success: false, error: err.message });
  }
});
```

---

## 📊 Endpoint 7: Session Statistics

### GET `/api/telemetry/analytics/sessions`

Zwraca statystyki czasu sesji.

#### Query Parameters:
- `date` (optional) - YYYY-MM-DD (domyślnie: dzisiaj)

#### Example Request:
```bash
curl -X GET "https://susmodder.app/api/telemetry/analytics/sessions" \
  -H "Authorization: Bearer {ADMIN_TOKEN}"
```

#### Response:
```json
{
  "success": true,
  "data": {
    "date": "2025-10-27",
    "totalSessions": 1234,
    "averageSessionSeconds": 1845,
    "averageSessionMinutes": 30.75,
    "distribution": {
      "0-5min": 120,
      "5-15min": 340,
      "15-30min": 450,
      "30-60min": 250,
      "60+min": 74
    }
  }
}
```

#### Implementation:
```javascript
router.get('/analytics/sessions', authenticateAdmin, async (req, res) => {
  try {
    const date = req.query.date || new Date().toISOString().split('T')[0];
    const statsKey = `telemetry:daily:stats:${date}`;
    
    const totalTime = parseInt(await redis.hget(statsKey, 'totalSessionTime') || '0');
    const sessionCount = parseInt(await redis.hget(statsKey, 'sessionCount') || '0');
    
    const avgSeconds = sessionCount > 0 ? Math.round(totalTime / sessionCount) : 0;
    const avgMinutes = parseFloat((avgSeconds / 60).toFixed(2));
    
    // Distribution (można też przechowywać w Redis)
    const distribution = {
      '0-5min': parseInt(await redis.hget(statsKey, 'session:0-5') || '0'),
      '5-15min': parseInt(await redis.hget(statsKey, 'session:5-15') || '0'),
      '15-30min': parseInt(await redis.hget(statsKey, 'session:15-30') || '0'),
      '30-60min': parseInt(await redis.hget(statsKey, 'session:30-60') || '0'),
      '60+min': parseInt(await redis.hget(statsKey, 'session:60+') || '0')
    };
    
    res.json({
      success: true,
      data: {
        date,
        totalSessions: sessionCount,
        averageSessionSeconds: avgSeconds,
        averageSessionMinutes: avgMinutes,
        distribution
      }
    });
  } catch (err) {
    res.status(500).json({ success: false, error: err.message });
  }
});
```

**Uwaga:** Distribution wymaga dodatkowego zapisywania bucketów w heartbeat handler:

```javascript
// W heartbeat handler - dodaj bucket tracking
const minutes = Math.floor(sessionTimeSeconds / 60);
let bucket;
if (minutes < 5) bucket = 'session:0-5';
else if (minutes < 15) bucket = 'session:5-15';
else if (minutes < 30) bucket = 'session:15-30';
else if (minutes < 60) bucket = 'session:30-60';
else bucket = 'session:60+';

await redis.hincrby(statsKey, bucket, 1);
```

---

## 📊 Endpoint 8: Overview Dashboard

### GET `/api/telemetry/analytics/overview`

Zwraca pełny przegląd wszystkich metryk (dla głównego dashboardu).

#### Query Parameters:
- `date` (optional) - YYYY-MM-DD (domyślnie: dzisiaj)

#### Example Request:
```bash
curl -X GET "https://susmodder.app/api/telemetry/analytics/overview" \
  -H "Authorization: Bearer {ADMIN_TOKEN}"
```

#### Response:
```json
{
  "success": true,
  "data": {
    "date": "2025-10-27",
    "users": {
      "dau": 1234,
      "wau": 3456,
      "mau": 8901,
      "changeFromYesterday": "+7.3%"
    },
    "platforms": {
      "steam": { "count": 987, "percentage": 80.0 },
      "epic": { "count": 247, "percentage": 20.0 }
    },
    "versions": {
      "latest": "2.0.0",
      "adoptionRate": 89.1,
      "outdated": 134
    },
    "topMods": [
      { "modId": 1, "modName": "Town of Us", "installations": 1020 },
      { "modId": 3, "modName": "The Other Roles", "installations": 950 },
      { "modId": 7, "modName": "Stellar Roles", "installations": 780 }
    ],
    "sessions": {
      "average": "30.75 minutes",
      "total": 1234
    }
  }
}
```

#### Implementation:
```javascript
router.get('/analytics/overview', authenticateAdmin, async (req, res) => {
  try {
    const date = req.query.date || new Date().toISOString().split('T')[0];
    
    // Wywołaj wszystkie analytics w parallel
    const [
      dau,
      wau,
      mau,
      platforms,
      versions,
      topMods,
      sessions
    ] = await Promise.all([
      getDAU(date),
      getWAU(date),
      getMAU(date),
      getPlatforms(date),
      getVersions(date),
      getTopMods(date, 3),
      getSessions(date)
    ]);
    
    res.json({
      success: true,
      data: {
        date,
        users: {
          dau: dau.count,
          wau,
          mau,
          changeFromYesterday: dau.change
        },
        platforms,
        versions,
        topMods,
        sessions
      }
    });
  } catch (err) {
    res.status(500).json({ success: false, error: err.message });
  }
});
```

---

## 🔐 Authentication Middleware

```javascript
// middleware/auth.js

function authenticateAdmin(req, res, next) {
  const token = req.headers.authorization?.replace('Bearer ', '');
  
  if (!token) {
    return res.status(401).json({
      success: false,
      error: 'Missing authorization token'
    });
  }
  
  // Validate token (z SecretProvider lub env)
  const adminToken = process.env.ADMIN_TOKEN || 'your-secret-token';
  
  if (token !== adminToken) {
    return res.status(403).json({
      success: false,
      error: 'Invalid authorization token'
    });
  }
  
  next();
}

module.exports = { authenticateAdmin };
```

---

## 📈 Helper Functions

```javascript
// utils/analytics-helpers.js

function getDateRange(endDate, days) {
  const dates = [];
  const end = new Date(endDate);
  
  for (let i = days - 1; i >= 0; i--) {
    const date = new Date(end);
    date.setDate(date.getDate() - i);
    dates.push(date.toISOString().split('T')[0]);
  }
  
  return dates;
}

function getYesterday(date) {
  const d = new Date(date);
  d.setDate(d.getDate() - 1);
  return d.toISOString().split('T')[0];
}

module.exports = { getDateRange, getYesterday };
```

---

**Status:** ✅ Dokumentacja kompletna  
**Estimated API implementation time:** 6-8 godzin
