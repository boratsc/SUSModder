# AI Support Assistant — API Contract (v2)

> **Base URL:** `https://susmodder.app/api/v2/support`  
> **Version:** 1.0.0 (MVP KB-only)  
> **Status:** Production — KB-only mode (`AI_SUPPORT_LLM_ENABLED=false`)  
> **Last updated:** 2026-06-11

---

## 1. Overview

The AI Support Assistant is a **first-line help system** for SUSModder users. A user describes a problem; the backend searches a curated knowledge base (KB) and returns **structured repair steps** with action codes the client maps to UI buttons.

### Key design decisions

- **KB-first**: The knowledge base always answers first. LLM is only used for medium/low confidence KB matches and is behind a feature flag disabled on launch.
- **No file upload**: ZIPs and full logs are never sent to the API in MVP. The client generates diagnostic reports locally.
- **No raw problem storage**: Session metadata is stored; the raw `problem` text is never persisted server-side.
- **PII redacted**: Backend scrubs Windows user paths, tokens, and emails before processing.
- **Bilingual**: `pl` (Polish, canonical) and `en` (English, managed translation).

### Privacy notice (show to users before first use)

> SUSModder sends: your problem description (redacted), diagnostic codes (e.g. "launch.firewall.rule_missing_or_blocked"), app version, platform mode, update channel, and limited BepInEx log summary.
> **We never send**: full log files, crash dumps, ZIP archives, Discord tokens, personal file paths, or system credentials.
> When enabled, an AI model (OpenRouter free tier) may help rewrite KB answers. The model never runs PowerShell or modifies your system.

---

## 2. Endpoints

### 2.1 `GET /api/v2/support/knowledge/meta`

Returns KB metadata for client-side caching decisions.

**No auth required.**

**Response `200`:**
```json
{
  "data": {
    "version": "2026-06-11.20",
    "locales": ["en", "pl"],
    "categories": ["antivirus", "firewall", "install", "launch", "mods", "network", "proximity", "sustats"],
    "articleCount": 20,
    "loadedAt": "2026-06-11T16:04:19.505Z"
  }
}
```

**Fields:**
| Field | Type | Description |
|-------|------|-------------|
| `version` | string | KB version (`YYYY-MM-DD.articleCount`) — use for cache busting |
| `locales` | string[] | Supported languages (always `["en","pl"]` in MVP) |
| `categories` | string[] | Valid values for `categoryHint` in query |
| `articleCount` | int | Total published articles |
| `loadedAt` | ISO8601 | When the KB was loaded into memory |

---

### 2.2 `POST /api/v2/support/query`

**The main endpoint.** Searches KB for matching articles and returns structured repair steps.

**Headers:**
| Header | Required | Description |
|--------|----------|-------------|
| `Content-Type: application/json` | Yes | – |
| `X-User-Hash` | No | Anonymous user identity (SHA256 of HWID) for abuse limits |

**Rate limit:** 10 requests/minute/IP

**Request body:**
```json
{
  "language": "pl",
  "problem": "Nie mogę dołączyć do lobby, disconnected from server",
  "categoryHint": "network",
  "app": {
    "version": "3.0.0",
    "platformMode": "steam",
    "updateChannel": "release"
  },
  "diagnostics": {
    "diagnosisCodes": ["launch.firewall.rule_missing_or_blocked"],
    "modTypes": ["full"],
    "amongUsVersion": "2026.5.14",
    "wasRunAsAdmin": false,
    "firewallExceptionExists": false,
    "defenderEventCodes": [1123],
    "bepInExSummary": [
      "BepInEx log stale after launch",
      "Access denied while loading plugin"
    ]
  }
}
```

**Request fields:**
| Field | Type | Required | Limits | Description |
|-------|------|----------|--------|-------------|
| `language` | `"pl"`\|`"en"` | **Yes** | – | Response language. PL is canonical source. |
| `problem` | string | **Yes** | 20–2000 chars | User's free-text problem description |
| `categoryHint` | string | No | See categories list | Narrows search scope |
| `app.version` | string | No | ≤50 chars | SUSModder client version |
| `app.platformMode` | string | No | `steam`\|`epic`\|`xbox`\|`itchio` | Game platform |
| `app.updateChannel` | string | No | `release`\|`beta` | Update channel |
| `diagnostics.diagnosisCodes` | string[] | No | ≤10 items | Client-generated diagnosis codes |
| `diagnostics.modTypes` | string[] | No | ≤5 items, `full`\|`dll` | Active mod types |
| `diagnostics.amongUsVersion` | string | No | ≤30 chars | Installed Among Us version |
| `diagnostics.wasRunAsAdmin` | boolean | No | – | Whether the app was run elevated |
| `diagnostics.firewallExceptionExists` | boolean | No | – | Whether a firewall rule exists |
| `diagnostics.defenderEventCodes` | int[] | No | ≤20 items | Windows Defender event IDs |
| `diagnostics.bepInExSummary` | string[] | No | ≤20 lines, ≤300 chars each | Redacted BepInEx log fragments |

**Success response `200`:**
```json
{
  "data": {
    "supportSessionId": "SUP-2026-37WOTA",
    "source": "knowledge_base",
    "confidence": "high",
    "summary": "Gra nie może połączyć się z serwerem...",
    "steps": [
      {
        "text": "Uruchom narzędzie naprawy firewalla w SUSModder...",
        "actionCode": "open_firewall_repair",
        "requiresAdmin": true
      }
    ],
    "warnings": [
      "Dodawaj wyjątki firewalla tylko dla plików z oficjalnego źródła SUSModder."
    ],
    "matchedArticles": [
      {
        "id": "network_disconnected_from_server",
        "title": "Rozłączono z serwera – nie można dołączyć do lobby",
        "category": "network",
        "severity": "high",
        "score": 51
      }
    ],
    "needsDiagnosticReport": false,
    "discordRecommended": false,
    "safetyNotice": "Postępuj zgodnie z instrukcjami..."
  },
  "meta": {
    "kbVersion": "2026-06-11.20",
    "model": null,
    "llmUsed": false,
    "cached": false
  }
}
```

**Response fields — `data`:**
| Field | Type | Description |
|-------|------|-------------|
| `supportSessionId` | string | Unique session ID (`SUP-YYYY-XXXXXX`). Use for feedback. |
| `source` | `"knowledge_base"`\|`"llm"`\|`"fallback"` | Where the answer came from |
| `confidence` | `"high"`\|`"medium"`\|`"low"` | Match confidence |
| `summary` | string | One-sentence problem diagnosis |
| `steps` | Step[] | Ordered repair steps (max 8) |
| `steps[].text` | string | Human-readable instruction |
| `steps[].actionCode` | ActionCode | Client action to show as button |
| `steps[].requiresAdmin` | boolean | Whether step needs admin elevation |
| `warnings` | string[] | Safety warnings to display |
| `matchedArticles` | Article[] | KB articles that matched (top 5) |
| `needsDiagnosticReport` | boolean | Client should suggest generating a report ZIP |
| `discordRecommended` | boolean | Client should show Discord invite links |
| `safetyNotice` | string | Generic safety reminder |

**Response fields — `meta`:**
| Field | Type | Description |
|-------|------|-------------|
| `kbVersion` | string | KB version used |
| `model` | string\|null | AI model used (null = KB-only) |
| `llmUsed` | boolean | Whether LLM was involved |
| `cached` | boolean | Whether response came from cache |

**Action codes (allowlist):**
| Code | Meaning | Client action |
|------|---------|---------------|
| `none` | No special action | Show as plain text step |
| `open_logs` | Open log folder | Open BepInEx/logs in Explorer |
| `open_mod_folder` | Open mod folder | Open Among Us mod directory in Explorer |
| `open_firewall_repair` | Open firewall repair | Trigger built-in firewall repair tool |
| `open_defender_instructions` | Show Defender guide | Open Windows Security or show guide |
| `generate_report` | Generate diagnostic report | Trigger local ZIP generation |
| `open_discord` | Open Discord | Show Discord invite or open Discord |

**Error responses:**
| Status | Code | When |
|--------|------|------|
| `400` | `VALIDATION_ERROR` | Invalid request (e.g., problem too short, bad language) |
| `429` | `RATE_LIMITED` | Too many requests (10/min/IP) |
| `500` | `INTERNAL_ERROR` | Server-side failure |

**Fallback response (no match):**
```json
{
  "data": {
    "supportSessionId": "SUP-2026-XXXXXX",
    "source": "fallback",
    "confidence": "low",
    "summary": "Nie znaleziono pasujących artykułów w bazie wiedzy.",
    "steps": [
      {
        "text": "Wygeneruj raport diagnostyczny i dołącz go na Discordzie SUSModder.",
        "actionCode": "generate_report",
        "requiresAdmin": false
      }
    ],
    "matchedArticles": [],
    "needsDiagnosticReport": true,
    "discordRecommended": true,
    "safetyNotice": "Nie pobieraj plików z nieznanych źródeł."
  },
  "meta": { ... }
}
```

---

### 2.3 `POST /api/v2/support/feedback`

Record whether the support answer helped.

**Rate limit:** 30 requests/minute/IP

**Request body:**
```json
{
  "supportSessionId": "SUP-2026-37WOTA",
  "result": "helped",
  "articleIds": ["network_disconnected_from_server"],
  "diagnosisCodes": ["launch.firewall.rule_missing_or_blocked"],
  "language": "pl",
  "optionalComment": "Pomogło po dodaniu wyjątku firewall"
}
```

**Request fields:**
| Field | Type | Required | Limits | Description |
|-------|------|----------|--------|-------------|
| `supportSessionId` | string | **Yes** | Format: `SUP-YYYY-XXXXXX` | From query response |
| `result` | string | **Yes** | `helped`\|`not_helped`\|`report_generated`\|`discord_clicked` | Outcome |
| `articleIds` | string[] | No | ≤10 items | Article slugs from matchedArticles |
| `diagnosisCodes` | string[] | No | ≤10 items | Diagnosis codes |
| `language` | string | **Yes** | `pl`\|`en` | User's language |
| `optionalComment` | string | No | ≤1000 chars | Free-text feedback (redacted) |

**Success response `201`:**
```json
{
  "data": {
    "recorded": true,
    "sessionId": "SUP-2026-37WOTA"
  }
}
```

**Limits:**
- Max 5 feedback entries per session
- Session must exist (404 otherwise)
- Comment is PII-redacted before storage

**Error responses:**
| Status | Code | When |
|--------|------|------|
| `400` | `VALIDATION_ERROR` | Invalid session ID format, bad result, or max feedback reached |
| `404` | `NOT_FOUND` | Session does not exist |
| `500` | `INTERNAL_ERROR` | Server failure |

---

### 2.4 `POST /api/v2/support/report-metadata`

Track that a user generated a diagnostic report. No file upload — just metadata.

**Headers:**
| Header | Required | Description |
|--------|----------|-------------|
| `X-User-Hash` | No | Anonymous identity for daily limits |

**Request body:**
```json
{
  "supportSessionId": "SUP-2026-37WOTA",
  "articleCount": 3,
  "diagnosisCodes": ["launch.firewall.rule_missing_or_blocked"],
  "language": "pl"
}
```

**Request fields:**
| Field | Type | Required | Limits | Description |
|-------|------|----------|--------|-------------|
| `supportSessionId` | string | No | – | Associated session (optional) |
| `articleCount` | int | **Yes** | 0–50 | Number of articles the user tried |
| `diagnosisCodes` | string[] | No | ≤20 items | Diagnosis codes |
| `language` | string | **Yes** | `pl`\|`en` | User's language |

**Success response `201`:**
```json
{
  "data": {
    "recorded": true
  }
}
```

---

### 2.5 `GET /api/v2/support/health`

System health — no secrets returned.

**Response `200`:**
```json
{
  "data": {
    "kbLoaded": true,
    "kbVersion": "2026-06-11.20",
    "articleCount": 20,
    "llmEnabled": false,
    "aiProviderReachable": true
  }
}
```

---

## 3. Client integration guide

### 3.1 Basic flow

```
1. User opens "AI Help" tab
2. Client builds diagnostic context (diagnosisCodes, app version, platform, etc.)
3. User types problem description → Client sends POST /query
4. Client shows:
   - summary
   - steps as interactive buttons (mapped from actionCode)
   - warnings
   - safetyNotice
5. User clicks "Helped" or "Didn't help" → Client sends POST /feedback
6. If not helped: Client suggests generating diagnostic report → Client sends POST /report-metadata
7. Client generates local ZIP and shows Discord invite links
```

### 3.2 Action code mapping (C# pseudocode)

```csharp
void ExecuteAction(string actionCode)
{
    switch (actionCode)
    {
        case "none":                         break;
        case "open_logs":                   OpenFolder(Path.Combine(modFolder, "BepInEx"));
        case "open_mod_folder":             OpenFolder(modFolder);
        case "open_firewall_repair":        _firewallRepairService.OpenRepairTool();
        case "open_defender_instructions":  _defenderService.ShowInstructions();
        case "generate_report":             _diagnosticService.GenerateReportZip();
        case "open_discord":                _discordService.OpenInvite();
        default:                            break; // ignore unknown codes
    }
}
```

**Important:** Never auto-execute `requiresAdmin: true` steps. Always show UAC prompt with step explanation.

### 3.3 Network / timeout

- Timeout: 15 seconds for `/query` (LLM may take up to 12s when enabled)
- Fallback: Show generic "service unavailable" message with "generate report" button
- Retry: Do not retry automatically — let the user decide

### 3.4 Caching

- Cache `GET /knowledge/meta` for 5 minutes (use `version` field as cache key)
- Do NOT cache `POST /query` responses (they contain session IDs)
- `POST /feedback` and `POST /report-metadata` are fire-and-forget (non-blocking, best-effort)

---

## 4. Security boundaries

### What the client MUST NOT send

- Full `LogOutput.log`, `ErrorLog.log`, `Player.log` contents
- Complete diagnostic ZIP files
- Raw file paths with usernames (`C:\Users\John\...`)
- Discord tokens, API keys, passwords
- Full system configuration dumps
- Crash dumps or memory dumps

### What the client CAN send

- Short problem description (20–2000 chars, user-written)
- Diagnosis codes from client-side diagnostic engine
- App version, platform mode, update channel
- Limited BepInEx summary (max 20 lines × 300 chars each)
- Boolean flags: wasRunAsAdmin, firewallExceptionExists

### Backend-side protections

- PII redaction before logging/processing (paths, tokens, emails)
- Problem text never stored in database
- LLM system prompt prevents command generation outside allowlist
- All `actionCode` values validated against allowlist before returning

---

## 5. KB categories reference

| Category | Typical problems | Example diagnosis codes |
|----------|-----------------|------------------------|
| `install` | Wrong path, missing permissions, platform mismatch | `install.wrong_path`, `install.no_write_permissions` |
| `launch` | Game doesn't start, black screen, crash after update | `launch.game_does_not_start`, `launch.black_screen` |
| `network` | Disconnected from server, can't join lobby | `network.disconnected_from_server`, `network.cannot_join_lobby` |
| `firewall` | Firewall blocks Among Us | `launch.firewall.rule_missing_or_blocked` |
| `antivirus` | Defender quarantines DLL, Controlled Folder Access | `launch.defender.quarantine_detected` |
| `mods` | Wrong version, DLL conflict, missing BepInEx | `mods.au_version_mismatch`, `mods.dll_conflict` |
| `proximity` | No microphone, can't hear others | `proximity.no_microphone`, `proximity.cannot_hear_others` |
| `sustats` | Stats not saving | `sustats.not_saving` |
