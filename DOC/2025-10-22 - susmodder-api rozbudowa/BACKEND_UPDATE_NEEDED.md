# Backend API Update Required

## Problem
Backend zwraca natywny format Velopack (`Assets`), ale aplikacja oczekuje custom format z `Releases`.

## Obecny response (nieprawidłowy):
```json
{
  "manifest": {
    "Assets": [{
      "PackageId": "SUSModder",
      "Version": "2.1.0",
      ...
    }]
  }
}
```

## Wymagany response:
```json
{
  "success": true,
  "channel": "win",
  "arch": "x64",
  "latestVersion": "2.1.0",
  "updatedAt": "2025-11-04T00:15:20.000Z",
  "manifest": {
    "LatestVersion": "2.1.0",
    "Releases": [{
      "Version": "2.1.0",
      "File": "SUSModder-2.1.0-full.nupkg",
      "SHA256": "B8B5BD39B25DF2587FEBE8F57AD6B9002FCE3C5F7351AA00FB528E5EA6E7FC52",
      "Channel": "win",
      "Size": 59497030,
      "CreateTime": "2025-11-04T00:15:18Z"
    }],
    "downloadBaseUrl": "https://susmodder.app/releases"
  },
  "downloadBaseUrl": "https://susmodder.app/releases"
}
```

## Checksums z prawdziwych plików:

### SUSModder-2.1.0-full.nupkg
- **SHA256**: `B8B5BD39B25DF2587FEBE8F57AD6B9002FCE3C5F7351AA00FB528E5EA6E7FC52`
- SHA1: `DA129A777AFEB20FF696E363B1FB489ACE333130`
- Size: 59,497,030 bytes (56.74 MB)

## Transformacja potrzebna w backend:

```javascript
// Read releases.win.json from velopack-releases/
const releasesJson = await fs.readFile('releases/releases.win.json', 'utf-8');
const velopackData = JSON.parse(releasesJson);

// Transform Assets → Releases
const releases = velopackData.Assets.map(asset => ({
  Version: asset.Version,
  File: asset.FileName,
  SHA256: asset.SHA256,
  Channel: channel,
  Size: asset.Size,
  CreateTime: new Date().toISOString()
}));

// Build response
const response = {
  success: true,
  channel: channel,
  arch: 'x64',
  latestVersion: releases[0].Version,
  updatedAt: new Date().toISOString(),
  manifest: {
    LatestVersion: releases[0].Version,
    Releases: releases,
    downloadBaseUrl: 'https://susmodder.app/releases'
  },
  downloadBaseUrl: 'https://susmodder.app/releases'
};
```

## Co uploadować na serwer:

Z katalogu `velopack-releases/` upload:
1. ✅ **SUSModder-2.1.0-full.nupkg** (56.74 MB) - główny pakiet
2. ✅ **RELEASES** (0.08 KB) - checksums (Squirrel legacy)
3. ✅ **releases.win.json** (0.25 KB) - manifest Velopack
4. ⚠️ **SUSModder-win-Setup.exe** (59.22 MB) - installer dla nowych użytkowników (opcjonalnie)

## Test po aktualizacji:

```powershell
.\test-velopack-api.ps1
```

Powinno pokazać:
- ✅ `[OK] latestVersion = 2.1.0`
- ✅ `[OK] manifest.Releases exists`
- ✅ `SHA256: B8B5BD39B25DF2587FEBE8F57AD6B9002FCE3C5F7351AA00FB528E5EA6E7FC52`
