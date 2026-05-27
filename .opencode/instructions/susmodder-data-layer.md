# SUSModder Data Layer (SQLite) – Agent Reference

## Overview (v2.9.0+)

All runtime configuration data has been migrated from JSON files to SQLite. The old JSON files are renamed to `.bak` and no longer used by the application.

## Database

- **File**: `%APPDATA%/SUSModder/susmodder.db`
- **Engine**: `Microsoft.Data.Sqlite` 10.0.8 (embedded, no system dependencies)
- **Mode**: WAL (`PRAGMA journal_mode=WAL`), synchronous=NORMAL, busy_timeout=5000

## Tables

### `mods` (replaces `config.json`)
| Column | Type | Notes |
|--------|------|-------|
| Id | INTEGER PK | Mod ID from API |
| ModName | TEXT | e.g. "Town of Us" |
| ModType | TEXT | `full`, `dll`, `Vanilla` |
| InstallPath | TEXT NULL | Set when mod is installed |
| ModVersion | TEXT | Installed version |
| GitHubRepoOrLink | TEXT | Download URL |
| ... | | 14 columns total |

### `user_settings` (replaces `user-settings.json`)
Singleton table (`CHECK id=1`), 16 columns:
`mode`, `theme`, `language`, `telemetry_enabled`, `mods_install_path`, `update_channel`, `license_accepted`, `vanilla_install_path`, `minimize_to_tray`, `show_quick_launch_tray`, `tray_first_minimize_shown`, `settings_version`, etc.

### `tou_configs` (replaces `touConfigsBase.json`)
`id`, `hash`, `created_at` – for ToU lobby config history.

## Repository Pattern

```
SUSModder.Core/Data/
├── DatabaseService          ← singleton, manages SqliteConnection
├── IModRepository           ← interface
├── ModRepository            ← CRUD for mods, write-through cache, API fetch/merge
├── IUserSettingsRepository  ← interface
├── UserSettingsRepository   ← CRUD for user_settings (16 cols), memory cache
├── ITouConfigRepository     ← interface
└── TouConfigRepository      ← CRUD for tou_configs
```

## Access Patterns

### From Core code (static, anywhere):
```csharp
// ConfigManager facade (delegates to SQLite when available)
ConfigManager.LoadConfig()          → IModRepository.GetAllMods()
ConfigManager.SaveConfig(list)      → IModRepository.SaveAllMods()
ConfigManager.AddTouConfig(hash)    → ITouConfigRepository.AddConfig()
ConfigManager.GetTouConfigs()       → ITouConfigRepository.GetAllConfigs()
```

### From UI code (via DI):
```csharp
// Inject via constructor
public MyClass(IModRepository modRepo, IUserSettingsRepository userRepo) { ... }
```

### UserSettingsService (dual-mode):
```csharp
// With repository (preferred):
new UserSettingsService(userSettingsRepo)

// Without (falls back to JSON file):
new UserSettingsService()

// After startup, SetDefaultRepository ensures all instances use SQLite:
UserSettingsService.SetDefaultRepository(repo);
```

## Initialization Flow

```
Program.Main()
  → App.axaml.cs: ConfigureServices()
    → registers DatabaseService, IModRepository, IUserSettingsRepository, ITouConfigRepository
  → InitializeApplicationAsync()
    → DatabaseService.InitializeAsync()
      → if new DB: CREATE TABLES + MigrateFromJson() + CleanupJsonFiles()
      → if existing: ApplyMigrations() + EnsureDataMigratedIfEmpty()
    → ConfigManager.SetRepository(modRepo)
    → UserSettingsService.SetDefaultRepository(userRepo)
```

## Migration (one-shot)

1. `config.json` → `mods` table
2. `user-settings.json` → `user_settings` table
3. `touConfigsBase.json` → `tou_configs` table
4. JSON files renamed to `.bak`
5. `.sqlite-migrated` flag written

**Recovery**: If tables are empty after buggy first-run, `EnsureDataMigratedIfEmpty()` re-imports from JSON.

## What was NOT migrated (stays as files)

- `.susmodder-install.json` – per-mod installation map (intentional redundancy, survives DB loss)
- `pl.json` / `en.json` – static, bundled localization resources
- `version.json` – updated by Velopack
- `appsettings.json` – **strictly read-only** (API endpoints, default paths)

## Rules for agents

1. **NEVER** write to `appsettings.json` at runtime – it's read-only.
2. Use `ConfigManager` facade for mod operations (it delegates to SQLite automatically).
3. Use `UserSettingsService` (with default repo) for user preferences.
4. New tables → add migration in `DatabaseService.ApplyMigrations()` with `PRAGMA user_version`.
5. All SQL queries must be **parametrized** (`@param`). Column names must be whitelisted.
6. `ModConfiguration` implements `INotifyPropertyChanged` – don't break data binding.
7. `ModRepository` has write-through cache – don't bypass it for mod writes.
