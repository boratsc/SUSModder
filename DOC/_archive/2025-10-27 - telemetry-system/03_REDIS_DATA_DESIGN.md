# Redis Data Design - Telemetry System

## 📋 Przegląd

Dokumentacja struktury danych w Redis dla systemu telemetrii SUSModder.

## 🗄️ Redis Keys Structure

### 1. Heartbeat Data (Raw)

**Format:** `telemetry:heartbeat:{userHash}:{timestamp}`  
**Type:** String (JSON)  
**TTL:** 90 days (7,776,000 seconds)

```redis
SET telemetry:heartbeat:a1b2c3...:1730034896789
{
  "userHash": "a1b2c3d4e5f6...",
  "appVersion": "2.0.0",
  "platform": "steam",
  "language": "pl",
  "installedModIds": [1, 3, 7, 12],
  "sessionTimeSeconds": 1234,
  "timestamp": "2025-10-27T12:34:56.789Z",
  "receivedAt": "2025-10-27T12:34:57.123Z"
}
EX 7776000
```

**Przykładowe komendy:**
```bash
# Zapisz heartbeat
redis-cli SET "telemetry:heartbeat:a1b2c3:1730034896789" '{"userHash":"a1b2c3","appVersion":"2.0.0"}' EX 7776000

# Pobierz heartbeat
redis-cli GET "telemetry:heartbeat:a1b2c3:1730034896789"

# Znajdź wszystkie heartbeaty użytkownika
redis-cli KEYS "telemetry:heartbeat:a1b2c3:*"
```

---

### 2. Rate Limiting

**Format:** `telemetry:ratelimit:{userHash}`  
**Type:** String  
**TTL:** 10 minutes (600 seconds)

```redis
SET telemetry:ratelimit:a1b2c3d4e5f6... "1" EX 600
```

**Przykładowe komendy:**
```bash
# Ustaw rate limit
redis-cli SETEX "telemetry:ratelimit:a1b2c3" 600 "1"

# Sprawdź czy użytkownik jest rate limited
redis-cli GET "telemetry:ratelimit:a1b2c3"

# Sprawdź ile czasu do wygaśnięcia
redis-cli TTL "telemetry:ratelimit:a1b2c3"
```

---

### 3. Daily Unique Users

**Format:** `telemetry:daily:users:{YYYY-MM-DD}`  
**Type:** Set  
**TTL:** 365 days (31,536,000 seconds)

```redis
SADD telemetry:daily:users:2025-10-27 "a1b2c3d4e5f6..."
SADD telemetry:daily:users:2025-10-27 "f6e5d4c3b2a1..."
EXPIRE telemetry:daily:users:2025-10-27 31536000
```

**Przykładowe komendy:**
```bash
# Dodaj użytkownika do dzisiejszego setu
redis-cli SADD "telemetry:daily:users:2025-10-27" "a1b2c3"

# Pobierz liczbę unikalnych użytkowników dzisiaj
redis-cli SCARD "telemetry:daily:users:2025-10-27"

# Pobierz wszystkie hashe użytkowników (tylko dla admin/debug)
redis-cli SMEMBERS "telemetry:daily:users:2025-10-27"

# Sprawdź czy użytkownik był aktywny dzisiaj
redis-cli SISMEMBER "telemetry:daily:users:2025-10-27" "a1b2c3"
```

---

### 4. Daily Statistics

**Format:** `telemetry:daily:stats:{YYYY-MM-DD}`  
**Type:** Hash  
**TTL:** 365 days (31,536,000 seconds)

```redis
HINCRBY telemetry:daily:stats:2025-10-27 "platform:steam" 1
HINCRBY telemetry:daily:stats:2025-10-27 "platform:epic" 1
HINCRBY telemetry:daily:stats:2025-10-27 "version:2.0.0" 1
HINCRBY telemetry:daily:stats:2025-10-27 "language:pl" 1
EXPIRE telemetry:daily:stats:2025-10-27 31536000
```

**Struktura Hash:**
```
platform:steam → 120
platform:epic → 45
version:2.0.0 → 150
version:1.9.9 → 15
language:pl → 140
language:en → 25
```

**Przykładowe komendy:**
```bash
# Inkrementuj counter dla Steam
redis-cli HINCRBY "telemetry:daily:stats:2025-10-27" "platform:steam" 1

# Pobierz liczbę użytkowników Steam
redis-cli HGET "telemetry:daily:stats:2025-10-27" "platform:steam"

# Pobierz wszystkie statystyki dnia
redis-cli HGETALL "telemetry:daily:stats:2025-10-27"

# Pobierz tylko statystyki platform
redis-cli HKEYS "telemetry:daily:stats:2025-10-27" | grep "platform:"
```

---

### 5. Mod Popularity

**Format:** `telemetry:mods:popularity:{YYYY-MM-DD}`  
**Type:** Sorted Set  
**TTL:** 90 days (7,776,000 seconds)

```redis
ZINCRBY telemetry:mods:popularity:2025-10-27 1 1    # Mod ID 1
ZINCRBY telemetry:mods:popularity:2025-10-27 1 3    # Mod ID 3
ZINCRBY telemetry:mods:popularity:2025-10-27 1 7    # Mod ID 7
EXPIRE telemetry:mods:popularity:2025-10-27 7776000
```

**Struktura Sorted Set:**
```
Mod ID → Score (liczba instalacji)
1 → 120
3 → 98
7 → 85
12 → 67
...
```

**Przykładowe komendy:**
```bash
# Inkrementuj score dla moda 1
redis-cli ZINCRBY "telemetry:mods:popularity:2025-10-27" 1 1

# Pobierz Top 10 modów (od najbardziej popularnych)
redis-cli ZREVRANGE "telemetry:mods:popularity:2025-10-27" 0 9 WITHSCORES

# Pobierz score dla konkretnego moda
redis-cli ZSCORE "telemetry:mods:popularity:2025-10-27" "1"

# Pobierz rangę moda (pozycja na liście popularności)
redis-cli ZREVRANK "telemetry:mods:popularity:2025-10-27" "1"

# Pobierz liczbę unikalnych modów zainstalowanych dzisiaj
redis-cli ZCARD "telemetry:mods:popularity:2025-10-27"
```

---

## 📊 Query Patterns (Analytics)

### 1. Daily Active Users (DAU)

```javascript
// Node.js
const today = new Date().toISOString().split('T')[0];
const dau = await redis.scard(`telemetry:daily:users:${today}`);
console.log(`DAU: ${dau}`);
```

```bash
# Redis CLI
redis-cli SCARD "telemetry:daily:users:2025-10-27"
```

---

### 2. Weekly Active Users (WAU)

```javascript
// Node.js
const dates = getLast7Days(); // ['2025-10-21', '2025-10-22', ...]
const keys = dates.map(d => `telemetry:daily:users:${d}`);

// Union wszystkich setów z ostatnich 7 dni
const tempKey = `telemetry:temp:wau:${Date.now()}`;
await redis.sunionstore(tempKey, ...keys);
const wau = await redis.scard(tempKey);
await redis.del(tempKey); // Usuń temp key

console.log(`WAU: ${wau}`);
```

```bash
# Redis CLI
redis-cli SUNIONSTORE "temp:wau" \
  "telemetry:daily:users:2025-10-21" \
  "telemetry:daily:users:2025-10-22" \
  "telemetry:daily:users:2025-10-23" \
  "telemetry:daily:users:2025-10-24" \
  "telemetry:daily:users:2025-10-25" \
  "telemetry:daily:users:2025-10-26" \
  "telemetry:daily:users:2025-10-27"

redis-cli SCARD "temp:wau"
redis-cli DEL "temp:wau"
```

---

### 3. Monthly Active Users (MAU)

```javascript
// Node.js
const dates = getLast30Days();
const keys = dates.map(d => `telemetry:daily:users:${d}`);

const tempKey = `telemetry:temp:mau:${Date.now()}`;
await redis.sunionstore(tempKey, ...keys);
const mau = await redis.scard(tempKey);
await redis.del(tempKey);

console.log(`MAU: ${mau}`);
```

---

### 4. Platform Distribution (Steam vs Epic)

```javascript
// Node.js
const today = new Date().toISOString().split('T')[0];
const statsKey = `telemetry:daily:stats:${today}`;

const steamCount = parseInt(await redis.hget(statsKey, 'platform:steam') || '0');
const epicCount = parseInt(await redis.hget(statsKey, 'platform:epic') || '0');

const total = steamCount + epicCount;
const steamPercent = ((steamCount / total) * 100).toFixed(1);
const epicPercent = ((epicCount / total) * 100).toFixed(1);

console.log({
  steam: `${steamCount} (${steamPercent}%)`,
  epic: `${epicCount} (${epicPercent}%)`
});
```

```bash
# Redis CLI
redis-cli HGET "telemetry:daily:stats:2025-10-27" "platform:steam"
redis-cli HGET "telemetry:daily:stats:2025-10-27" "platform:epic"
```

---

### 5. Version Adoption

```javascript
// Node.js
const today = new Date().toISOString().split('T')[0];
const statsKey = `telemetry:daily:stats:${today}`;

const stats = await redis.hgetall(statsKey);
const versions = {};

for (const [key, value] of Object.entries(stats)) {
  if (key.startsWith('version:')) {
    const version = key.replace('version:', '');
    versions[version] = parseInt(value);
  }
}

// Sortuj od najbardziej popularnej
const sorted = Object.entries(versions)
  .sort(([, a], [, b]) => b - a);

console.log('Version distribution:', sorted);
```

```bash
# Redis CLI
redis-cli HGETALL "telemetry:daily:stats:2025-10-27" | grep "version:"
```

---

### 6. Top 10 Most Popular Mods

```javascript
// Node.js
const today = new Date().toISOString().split('T')[0];
const popularityKey = `telemetry:mods:popularity:${today}`;

const top10 = await redis.zrevrange(popularityKey, 0, 9, 'WITHSCORES');

// Parse results: [modId1, score1, modId2, score2, ...]
const mods = [];
for (let i = 0; i < top10.length; i += 2) {
  mods.push({
    modId: parseInt(top10[i]),
    installations: parseInt(top10[i + 1])
  });
}

console.log('Top 10 mods:', mods);
```

```bash
# Redis CLI
redis-cli ZREVRANGE "telemetry:mods:popularity:2025-10-27" 0 9 WITHSCORES
```

---

### 7. Average Session Time

```javascript
// Node.js
const today = new Date().toISOString().split('T')[0];
const startOfDay = new Date(today).getTime();
const endOfDay = startOfDay + (24 * 60 * 60 * 1000);

// Znajdź wszystkie heartbeaty z dzisiaj
const pattern = `telemetry:heartbeat:*:${startOfDay}*`;
const keys = await redis.keys(pattern);

let totalTime = 0;
let count = 0;

for (const key of keys) {
  const data = JSON.parse(await redis.get(key));
  if (data && data.sessionTimeSeconds) {
    totalTime += data.sessionTimeSeconds;
    count++;
  }
}

const avgSessionTime = count > 0 ? Math.round(totalTime / count) : 0;
const avgMinutes = Math.round(avgSessionTime / 60);

console.log(`Average session time: ${avgMinutes} minutes`);
```

**Uwaga:** To może być wolne dla dużych dataset'ów. Lepiej agregować w locie podczas zapisywania.

**Optymalizacja:** Dodaj daily aggregated session time:

```javascript
// Podczas zapisywania heartbeat
await redis.hincrby(`telemetry:daily:stats:${today}`, 'totalSessionTime', sessionTimeSeconds);
await redis.hincrby(`telemetry:daily:stats:${today}`, 'sessionCount', 1);

// Podczas odczytu
const totalTime = parseInt(await redis.hget(statsKey, 'totalSessionTime') || '0');
const sessionCount = parseInt(await redis.hget(statsKey, 'sessionCount') || '0');
const avgTime = sessionCount > 0 ? Math.round(totalTime / sessionCount) : 0;
```

---

### 8. Language Distribution

```javascript
// Node.js
const today = new Date().toISOString().split('T')[0];
const statsKey = `telemetry:daily:stats:${today}`;

const stats = await redis.hgetall(statsKey);
const languages = {};

for (const [key, value] of Object.entries(stats)) {
  if (key.startsWith('language:')) {
    const lang = key.replace('language:', '');
    languages[lang] = parseInt(value);
  }
}

console.log('Language distribution:', languages);
// Output: { pl: 140, en: 25 }
```

---

## 🔧 Maintenance Queries

### 1. Cleanup Old Keys (Manual)

```bash
# Znajdź wszystkie klucze starsze niż 90 dni
redis-cli KEYS "telemetry:heartbeat:*" | while read key; do
  ttl=$(redis-cli TTL "$key")
  if [ $ttl -lt 0 ]; then
    redis-cli DEL "$key"
  fi
done
```

**Uwaga:** Redis automatycznie usuwa klucze z TTL, ale można sprawdzić manualnie.

---

### 2. Memory Usage Analysis

```bash
# Sprawdź ile pamięci zajmują klucze telemetry
redis-cli --bigkeys --pattern "telemetry:*"

# Sprawdź całkowitą liczbę kluczy
redis-cli DBSIZE

# Sprawdź wykorzystanie pamięci Redis
redis-cli INFO memory
```

---

### 3. Export Data to JSON

```javascript
// Node.js - Export daily stats to JSON
const today = '2025-10-27';

const data = {
  date: today,
  dau: await redis.scard(`telemetry:daily:users:${today}`),
  stats: await redis.hgetall(`telemetry:daily:stats:${today}`),
  topMods: await redis.zrevrange(`telemetry:mods:popularity:${today}`, 0, 9, 'WITHSCORES')
};

const fs = require('fs');
fs.writeFileSync(`telemetry-${today}.json`, JSON.stringify(data, null, 2));
```

---

## 📈 Performance Considerations

### 1. Memory Estimation

**Per heartbeat:**
- Key: ~80 bytes
- Value (JSON): ~200 bytes
- Total: ~280 bytes

**Daily estimation:**
- 1,000 users/day: ~280 KB
- 10,000 users/day: ~2.8 MB
- 100,000 users/day: ~28 MB

**90-day retention:**
- 1,000 users/day: ~25 MB
- 10,000 users/day: ~250 MB
- 100,000 users/day: ~2.5 GB

**Daily aggregates:**
- Unique users set: ~64 bytes * users
- Stats hash: ~1 KB
- Mod popularity: ~16 bytes * unique mods

**Total Redis memory (estimate for 10k DAU):**
- Raw heartbeats (90 days): ~250 MB
- Daily aggregates (365 days): ~20 MB
- Rate limits (active): ~1 MB
- **Total: ~300 MB**

### 2. Query Performance

- `SCARD` (DAU): O(1) - instant
- `SUNIONSTORE` (WAU/MAU): O(N*M) - fast dla 7-30 dni
- `HGETALL` (daily stats): O(N) - szybkie (małe hashe)
- `ZREVRANGE` (top mods): O(log(N)+M) - bardzo szybkie
- `KEYS` pattern match: O(N) - **unikać w production!**

### 3. Optimization Tips

✅ **DO:**
- Używaj `SCAN` zamiast `KEYS` dla pattern matching
- Agreguj dane daily (nie query raw heartbeats)
- Cache wyniki analytics na 5-15 minut
- Używaj pipeline dla batch operations

❌ **DON'T:**
- Nie używaj `KEYS *` w production
- Nie query wszystkich heartbeatów naraz
- Nie trzymaj temp keys dłużej niż potrzeba

---

## 🧪 Testing Redis Queries

### Setup Test Data

```bash
# Dodaj testowych użytkowników
redis-cli SADD "telemetry:daily:users:2025-10-27" "user1" "user2" "user3"

# Dodaj statystyki
redis-cli HINCRBY "telemetry:daily:stats:2025-10-27" "platform:steam" 50
redis-cli HINCRBY "telemetry:daily:stats:2025-10-27" "platform:epic" 20

# Dodaj popularność modów
redis-cli ZINCRBY "telemetry:mods:popularity:2025-10-27" 10 1
redis-cli ZINCRBY "telemetry:mods:popularity:2025-10-27" 5 3
redis-cli ZINCRBY "telemetry:mods:popularity:2025-10-27" 3 7
```

### Verify

```bash
redis-cli SCARD "telemetry:daily:users:2025-10-27"
# Output: 3

redis-cli HGETALL "telemetry:daily:stats:2025-10-27"
# Output: platform:steam 50 platform:epic 20

redis-cli ZREVRANGE "telemetry:mods:popularity:2025-10-27" 0 -1 WITHSCORES
# Output: 1 10 3 5 7 3
```

---

**Status:** ✅ Dokumentacja kompletna  
**Redis Version:** >= 6.0
