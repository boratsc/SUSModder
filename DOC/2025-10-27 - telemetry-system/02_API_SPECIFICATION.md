# API Specification - Telemetry Endpoint

## 📋 Przegląd

Dokumentacja endpointu `/api/telemetry/heartbeat` dla systemu telemetrii SUSModder.

## 🌐 Endpoint

### POST `/api/telemetry/heartbeat`

Przyjmuje anonimowe heartbeaty od klientów SUSModder.

#### Base URL:
- Development: `http://localhost:3001`
- Production: `https://susmodder.app`

#### Headers:
```http
POST /api/telemetry/heartbeat HTTP/1.1
Host: susmodder.app
Content-Type: application/json
User-Agent: SUSModder/2.0.0
```

#### Request Body:

```json
{
  "userHash": "a1b2c3d4e5f6...",
  "appVersion": "2.0.0",
  "platform": "steam",
  "language": "pl",
  "installedModIds": [1, 3, 7, 12],
  "sessionTimeSeconds": 1234,
  "timestamp": "2025-10-27T12:34:56.789Z"
}
```

#### Request Schema:

| Pole | Typ | Required | Opis |
|------|-----|----------|------|
| `userHash` | string | ✅ | SHA256 hash z Hardware ID (64 chars hex) |
| `appVersion` | string | ✅ | Wersja aplikacji (semver) |
| `platform` | string | ✅ | Platforma gry: `"steam"` lub `"epic"` |
| `language` | string | ✅ | Język UI: `"pl"`, `"en"`, etc. |
| `installedModIds` | array<int> | ✅ | Lista ID zainstalowanych modów |
| `sessionTimeSeconds` | int | ✅ | Czas sesji w sekundach |
| `timestamp` | string | ✅ | ISO 8601 timestamp (UTC) |

#### Response (Success):

```http
HTTP/1.1 200 OK
Content-Type: application/json

{
  "success": true,
  "message": "Heartbeat recorded",
  "timestamp": "2025-10-27T12:34:57.123Z"
}
```

#### Response (Rate Limited):

```http
HTTP/1.1 429 Too Many Requests
Content-Type: application/json
Retry-After: 600

{
  "success": false,
  "error": "Rate limit exceeded",
  "message": "Please wait 10 minutes between heartbeats",
  "retryAfter": 600
}
```

#### Response (Validation Error):

```http
HTTP/1.1 400 Bad Request
Content-Type: application/json

{
  "success": false,
  "error": "Validation failed",
  "details": {
    "userHash": "Must be 64 character hex string",
    "platform": "Must be 'steam' or 'epic'"
  }
}
```

#### Response (Server Error):

```http
HTTP/1.1 500 Internal Server Error
Content-Type: application/json

{
  "success": false,
  "error": "Internal server error",
  "message": "Failed to save heartbeat"
}
```

## 🛡️ Rate Limiting

### Strategia:
- **1 heartbeat per 10 minut** per `userHash`
- Key w Redis: `telemetry:ratelimit:{userHash}`
- TTL: 600 sekund (10 minut)

### Implementacja:

```javascript
// Rate limiting check
const rateLimitKey = `telemetry:ratelimit:${userHash}`;
const existing = await redis.get(rateLimitKey);

if (existing) {
  return res.status(429).json({
    success: false,
    error: 'Rate limit exceeded',
    message: 'Please wait 10 minutes between heartbeats',
    retryAfter: await redis.ttl(rateLimitKey)
  });
}

// Set rate limit
await redis.setex(rateLimitKey, 600, '1');
```

## ✅ Validation Rules

### 1. userHash
- **Format:** 64 character hex string (lowercase)
- **Pattern:** `/^[a-f0-9]{64}$/`
- **Required:** Yes
- **Example:** `"a1b2c3d4e5f6..."`

### 2. appVersion
- **Format:** Semantic versioning (major.minor.patch)
- **Pattern:** `/^\d+\.\d+\.\d+$/`
- **Required:** Yes
- **Example:** `"2.0.0"`

### 3. platform
- **Format:** Enum
- **Values:** `"steam"` | `"epic"`
- **Required:** Yes

### 4. language
- **Format:** ISO 639-1 code (2 chars lowercase)
- **Pattern:** `/^[a-z]{2}$/`
- **Required:** Yes
- **Example:** `"pl"`, `"en"`

### 5. installedModIds
- **Format:** Array of integers
- **Min items:** 0
- **Max items:** 100
- **Required:** Yes
- **Example:** `[1, 3, 7, 12]`

### 6. sessionTimeSeconds
- **Format:** Integer (positive)
- **Min:** 0
- **Max:** 86400 (24h)
- **Required:** Yes
- **Example:** `1234`

### 7. timestamp
- **Format:** ISO 8601 (UTC)
- **Required:** Yes
- **Example:** `"2025-10-27T12:34:56.789Z"`

## 📝 Validation Schema (Joi)

```javascript
const Joi = require('joi');

const heartbeatSchema = Joi.object({
  userHash: Joi.string()
    .pattern(/^[a-f0-9]{64}$/)
    .required()
    .messages({
      'string.pattern.base': 'userHash must be 64 character hex string'
    }),
  
  appVersion: Joi.string()
    .pattern(/^\d+\.\d+\.\d+$/)
    .required()
    .messages({
      'string.pattern.base': 'appVersion must be semver (e.g., 2.0.0)'
    }),
  
  platform: Joi.string()
    .valid('steam', 'epic')
    .required()
    .messages({
      'any.only': 'platform must be either "steam" or "epic"'
    }),
  
  language: Joi.string()
    .pattern(/^[a-z]{2}$/)
    .required()
    .messages({
      'string.pattern.base': 'language must be 2-char ISO code (e.g., "pl")'
    }),
  
  installedModIds: Joi.array()
    .items(Joi.number().integer().positive())
    .max(100)
    .required()
    .messages({
      'array.max': 'installedModIds cannot exceed 100 items'
    }),
  
  sessionTimeSeconds: Joi.number()
    .integer()
    .min(0)
    .max(86400)
    .required()
    .messages({
      'number.max': 'sessionTimeSeconds cannot exceed 24 hours (86400)'
    }),
  
  timestamp: Joi.date()
    .iso()
    .required()
    .messages({
      'date.format': 'timestamp must be ISO 8601 format'
    })
});
```

## 🔧 Implementation (Node.js + Express)

### 1. Route Handler

```javascript
// routes/telemetry.js

const express = require('express');
const router = express.Router();
const Joi = require('joi');
const redis = require('../config/redis');

// Validation schema
const heartbeatSchema = Joi.object({
  userHash: Joi.string().pattern(/^[a-f0-9]{64}$/).required(),
  appVersion: Joi.string().pattern(/^\d+\.\d+\.\d+$/).required(),
  platform: Joi.string().valid('steam', 'epic').required(),
  language: Joi.string().pattern(/^[a-z]{2}$/).required(),
  installedModIds: Joi.array().items(Joi.number().integer().positive()).max(100).required(),
  sessionTimeSeconds: Joi.number().integer().min(0).max(86400).required(),
  timestamp: Joi.date().iso().required()
});

/**
 * POST /api/telemetry/heartbeat
 * Receives anonymous telemetry data from SUSModder clients
 */
router.post('/heartbeat', async (req, res) => {
  try {
    // 1. Validate request body
    const { error, value } = heartbeatSchema.validate(req.body);
    
    if (error) {
      return res.status(400).json({
        success: false,
        error: 'Validation failed',
        details: error.details.reduce((acc, curr) => {
          acc[curr.path[0]] = curr.message;
          return acc;
        }, {})
      });
    }

    const { userHash, appVersion, platform, language, installedModIds, sessionTimeSeconds, timestamp } = value;

    // 2. Rate limiting check
    const rateLimitKey = `telemetry:ratelimit:${userHash}`;
    const isRateLimited = await redis.get(rateLimitKey);
    
    if (isRateLimited) {
      const ttl = await redis.ttl(rateLimitKey);
      return res.status(429).json({
        success: false,
        error: 'Rate limit exceeded',
        message: 'Please wait 10 minutes between heartbeats',
        retryAfter: ttl
      });
    }

    // 3. Save heartbeat to Redis
    const heartbeatData = {
      userHash,
      appVersion,
      platform,
      language,
      installedModIds,
      sessionTimeSeconds,
      timestamp,
      receivedAt: new Date().toISOString()
    };

    // Key format: telemetry:heartbeat:{userHash}:{timestamp}
    const heartbeatKey = `telemetry:heartbeat:${userHash}:${Date.now()}`;
    await redis.setex(
      heartbeatKey,
      60 * 60 * 24 * 90, // TTL: 90 days
      JSON.stringify(heartbeatData)
    );

    // 4. Update daily statistics
    const today = new Date().toISOString().split('T')[0]; // YYYY-MM-DD
    
    // Add user to daily unique users set
    await redis.sadd(`telemetry:daily:users:${today}`, userHash);
    await redis.expire(`telemetry:daily:users:${today}`, 60 * 60 * 24 * 365); // 1 year TTL

    // Increment daily stats counters
    const statsKey = `telemetry:daily:stats:${today}`;
    await redis.hincrby(statsKey, `platform:${platform}`, 1);
    await redis.hincrby(statsKey, `version:${appVersion}`, 1);
    await redis.hincrby(statsKey, `language:${language}`, 1);
    await redis.expire(statsKey, 60 * 60 * 24 * 365);

    // Update mod popularity counters
    for (const modId of installedModIds) {
      await redis.zincrby(`telemetry:mods:popularity:${today}`, 1, modId);
      await redis.expire(`telemetry:mods:popularity:${today}`, 60 * 60 * 24 * 90);
    }

    // 5. Set rate limit
    await redis.setex(rateLimitKey, 600, '1'); // 10 minutes

    // 6. Return success
    res.status(200).json({
      success: true,
      message: 'Heartbeat recorded',
      timestamp: new Date().toISOString()
    });

  } catch (err) {
    console.error('Telemetry heartbeat error:', err);
    res.status(500).json({
      success: false,
      error: 'Internal server error',
      message: 'Failed to save heartbeat'
    });
  }
});

module.exports = router;
```

### 2. Redis Configuration

```javascript
// config/redis.js

const redis = require('redis');

const client = redis.createClient({
  host: process.env.REDIS_HOST || 'localhost',
  port: process.env.REDIS_PORT || 6379,
  password: process.env.REDIS_PASSWORD || undefined,
  db: process.env.REDIS_DB || 0
});

client.on('error', (err) => {
  console.error('Redis error:', err);
});

client.on('connect', () => {
  console.log('Redis connected');
});

module.exports = client;
```

### 3. Mount Routes

```javascript
// server.js

const express = require('express');
const telemetryRoutes = require('./routes/telemetry');

const app = express();

app.use(express.json());
app.use('/api/telemetry', telemetryRoutes);

const PORT = process.env.PORT || 3001;
app.listen(PORT, () => {
  console.log(`Server running on port ${PORT}`);
});
```

## 📊 Redis Keys Generated

### Per Heartbeat:
```
telemetry:heartbeat:{userHash}:{timestamp} → JSON (TTL: 90 days)
telemetry:ratelimit:{userHash} → "1" (TTL: 10 minutes)
```

### Daily Aggregates:
```
telemetry:daily:users:{YYYY-MM-DD} → Set of userHashes (TTL: 365 days)
telemetry:daily:stats:{YYYY-MM-DD} → Hash (TTL: 365 days)
  - platform:steam → counter
  - platform:epic → counter
  - version:2.0.0 → counter
  - language:pl → counter
telemetry:mods:popularity:{YYYY-MM-DD} → Sorted Set (TTL: 90 days)
  - modId → score (number of installations)
```

## 🧪 Testing

### Test 1: Valid Heartbeat

```bash
curl -X POST http://localhost:3001/api/telemetry/heartbeat \
  -H "Content-Type: application/json" \
  -d '{
    "userHash": "a1b2c3d4e5f6789012345678901234567890123456789012345678901234567890",
    "appVersion": "2.0.0",
    "platform": "steam",
    "language": "pl",
    "installedModIds": [1, 3, 7, 12],
    "sessionTimeSeconds": 1234,
    "timestamp": "2025-10-27T12:34:56.789Z"
  }'
```

**Expected:** 200 OK

### Test 2: Invalid userHash

```bash
curl -X POST http://localhost:3001/api/telemetry/heartbeat \
  -H "Content-Type: application/json" \
  -d '{
    "userHash": "invalid-hash",
    "appVersion": "2.0.0",
    "platform": "steam",
    "language": "pl",
    "installedModIds": [],
    "sessionTimeSeconds": 100,
    "timestamp": "2025-10-27T12:34:56.789Z"
  }'
```

**Expected:** 400 Bad Request (validation error)

### Test 3: Rate Limiting

```bash
# First request
curl -X POST http://localhost:3001/api/telemetry/heartbeat -d '...'
# Returns: 200 OK

# Immediate second request (same userHash)
curl -X POST http://localhost:3001/api/telemetry/heartbeat -d '...'
# Returns: 429 Too Many Requests
```

## 📈 Monitoring

### Metrics to Track:
- Requests per minute
- Success rate (200 vs 4xx/5xx)
- Rate limit hits (429 responses)
- Average response time
- Redis memory usage
- Daily active users count

### Logging:

```javascript
// Add logging middleware
app.use('/api/telemetry', (req, res, next) => {
  const start = Date.now();
  
  res.on('finish', () => {
    const duration = Date.now() - start;
    console.log({
      method: req.method,
      path: req.path,
      status: res.statusCode,
      duration: `${duration}ms`,
      userAgent: req.get('user-agent'),
      timestamp: new Date().toISOString()
    });
  });
  
  next();
});
```

## 🔒 Security Considerations

### 1. No PII Collection
- Endpoint nie zbiera IP addresses (można usunąć z logów Nginx)
- userHash jest jednostronnie zahashowany
- Brak możliwości reverse engineering do raw Hardware ID

### 2. Rate Limiting
- Zabezpiecza przed abuse/spam
- 1 request / 10 min per user jest wystarczający

### 3. Input Validation
- Strict schema validation (Joi)
- Reject invalid data early
- Prevent injection attacks

### 4. CORS Policy

```javascript
// Allow only from susmodder.app domain
app.use('/api/telemetry', cors({
  origin: ['https://susmodder.app', 'http://localhost'],
  methods: ['POST'],
  allowedHeaders: ['Content-Type']
}));
```

## 📝 Environment Variables

```env
# .env file
REDIS_HOST=localhost
REDIS_PORT=6379
REDIS_PASSWORD=your_redis_password
REDIS_DB=0

PORT=3001
NODE_ENV=production
```

---

**Status:** ✅ Ready for Implementation  
**Estimated time:** 3-4 godziny (z testami)
