# Migration Plan - Przejście dla Istniejących Użytkowników

**Data:** 2025-10-28
**Current production:** v2.0.1 (old updater)
**Target:** v2.1.0+ (Velopack)

---

## Problem Statement

Istniejący użytkownicy na v2.0.1 używają starego update mechanizmu:
- `AppUpdateService` + `Updater.exe`
- Endpoint: `/api/download-latest` (ZIP)

**Challenge:** Jak przejść na Velopack bez breaking existing installations?

---

## Strategy: Bridge Update (v2.0.2)

### Overview

Create **v2.0.2** as "bridge version" która:
1. ✅ Aktualizuje się starym mechanizmem (dla users na 2.0.1)
2. ✅ Zawiera Velopack framework
3. ✅ Kolejne updatey (2.1.0+) używają Velopack

```
┌──────────────────────────────────────────────────────┐
│ Migration Path                                       │
└──────────────────────────────────────────────────────┘

v2.0.1 (production)
  │ Old updater
  ▼
v2.0.2 (bridge) ◄─── CRITICAL VERSION
  │ ├─ Contains old updater (for this update only)
  │ └─ Installs Velopack framework
  │ Velopack
  ▼
v2.1.0+ (Velopack-only)
  │ Delta updates
  ▼
Future versions
```

---

## Phase 1: Create Bridge Version (v2.0.2)

### 1.1. Code Changes for v2.0.2

**Keep both updaters temporarily:**

```
SUSModder/
├─ Services/
│   ├─ AppUpdateService.cs       (OLD - keep for now)
│   └─ VelopackUpdateService.cs  (NEW - add)
├─ Program.cs                     (Add Velopack hooks)
└─ MainWindowViewModel.cs         (Detect updater version)

Updater/                          (Keep for 2.0.1 → 2.0.2 transition)
└─ Program.cs
```

**Update detection logic:**

```csharp
// MainWindowViewModel.cs
public async void CheckForUpdates()
{
    // Detect which updater to use
    if (IsVelopackInstalled())
    {
        // Use new Velopack updater
        await CheckForUpdatesVelopack();
    }
    else
    {
        // Use old updater (last time)
        await CheckForUpdatesLegacy();
    }
}

private bool IsVelopackInstalled()
{
    // Check if Velopack is present
    // Velopack stores Update.exe in the app root directory
    var appDir = AppContext.BaseDirectory;
    var velopackUpdatePath = Path.Combine(appDir, "..", "Update.exe");
    return File.Exists(velopackUpdatePath);
}

private async Task CheckForUpdatesLegacy()
{
    // Old AppUpdateService logic
    var result = await _appUpdateService.CheckForUpdateAsync();
    // ... existing code ...
}

private async Task CheckForUpdatesVelopack()
{
    // New VelopackUpdateService logic
    var result = await _velopackUpdateService.CheckForUpdateAsync();
    // ... new code ...
}
```

### 1.2. Backend for v2.0.2

**Maintain both endpoints temporarily:**

```
/api/download-latest                     → v2.0.2.zip (for old updater)
/releases/                               → Velopack releases directory
  ├─ releases.{channel}.json             → Release manifest
  └─ SUSModder-{version}-win-full.nupkg  → Velopack package
```

**v2.0.2 deployment:**
```bash
# 1. Install Velopack CLI tool
dotnet tool install -g vpk

# 2. Build with old updater (for legacy path)
dotnet publish Updater/Updater.csproj -c Release -r win-x64 --self-contained

# 3. Build main app for Velopack
dotnet publish SUSModder/SUSModder.csproj -c Release -r win-x64 --self-contained -o ./publish/susmodder

# 4. Create ZIP (for old updater path - 2.0.1 → 2.0.2 transition)
cd publish
7z a SUSModder-2.0.2.zip susmodder/

# 5. Create Velopack package
vpk pack `
  --packId SUSModder `
  --packVersion 2.0.2 `
  --packDir ./publish/susmodder `
  --mainExe SUSModder.exe `
  --outputDir ./releases

# 6. Upload both
scp SUSModder-2.0.2.zip server:/var/www/susmodder/updates/
scp releases/* server:/var/www/susmodder/releases/
```

### 1.3. Testing v2.0.2

**Test Case 1: Update from v2.0.1**
```
1. Install v2.0.1 (production)
2. Click "Check for updates"
3. Should detect v2.0.2
4. Download ZIP (old way)
5. Updater.exe applies update
6. App restarts to v2.0.2
7. Verify Velopack installed: Check {InstallDir}\Update.exe and current\ folder
```

**Test Case 2: Fresh Install v2.0.2**
```
1. Download Setup.exe (from Velopack build - vpk pack output)
2. Install
3. Launch app
4. Already on Velopack (skip bridge)
5. Verify app is in {InstallDir}\current\SUSModder.exe
```

---

## Phase 2: Monitor Bridge Adoption

### 2.1. Telemetry

Add telemetry to track adoption:

```csharp
// In app startup
public void TrackUpdateMechanism()
{
    var mechanism = IsVelopackInstalled() ? "velopack" : "legacy";
    _telemetryService.TrackEvent("app_start", new Dictionary<string, string>
    {
        {"updater", mechanism},
        {"version", CurrentVersion}
    });
}
```

### 2.2. Adoption Metrics

**Target before Phase 3:**
```
80%+ users on v2.0.2+
```

**Check via:**
```sql
-- If you have telemetry database
SELECT
    updater_mechanism,
    COUNT(*) as users,
    COUNT(*) * 100.0 / SUM(COUNT(*)) OVER () as percentage
FROM app_starts
WHERE date >= NOW() - INTERVAL '7 days'
GROUP BY updater_mechanism;

Expected after 2-3 weeks:
+-------------------+-------+------------+
| updater_mechanism | users | percentage |
+-------------------+-------+------------+
| velopack          | 820   | 82%        |
| legacy            | 180   | 18%        |
+-------------------+-------+------------+
```

### 2.3. User Communication

**In-app notification (for users still on 2.0.1):**

```
┌────────────────────────────────────────────────┐
│ Ważna aktualizacja dostępna                   │
│                                                │
│ Wersja 2.0.2 zawiera ulepszenia systemu        │
│ aktualizacji. Zalecamy aktualizację.           │
│                                                │
│ [Aktualizuj teraz]  [Później]                  │
└────────────────────────────────────────────────┘
```

**Email/Discord announcement:**
```
Cześć!

Wydaliśmy v2.0.2 z ulepszonami aktualizacji:
✓ Szybsze pobieranie (delta updates)
✓ Mniejsze zużycie danych
✓ Lepsza stabilność

Aktualizacja automatyczna dostępna w aplikacji.

Link do pobrania: https://susmodder.app/download
```

---

## Phase 3: Remove Legacy Updater (v2.1.0)

### 3.1. When to Deploy

**Criteria:**
- ✅ >80% users on v2.0.2+
- ✅ No major issues reported with Velopack
- ✅ 2-3 weeks of monitoring

### 3.2. Code Cleanup

```bash
# Remove old updater
dotnet sln remove Updater/Updater.csproj
rm -rf Updater/

# Remove legacy update code
git rm SUSModder.Core/Services/AppUpdateService.cs

# Update MainWindowViewModel - tylko Velopack
# Remove IsVelopackInstalled() checks
```

### 3.3. Backend Cleanup

```bash
# Keep /api/download-latest for 1-2 months (grace period)
# Add deprecation warning

# Primary path: /releases/ only
```

### 3.4. Documentation Update

Update CLAUDE.md:
```diff
## Update Mechanism

- ❌ OLD: Custom ZIP-based updater with Updater.exe
+ ✅ NEW: Velopack framework (v2.1.0+)

Update process:
1. VelopackUpdateService checks /releases/releases.{channel}.json
2. Downloads delta or full .nupkg
3. Atomic swap with rollback support
4. Stable exe path in current/ directory
```

---

## Phase 4: Long-term Maintenance

### 4.1. Update Cadence

**Recommended:**
```
Minor updates (2.1.x): Every 2-4 weeks
- Bug fixes
- Small features
- Delta updates (~5-15MB)

Major updates (2.x.0): Every 2-3 months
- New features
- Breaking changes
- Full updates (~50MB)
```

### 4.2. Retention Policy

**Server storage:**
```
/releases/
├─ RELEASES (always keep latest)
├─ Last 3 versions (full .nupkg)
└─ Last 5 deltas

Example:
- Keep: 2.1.0, 2.1.1, 2.1.2 (full)
- Keep: All deltas from 2.1.0→2.1.1, 2.1.1→2.1.2, etc.
- Delete: 2.0.x (after 3 months grace period)
```

**Cleanup script:**
```bash
#!/bin/bash
# cleanup-old-releases.sh

cd /var/www/susmodder/releases

# Keep last 3 full versions
ls -t *-full.nupkg | tail -n +4 | xargs rm -f

# Keep last 5 deltas
ls -t *-delta.nupkg | tail -n +6 | xargs rm -f

# Note: Velopack automatically manages releases.{channel}.json
# No need to regenerate manually like Squirrel's RELEASES file
```

### 4.3. Disaster Recovery

**If Velopack completely fails:**

1. **Immediate rollback:**
   ```bash
   # Restore v2.0.2 ZIP endpoint
   cp backups/SUSModder-2.0.2.zip /var/www/susmodder/updates/

   # Update API to return 2.0.2
   curl -X POST https://susmodder.app/api/admin/rollback \
       -d '{"version": "2.0.2"}'
   ```

2. **Hotfix release (v2.0.3):**
   - Build with old updater
   - Deploy as ZIP
   - Fix Velopack issues offline
   - Re-attempt Velopack in v2.2.0

3. **Communication:**
   ```
   Announcement: "Temporary rollback to v2.0.2 due to update
   issues. We're working on a fix. Your data is safe."
   ```

---

## Risk Mitigation

### Risk 1: Users Stuck on v2.0.1

**Scenario:** 20% users nie aktualizują się do 2.0.2

**Mitigation:**
- Keep old endpoint active for 3 months
- In-app "forced update" prompt after 1 month
- Final warning: "v2.0.1 support ends in 30 days"

### Risk 2: Velopack Installation Fails

**Scenario:** v2.0.2 installs ale Velopack nie działa

**Mitigation:**
- v2.0.2 falls back to legacy updater if Velopack init fails
- Telemetry tracks failures
- Hotfix v2.0.3 if >5% failure rate

### Risk 3: Breaking Change in Backend

**Scenario:** /releases/ endpoint misconfigured

**Mitigation:**
- Staging environment with test users
- Gradual rollout (10% → 50% → 100%)
- Health check endpoint:
  ```bash
  curl https://susmodder.app/releases/health
  # Returns: {"status": "ok", "latest": "2.1.0"}
  ```

---

## Timeline Summary

```
┌────────────────────────────────────────────────────────┐
│ Week 1: Develop & Test v2.0.2 (bridge)                │
│   - Dual updater support                               │
│   - Internal testing                                   │
├────────────────────────────────────────────────────────┤
│ Week 2: Deploy v2.0.2 to Production                   │
│   - Monitor adoption                                   │
│   - Track telemetry                                    │
├────────────────────────────────────────────────────────┤
│ Week 3-4: Monitor & Communicate                       │
│   - Send reminders to users on 2.0.1                  │
│   - Target: 80%+ on 2.0.2                             │
├────────────────────────────────────────────────────────┤
│ Week 5: Develop v2.1.0 (Velopack-only)                │
│   - Remove legacy code                                 │
│   - New features                                       │
├────────────────────────────────────────────────────────┤
│ Week 6: Deploy v2.1.0                                 │
│   - Delta updates for users on 2.0.2                  │
│   - Monitor stability                                  │
├────────────────────────────────────────────────────────┤
│ Week 7+: Normal Update Cadence                        │
│   - v2.1.x every 2-4 weeks                            │
│   - Legacy endpoint kept for 2 more months (grace)    │
└────────────────────────────────────────────────────────┘
```

---

## Rollout Checklist

### Before v2.0.2 Release

- [ ] Dual updater logic tested (legacy + Velopack)
- [ ] Backend endpoints configured (/api/download-latest + /releases/)
- [ ] Telemetry tracking updater mechanism
- [ ] Rollback plan documented
- [ ] Support team briefed

### During v2.0.2 Rollout

- [ ] Deploy v2.0.2 to 10% users (canary)
- [ ] Monitor for 24h, check error rates
- [ ] If stable: increase to 50%
- [ ] Monitor for 48h
- [ ] If stable: 100% rollout
- [ ] Send announcement to users

### Before v2.1.0 Release

- [ ] >80% users on v2.0.2+
- [ ] No major Velopack issues reported
- [ ] v2.1.0 tested end-to-end
- [ ] Legacy updater code removed
- [ ] Documentation updated

### After v2.1.0 Release

- [ ] Monitor delta update success rate
- [ ] Check bandwidth savings (delta vs full)
- [ ] Track false positive rate improvement
- [ ] Plan next minor release (2.1.1)

---

## Support Scripts

### Check User Version Distribution

```powershell
# analyze-versions.ps1
param([string]$TelemetryDb)

$query = @"
SELECT
    version,
    updater_mechanism,
    COUNT(*) as user_count,
    MAX(last_seen) as last_active
FROM user_sessions
WHERE last_seen >= NOW() - INTERVAL '7 days'
GROUP BY version, updater_mechanism
ORDER BY version DESC, user_count DESC
"@

Invoke-SqlQuery -Database $TelemetryDb -Query $query
```

### Force Legacy Endpoint for Specific Users

```javascript
// Backend: /api/susmodder-current-version
app.get('/api/susmodder-current-version', (req, res) => {
    const userVersion = req.query.current;
    const userId = req.query.uid;

    // Force 2.0.2 for users on 2.0.1
    if (userVersion === '2.0.1') {
        return res.json({
            version: '2.0.2',
            downloadUrl: '/api/download-latest',  // Legacy ZIP
            updateType: 'legacy'
        });
    }

    // Normal Velopack path
    return res.json({
        version: '2.1.0',
        downloadUrl: '/releases/',
        updateType: 'velopack'
    });
});
```

---

## Next Steps

1. Review this migration plan
2. Implement v2.0.2 (bridge version)
3. Test extensively (see Phase 1.3)
4. Deploy and monitor (Phase 2)
5. Proceed to v2.1.0 when ready (Phase 3)

**See also:**
- [VELOPACK_IMPLEMENTATION.md](./VELOPACK_IMPLEMENTATION.md) - Technical implementation details
- [BACKEND_SETUP.md](./BACKEND_SETUP.md) - Server configuration
- [CODE_EXAMPLES.md](./CODE_EXAMPLES.md) - Ready-to-use code snippets
