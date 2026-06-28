# Telemetry Logging & Monitoring

## 📋 Przegląd

System logowania i monitorowania telemetrii SUSModder.

## 📊 Endpoint Admin: Recent Heartbeats

### GET `/api/telemetry/admin/recent`

Zwraca ostatnie heartbeaty (do debugowania).

#### Query Parameters:
- `limit` (optional) - liczba heartbeatów (domyślnie: 20, max: 100)
- `userHash` (optional) - filtruj po konkretnym użytkowniku

#### Authentication:
```http
GET /api/telemetry/admin/recent?limit=20
Authorization: Bearer {ADMIN_TOKEN}
```

#### Response:
```json
{
  "success": true,
  "data": {
    "count": 5,
    "heartbeats": [
      {
        "userHash": "0af304d3dc487f56c195f784a66b661ec8db7673678212ad6a0d45205476adb6",
        "appVersion": "2.0.0",
        "platform": "steam",
        "language": "pl",
        "installedModIds": [7, 13],
        "sessionTimeSeconds": 3,
        "timestamp": "2025-10-27T14:35:48.174Z",
        "receivedAt": "2025-10-27T14:35:48.500Z"
      },
      {
        "userHash": "a1b2c3d4e5f6...",
        "appVersion": "2.0.0",
        "platform": "epic",
        "language": "en",
        "installedModIds": [1, 3, 7],
        "sessionTimeSeconds": 120,
        "timestamp": "2025-10-27T14:30:00.000Z",
        "receivedAt": "2025-10-27T14:30:01.234Z"
      }
    ]
  }
}
```

#### Implementation (Node.js):

```javascript
// routes/telemetry.js

router.get('/admin/recent', authenticateAdmin, async (req, res) => {
  try {
    const limit = Math.min(parseInt(req.query.limit || '20'), 100);
    const userHash = req.query.userHash;
    
    // Pattern do wyszukiwania kluczy
    const pattern = userHash 
      ? `telemetry:heartbeat:${userHash}:*`
      : 'telemetry:heartbeat:*';
    
    // SCAN zamiast KEYS (bezpieczniejsze dla produkcji)
    const heartbeats = [];
    let cursor = '0';
    
    do {
      const [newCursor, keys] = await redis.scan(cursor, 'MATCH', pattern, 'COUNT', 100);
      cursor = newCursor;
      
      // Pobierz dane dla każdego klucza
      for (const key of keys) {
        const data = await redis.get(key);
        if (data) {
          heartbeats.push(JSON.parse(data));
        }
        
        // Limit reached
        if (heartbeats.length >= limit) break;
      }
      
      if (heartbeats.length >= limit) break;
    } while (cursor !== '0');
    
    // Sortuj po timestamp (od najnowszych)
    heartbeats.sort((a, b) => new Date(b.timestamp) - new Date(a.timestamp));
    
    res.json({
      success: true,
      data: {
        count: heartbeats.length,
        heartbeats: heartbeats.slice(0, limit)
      }
    });
    
  } catch (err) {
    console.error('Failed to fetch recent heartbeats:', err);
    res.status(500).json({
      success: false,
      error: 'Failed to fetch heartbeats'
    });
  }
});
```

---

## 📊 Endpoint Admin: User Activity

### GET `/api/telemetry/admin/user/:userHash`

Zwraca wszystkie dane dla konkretnego użytkownika.

#### Authentication:
```http
GET /api/telemetry/admin/user/0af304d3dc487f56c195f784a66b661ec8db7673678212ad6a0d45205476adb6
Authorization: Bearer {ADMIN_TOKEN}
```

#### Response:
```json
{
  "success": true,
  "data": {
    "userHash": "0af304d3dc487f56c195f784a66b661ec8db7673678212ad6a0d45205476adb6",
    "totalHeartbeats": 15,
    "firstSeen": "2025-10-20T10:00:00Z",
    "lastSeen": "2025-10-27T14:35:48Z",
    "platforms": ["steam"],
    "languages": ["pl"],
    "versions": ["2.0.0", "1.9.9"],
    "recentHeartbeats": [
      {
        "timestamp": "2025-10-27T14:35:48.174Z",
        "appVersion": "2.0.0",
        "installedModIds": [7, 13],
        "sessionTimeSeconds": 3
      }
    ]
  }
}
```

#### Implementation:

```javascript
router.get('/admin/user/:userHash', authenticateAdmin, async (req, res) => {
  try {
    const { userHash } = req.params;
    
    // Validate userHash format
    if (!/^[a-f0-9]{64}$/.test(userHash)) {
      return res.status(400).json({
        success: false,
        error: 'Invalid userHash format'
      });
    }
    
    // Find all heartbeats for this user
    const pattern = `telemetry:heartbeat:${userHash}:*`;
    const keys = [];
    let cursor = '0';
    
    do {
      const [newCursor, foundKeys] = await redis.scan(cursor, 'MATCH', pattern, 'COUNT', 1000);
      cursor = newCursor;
      keys.push(...foundKeys);
    } while (cursor !== '0');
    
    if (keys.length === 0) {
      return res.status(404).json({
        success: false,
        error: 'User not found'
      });
    }
    
    // Fetch all heartbeats
    const heartbeats = [];
    for (const key of keys) {
      const data = await redis.get(key);
      if (data) {
        heartbeats.push(JSON.parse(data));
      }
    }
    
    // Sort by timestamp
    heartbeats.sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp));
    
    // Extract metadata
    const platforms = [...new Set(heartbeats.map(h => h.platform))];
    const languages = [...new Set(heartbeats.map(h => h.language))];
    const versions = [...new Set(heartbeats.map(h => h.appVersion))];
    
    res.json({
      success: true,
      data: {
        userHash,
        totalHeartbeats: heartbeats.length,
        firstSeen: heartbeats[0]?.timestamp,
        lastSeen: heartbeats[heartbeats.length - 1]?.timestamp,
        platforms,
        languages,
        versions,
        recentHeartbeats: heartbeats.slice(-10).reverse()
      }
    });
    
  } catch (err) {
    console.error('Failed to fetch user activity:', err);
    res.status(500).json({
      success: false,
      error: 'Failed to fetch user activity'
    });
  }
});
```

---

## 📊 Endpoint Admin: Live Stats

### GET `/api/telemetry/admin/live`

Zwraca live statistics (ostatnie 5 minut).

#### Authentication:
```http
GET /api/telemetry/admin/live
Authorization: Bearer {ADMIN_TOKEN}
```

#### Response:
```json
{
  "success": true,
  "data": {
    "last5Minutes": {
      "heartbeats": 12,
      "uniqueUsers": 8
    },
    "last1Hour": {
      "heartbeats": 145,
      "uniqueUsers": 78
    },
    "rateLimitHits": 3,
    "redisMemoryUsage": "245 MB",
    "totalKeys": 15234
  }
}
```

---

## 🖥️ Console Logging (Backend)

### Middleware Request Logger

```javascript
// middleware/logger.js

const morgan = require('morgan');
const fs = require('fs');
const path = require('path');

// Ensure logs directory exists
const logsDir = path.join(__dirname, '../logs');
if (!fs.existsSync(logsDir)) {
  fs.mkdirSync(logsDir);
}

// Create write stream for access log
const accessLogStream = fs.createWriteStream(
  path.join(logsDir, 'telemetry-access.log'),
  { flags: 'a' }
);

// Custom format including request body for telemetry endpoints
morgan.token('body', (req) => {
  if (req.path.includes('/telemetry/heartbeat') && req.body) {
    return JSON.stringify({
      userHash: req.body.userHash?.substring(0, 16) + '...', // Skróć dla czytelności
      appVersion: req.body.appVersion,
      platform: req.body.platform,
      installedModIds: req.body.installedModIds?.length || 0
    });
  }
  return '-';
});

// Format: [timestamp] method url status responseTime body
const logFormat = '[:date[iso]] :method :url :status :response-time ms - :body';

// Export middleware
module.exports = {
  // Console logger (development)
  consoleLogger: morgan(logFormat),
  
  // File logger (production)
  fileLogger: morgan(logFormat, { stream: accessLogStream }),
  
  // Combined
  combinedLogger: (req, res, next) => {
    morgan(logFormat)(req, res, () => {});
    morgan(logFormat, { stream: accessLogStream })(req, res, next);
  }
};
```

### Usage in server.js:

```javascript
// server.js

const express = require('express');
const { combinedLogger } = require('./middleware/logger');

const app = express();

// Apply logging middleware
app.use(combinedLogger);

// Your routes...
app.use('/api/telemetry', telemetryRoutes);

app.listen(3001, () => {
  console.log('Server running on port 3001');
});
```

### Log Output Example:

```
[2025-10-27T14:35:48.500Z] POST /api/telemetry/heartbeat 200 45 ms - {"userHash":"0af304d3dc487f...","appVersion":"2.0.0","platform":"steam","installedModIds":0}
[2025-10-27T14:36:12.123Z] POST /api/telemetry/heartbeat 429 12 ms - {"userHash":"0af304d3dc487f...","appVersion":"2.0.0","platform":"steam","installedModIds":2}
[2025-10-27T14:40:00.456Z] GET /api/telemetry/admin/recent 200 89 ms - -
```

---

## 📊 Enhanced Heartbeat Handler (z dodatkowym logowaniem)

```javascript
// routes/telemetry.js - Enhanced version

router.post('/heartbeat', async (req, res) => {
  const startTime = Date.now();
  
  try {
    // 1. Validate
    const { error, value } = heartbeatSchema.validate(req.body);
    
    if (error) {
      console.log(`[Telemetry] ❌ Validation failed:`, error.details.map(d => d.message));
      return res.status(400).json({
        success: false,
        error: 'Validation failed',
        details: error.details.reduce((acc, curr) => {
          acc[curr.path[0]] = curr.message;
          return acc;
        }, {})
      });
    }

    const { userHash, appVersion, platform, installedModIds } = value;
    
    // 2. Rate limiting
    const rateLimitKey = `telemetry:ratelimit:${userHash}`;
    const isRateLimited = await redis.get(rateLimitKey);
    
    if (isRateLimited) {
      const ttl = await redis.ttl(rateLimitKey);
      console.log(`[Telemetry] ⏱️ Rate limited: ${userHash.substring(0, 16)}... (retry in ${ttl}s)`);
      return res.status(429).json({
        success: false,
        error: 'Rate limit exceeded',
        retryAfter: ttl
      });
    }

    // 3. Save heartbeat
    const heartbeatData = { ...value, receivedAt: new Date().toISOString() };
    const heartbeatKey = `telemetry:heartbeat:${userHash}:${Date.now()}`;
    
    await redis.setex(heartbeatKey, 60 * 60 * 24 * 90, JSON.stringify(heartbeatData));
    
    // 4. Update aggregates
    const today = new Date().toISOString().split('T')[0];
    await redis.sadd(`telemetry:daily:users:${today}`, userHash);
    await redis.hincrby(`telemetry:daily:stats:${today}`, `platform:${platform}`, 1);
    await redis.hincrby(`telemetry:daily:stats:${today}`, `version:${appVersion}`, 1);
    
    // Mod popularity
    for (const modId of installedModIds) {
      await redis.zincrby(`telemetry:mods:popularity:${today}`, 1, modId);
    }
    
    // 5. Set rate limit
    await redis.setex(rateLimitKey, 600, '1');
    
    // 6. Log success
    const duration = Date.now() - startTime;
    console.log(`[Telemetry] ✅ Heartbeat saved: ${userHash.substring(0, 16)}... | v${appVersion} | ${platform} | ${installedModIds.length} mods | ${duration}ms`);
    
    res.status(200).json({
      success: true,
      message: 'Heartbeat recorded',
      timestamp: new Date().toISOString()
    });

  } catch (err) {
    const duration = Date.now() - startTime;
    console.error(`[Telemetry] ❌ Error (${duration}ms):`, err.message);
    res.status(500).json({
      success: false,
      error: 'Internal server error'
    });
  }
});
```

---

## 🧪 Testing Endpoints (Curl)

### 1. Check Recent Heartbeats

```bash
curl -X GET "http://localhost:3001/api/telemetry/admin/recent?limit=5" \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN"
```

### 2. Check Specific User

```bash
curl -X GET "http://localhost:3001/api/telemetry/admin/user/0af304d3dc487f56c195f784a66b661ec8db7673678212ad6a0d45205476adb6" \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN"
```

### 3. Live Stats

```bash
curl -X GET "http://localhost:3001/api/telemetry/admin/live" \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN"
```

---

## 📝 Log Files

### Struktura:

```
logs/
├── telemetry-access.log       # All requests
├── telemetry-errors.log       # Errors only
└── telemetry-heartbeats.log   # Heartbeats only (optional)
```

### Log Rotation (PM2):

```json
{
  "apps": [{
    "name": "telemetry-api",
    "script": "server.js",
    "log_date_format": "YYYY-MM-DD HH:mm:ss Z",
    "merge_logs": true,
    "max_memory_restart": "500M",
    "error_file": "logs/telemetry-errors.log",
    "out_file": "logs/telemetry-access.log",
    "log_type": "json"
  }]
}
```

---

## 📊 Example Console Output

```
Server running on port 3001
Redis connected

[Telemetry] ✅ Heartbeat saved: 0af304d3dc487f... | v2.0.0 | steam | 0 mods | 45ms
[Telemetry] ✅ Heartbeat saved: a1b2c3d4e5f6... | v2.0.0 | epic | 3 mods | 38ms
[Telemetry] ⏱️ Rate limited: 0af304d3dc487f... (retry in 587s)
[Telemetry] ❌ Validation failed: ["userHash must be 64 character hex string"]
```

---

**Status:** ✅ Dokumentacja logowania kompletna  
**Zależności:** `morgan` (npm install morgan)
