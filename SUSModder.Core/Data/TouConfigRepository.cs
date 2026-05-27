using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using SUSModder.Core.Models;

namespace SUSModder.Core.Data
{
    /// <summary>
    /// Repozytorium dla zapisanych konfiguracji ToU (tabela tou_configs).
    /// Zastępuje touConfigsBase.json używający dynamic + Newtonsoft.Json.
    /// </summary>
    public class TouConfigRepository : ITouConfigRepository
    {
        private readonly DatabaseService _db;

        public TouConfigRepository(DatabaseService db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <inheritdoc/>
        public List<TouConfig> GetAllConfigs()
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, hash, created_at FROM tou_configs ORDER BY created_at DESC;";

            var configs = new List<TouConfig>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                configs.Add(new TouConfig
                {
                    Id = reader.GetInt32(0),
                    Hash = reader.GetString(1),
                    CreatedAt = reader.GetString(2)
                });
            }

            return configs;
        }

        /// <inheritdoc/>
        public void AddConfig(string hash)
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO tou_configs (hash, created_at) VALUES (@hash, datetime('now'));";
            cmd.Parameters.AddWithValue("@hash", hash);
            cmd.ExecuteNonQuery();
        }

        /// <inheritdoc/>
        public void ClearAll()
        {
            var conn = _db.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM tou_configs;";
            cmd.ExecuteNonQuery();
        }
    }
}
