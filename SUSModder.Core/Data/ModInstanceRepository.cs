using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using SUSModder.Core.Models;

namespace SUSModder.Core.Data
{
    /// <summary>
    /// SQLite CRUD dla lokalnych instancji modpacków i zależnych DLL/configów.
    /// </summary>
    public class ModInstanceRepository : IModInstanceRepository
    {
        private readonly DatabaseService _db;

        public ModInstanceRepository(DatabaseService db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public List<ModInstance> GetAllInstances()
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT instance_id, display_name, base_mod_id, base_mod_name, full_mod_version,
                       among_version, platform, install_path, origin, source_pack_code,
                       pinned_version, auto_update_enabled, notes, created_at, updated_at,
                       last_launched_at
                FROM mod_instances
                ORDER BY last_launched_at IS NULL, last_launched_at DESC, updated_at DESC;";

            var instances = new List<ModInstance>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                instances.Add(MapInstance(reader));
            }

            return instances;
        }

        public List<ModInstance> GetPackInstances()
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT instance_id, display_name, base_mod_id, base_mod_name, full_mod_version,
                       among_version, platform, install_path, origin, source_pack_code,
                       pinned_version, auto_update_enabled, notes, created_at, updated_at,
                       last_launched_at
                FROM mod_instances
                WHERE origin IN ('manual', 'shared_pack', 'clone')
                ORDER BY last_launched_at IS NULL, last_launched_at DESC, updated_at DESC;";

            var instances = new List<ModInstance>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                instances.Add(MapInstance(reader));
            }

            return instances;
        }

        public ModInstance? GetInstance(string instanceId)
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT instance_id, display_name, base_mod_id, base_mod_name, full_mod_version,
                       among_version, platform, install_path, origin, source_pack_code,
                       pinned_version, auto_update_enabled, notes, created_at, updated_at,
                       last_launched_at
                FROM mod_instances
                WHERE instance_id = @instance_id;";
            cmd.Parameters.AddWithValue("@instance_id", instanceId);

            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapInstance(reader) : null;
        }

        public List<ModInstance> GetInstancesForBaseMod(int baseModId)
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT instance_id, display_name, base_mod_id, base_mod_name, full_mod_version,
                       among_version, platform, install_path, origin, source_pack_code,
                       pinned_version, auto_update_enabled, notes, created_at, updated_at,
                       last_launched_at
                FROM mod_instances
                WHERE base_mod_id = @base_mod_id
                  AND origin IN ('manual', 'shared_pack', 'clone')
                ORDER BY display_name COLLATE NOCASE;";
            cmd.Parameters.AddWithValue("@base_mod_id", baseModId);

            var instances = new List<ModInstance>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                instances.Add(MapInstance(reader));
            }

            return instances;
        }

        public void AddInstance(ModInstance instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (string.IsNullOrWhiteSpace(instance.InstanceId))
                instance.InstanceId = Guid.NewGuid().ToString("D");

            var now = DateTime.UtcNow.ToString("O");
            if (string.IsNullOrWhiteSpace(instance.CreatedAt)) instance.CreatedAt = now;
            if (string.IsNullOrWhiteSpace(instance.UpdatedAt)) instance.UpdatedAt = now;

            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO mod_instances (
                    instance_id, display_name, base_mod_id, base_mod_name, full_mod_version,
                    among_version, platform, install_path, origin, source_pack_code, pinned_version,
                    auto_update_enabled, notes, created_at, updated_at, last_launched_at
                ) VALUES (
                    @instance_id, @display_name, @base_mod_id, @base_mod_name, @full_mod_version,
                    @among_version, @platform, @install_path, @origin, @source_pack_code, @pinned_version,
                    @auto_update_enabled, @notes, @created_at, @updated_at, @last_launched_at
                );";
            AddInstanceParameters(cmd, instance);
            cmd.ExecuteNonQuery();
        }

        public void UpdateInstance(ModInstance instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            instance.UpdatedAt = DateTime.UtcNow.ToString("O");

            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE mod_instances SET
                    display_name = @display_name,
                    base_mod_id = @base_mod_id,
                    base_mod_name = @base_mod_name,
                    full_mod_version = @full_mod_version,
                    among_version = @among_version,
                    platform = @platform,
                    install_path = @install_path,
                    origin = @origin,
                    source_pack_code = @source_pack_code,
                    pinned_version = @pinned_version,
                    auto_update_enabled = @auto_update_enabled,
                    notes = @notes,
                    updated_at = @updated_at,
                    last_launched_at = @last_launched_at
                WHERE instance_id = @instance_id;";
            AddInstanceParameters(cmd, instance);
            cmd.ExecuteNonQuery();
        }

        public void DeleteInstance(string instanceId)
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM mod_instances WHERE instance_id = @instance_id;";
            cmd.Parameters.AddWithValue("@instance_id", instanceId);
            cmd.ExecuteNonQuery();
        }

        public void RenameInstance(string instanceId, string displayName)
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE mod_instances
                SET display_name = @display_name, updated_at = @updated_at
                WHERE instance_id = @instance_id;";
            cmd.Parameters.AddWithValue("@display_name", displayName);
            cmd.Parameters.AddWithValue("@updated_at", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@instance_id", instanceId);
            cmd.ExecuteNonQuery();
        }

        public void UpdateLastLaunched(string instanceId)
        {
            var now = DateTime.UtcNow.ToString("O");
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE mod_instances
                SET last_launched_at = @now, updated_at = @now
                WHERE instance_id = @instance_id;";
            cmd.Parameters.AddWithValue("@now", now);
            cmd.Parameters.AddWithValue("@instance_id", instanceId);
            cmd.ExecuteNonQuery();
        }

        public List<ModInstanceDll> GetDlls(string instanceId)
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, instance_id, dll_mod_id, dll_name, dll_version, source, sha256,
                       vt_status, installed_path, created_at
                FROM mod_instance_dlls
                WHERE instance_id = @instance_id
                ORDER BY dll_name COLLATE NOCASE;";
            cmd.Parameters.AddWithValue("@instance_id", instanceId);

            var dlls = new List<ModInstanceDll>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                dlls.Add(MapDll(reader));
            }

            return dlls;
        }

        public long AddDll(ModInstanceDll dll)
        {
            if (dll == null) throw new ArgumentNullException(nameof(dll));
            if (string.IsNullOrWhiteSpace(dll.CreatedAt)) dll.CreatedAt = DateTime.UtcNow.ToString("O");

            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO mod_instance_dlls (
                    instance_id, dll_mod_id, dll_name, dll_version, source, sha256,
                    vt_status, installed_path, created_at
                ) VALUES (
                    @instance_id, @dll_mod_id, @dll_name, @dll_version, @source, @sha256,
                    @vt_status, @installed_path, @created_at
                );
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@instance_id", dll.InstanceId);
            cmd.Parameters.AddWithValue("@dll_mod_id", (object?)dll.DllModId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dll_name", dll.DllName ?? string.Empty);
            cmd.Parameters.AddWithValue("@dll_version", dll.DllVersion ?? string.Empty);
            cmd.Parameters.AddWithValue("@source", dll.Source ?? "catalog");
            cmd.Parameters.AddWithValue("@sha256", (object?)dll.Sha256 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@vt_status", dll.VtStatus ?? "unknown");
            cmd.Parameters.AddWithValue("@installed_path", dll.InstalledPath ?? string.Empty);
            cmd.Parameters.AddWithValue("@created_at", dll.CreatedAt);

            var id = (long)(cmd.ExecuteScalar() ?? 0L);
            dll.Id = id;
            return id;
        }

        public void RemoveDll(long id)
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM mod_instance_dlls WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public List<ModInstanceConfig> GetConfigs(string instanceId)
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, instance_id, config_type, config_name, config_json, created_at, updated_at
                FROM mod_instance_configs
                WHERE instance_id = @instance_id
                ORDER BY updated_at DESC;";
            cmd.Parameters.AddWithValue("@instance_id", instanceId);

            var configs = new List<ModInstanceConfig>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                configs.Add(MapConfig(reader));
            }

            return configs;
        }

        public long AddConfig(ModInstanceConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            var now = DateTime.UtcNow.ToString("O");
            if (string.IsNullOrWhiteSpace(config.CreatedAt)) config.CreatedAt = now;
            if (string.IsNullOrWhiteSpace(config.UpdatedAt)) config.UpdatedAt = now;

            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO mod_instance_configs (
                    instance_id, config_type, config_name, config_json, created_at, updated_at
                ) VALUES (
                    @instance_id, @config_type, @config_name, @config_json, @created_at, @updated_at
                );
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@instance_id", config.InstanceId);
            cmd.Parameters.AddWithValue("@config_type", config.ConfigType ?? string.Empty);
            cmd.Parameters.AddWithValue("@config_name", config.ConfigName ?? string.Empty);
            cmd.Parameters.AddWithValue("@config_json", config.ConfigJson ?? string.Empty);
            cmd.Parameters.AddWithValue("@created_at", config.CreatedAt);
            cmd.Parameters.AddWithValue("@updated_at", config.UpdatedAt);

            var id = (long)(cmd.ExecuteScalar() ?? 0L);
            config.Id = id;
            return id;
        }

        public void UpdateConfig(ModInstanceConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            config.UpdatedAt = DateTime.UtcNow.ToString("O");

            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE mod_instance_configs SET
                    config_type = @config_type,
                    config_name = @config_name,
                    config_json = @config_json,
                    updated_at = @updated_at
                WHERE id = @id;";
            cmd.Parameters.AddWithValue("@config_type", config.ConfigType ?? string.Empty);
            cmd.Parameters.AddWithValue("@config_name", config.ConfigName ?? string.Empty);
            cmd.Parameters.AddWithValue("@config_json", config.ConfigJson ?? string.Empty);
            cmd.Parameters.AddWithValue("@updated_at", config.UpdatedAt);
            cmd.Parameters.AddWithValue("@id", config.Id);
            cmd.ExecuteNonQuery();
        }

        public void DeleteConfig(long id)
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM mod_instance_configs WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        private static void AddInstanceParameters(SqliteCommand cmd, ModInstance instance)
        {
            cmd.Parameters.AddWithValue("@instance_id", instance.InstanceId);
            cmd.Parameters.AddWithValue("@display_name", instance.DisplayName ?? string.Empty);
            cmd.Parameters.AddWithValue("@base_mod_id", instance.BaseModId);
            cmd.Parameters.AddWithValue("@base_mod_name", instance.BaseModName ?? string.Empty);
            cmd.Parameters.AddWithValue("@full_mod_version", instance.FullModVersion ?? string.Empty);
            cmd.Parameters.AddWithValue("@among_version", instance.AmongVersion ?? string.Empty);
            cmd.Parameters.AddWithValue("@platform", instance.Platform ?? string.Empty);
            cmd.Parameters.AddWithValue("@install_path", instance.InstallPath ?? string.Empty);
            cmd.Parameters.AddWithValue("@origin", instance.Origin ?? "manual");
            cmd.Parameters.AddWithValue("@source_pack_code", (object?)instance.SourcePackCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pinned_version", (object?)instance.PinnedVersion ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@auto_update_enabled", instance.AutoUpdateEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@notes", instance.Notes ?? string.Empty);
            cmd.Parameters.AddWithValue("@created_at", instance.CreatedAt);
            cmd.Parameters.AddWithValue("@updated_at", instance.UpdatedAt);
            cmd.Parameters.AddWithValue("@last_launched_at", (object?)instance.LastLaunchedAt ?? DBNull.Value);
        }

        private static ModInstance MapInstance(SqliteDataReader reader)
        {
            return new ModInstance
            {
                InstanceId = reader.GetString(0),
                DisplayName = reader.GetString(1),
                BaseModId = reader.GetInt32(2),
                BaseModName = reader.GetString(3),
                FullModVersion = reader.GetString(4),
                AmongVersion = reader.GetString(5),
                Platform = reader.GetString(6),
                InstallPath = reader.GetString(7),
                Origin = reader.GetString(8),
                SourcePackCode = reader.IsDBNull(9) ? null : reader.GetString(9),
                PinnedVersion = reader.IsDBNull(10) ? null : reader.GetString(10),
                AutoUpdateEnabled = reader.GetInt32(11) != 0,
                Notes = reader.GetString(12),
                CreatedAt = reader.GetString(13),
                UpdatedAt = reader.GetString(14),
                LastLaunchedAt = reader.IsDBNull(15) ? null : reader.GetString(15)
            };
        }

        private static ModInstanceDll MapDll(SqliteDataReader reader)
        {
            return new ModInstanceDll
            {
                Id = reader.GetInt64(0),
                InstanceId = reader.GetString(1),
                DllModId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                DllName = reader.GetString(3),
                DllVersion = reader.GetString(4),
                Source = reader.GetString(5),
                Sha256 = reader.IsDBNull(6) ? null : reader.GetString(6),
                VtStatus = reader.GetString(7),
                InstalledPath = reader.GetString(8),
                CreatedAt = reader.GetString(9)
            };
        }

        private static ModInstanceConfig MapConfig(SqliteDataReader reader)
        {
            return new ModInstanceConfig
            {
                Id = reader.GetInt64(0),
                InstanceId = reader.GetString(1),
                ConfigType = reader.GetString(2),
                ConfigName = reader.GetString(3),
                ConfigJson = reader.GetString(4),
                CreatedAt = reader.GetString(5),
                UpdatedAt = reader.GetString(6)
            };
        }
    }
}
