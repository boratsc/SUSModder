using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SUSModder.Core.Data;
using SUSModder.Core.Models;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Snapshot configu ToU (settings.amogus_TOU) powiązany z lokalną instancją modpacka.
    /// </summary>
    public static class ModInstanceTouConfigService
    {
        public const string ConfigType = "tou";
        public const string DefaultConfigName = "settings.amogus_TOU";

        public static string GetGlobalTouSettingsPath()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @"AppData\LocalLow\Innersloth\Among Us");
            return Path.Combine(dir, DefaultConfigName);
        }

        public static void ApplyJsonToGlobalFile(string configJson)
        {
            if (string.IsNullOrWhiteSpace(configJson))
                throw new ArgumentException("mod_instance_tou_config_empty", nameof(configJson));

            var path = GetGlobalTouSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, configJson);
        }

        public static void ApplyJsonToGlobalFile(JsonElement config)
        {
            ApplyJsonToGlobalFile(config.GetRawText());
        }

        public static bool TryReadGlobalFile(out string? configJson)
        {
            configJson = null;
            var path = GetGlobalTouSettingsPath();
            if (!File.Exists(path))
                return false;

            configJson = File.ReadAllText(path);
            return !string.IsNullOrWhiteSpace(configJson);
        }

        public static void SaveSnapshot(IModInstanceRepository instances, string instanceId, string configJson, string? configName = null)
        {
            if (instances == null) throw new ArgumentNullException(nameof(instances));
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException("mod_instance_id_required", nameof(instanceId));
            if (string.IsNullOrWhiteSpace(configJson))
                throw new ArgumentException("mod_instance_tou_config_empty", nameof(configJson));

            if (instances.GetInstance(instanceId) == null)
                throw new InvalidOperationException("mod_instance_not_found");

            var now = DateTime.UtcNow.ToString("O");
            foreach (var existing in instances.GetConfigs(instanceId)
                         .Where(c => string.Equals(c.ConfigType, ConfigType, StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                instances.DeleteConfig(existing.Id);
            }

            instances.AddConfig(new ModInstanceConfig
            {
                InstanceId = instanceId,
                ConfigType = ConfigType,
                ConfigName = string.IsNullOrWhiteSpace(configName) ? DefaultConfigName : configName.Trim(),
                ConfigJson = configJson,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        public static void SaveSnapshot(IModInstanceRepository instances, string instanceId, JsonElement config, string? configName = null)
        {
            SaveSnapshot(instances, instanceId, config.GetRawText(), configName);
        }

        public static bool TryApplyInstanceConfigToGlobal(IModInstanceRepository instances, string instanceId)
        {
            var tou = instances.GetConfigs(instanceId)
                .FirstOrDefault(c => string.Equals(c.ConfigType, ConfigType, StringComparison.OrdinalIgnoreCase));

            if (tou == null || string.IsNullOrWhiteSpace(tou.ConfigJson))
                return false;

            ApplyJsonToGlobalFile(tou.ConfigJson);
            return true;
        }

        public static bool TryCaptureGlobalToInstance(IModInstanceRepository instances, string instanceId)
        {
            if (!TryReadGlobalFile(out var json) || json == null)
                return false;

            SaveSnapshot(instances, instanceId, json);
            return true;
        }
    }
}
