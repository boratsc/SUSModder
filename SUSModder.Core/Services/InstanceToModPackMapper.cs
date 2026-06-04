using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using SUSModder.Core.Data;
using SUSModder.Core.Models;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Mapuje lokalną instancję modpacka na żądanie utworzenia udostępnionej paczki (API #16).
    /// </summary>
    public sealed class InstanceToModPackMapper
    {
        private readonly IModInstanceRepository _instances;

        public InstanceToModPackMapper(IModInstanceRepository instances)
        {
            _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        }

        public ModPackCreateRequest Map(
            string instanceId,
            string? packDisplayName = null,
            int ttlDays = 30,
            string? creatorName = null,
            string? discordInvite = null,
            bool? includeIntegrationDll = null)
        {
            var instance = _instances.GetInstance(instanceId)
                ?? throw new InvalidOperationException("mod_instance_not_found");

            var dllRows = _instances.GetDlls(instanceId);
            var dllMods = dllRows
                .Where(d => d.DllModId.HasValue)
                .Select(d => new ModPackDllModRequest
                {
                    DllModId = d.DllModId!.Value,
                    DllModVersion = string.IsNullOrWhiteSpace(d.DllVersion) ? "latest" : d.DllVersion
                })
                .ToList();

            var externalDlls = dllRows
                .Where(d => !d.DllModId.HasValue && string.Equals(d.Source, "external", StringComparison.OrdinalIgnoreCase))
                .Select(MapExternalDll)
                .Where(d => d != null)
                .Cast<ModPackExternalDllDeclaration>()
                .ToList();

            JsonElement? touConfig = TryReadTouConfig(instanceId);

            var includeIntegration = File.Exists(Path.Combine(
                PathSettings.GetActualModPath(instance.InstallPath),
                "BepInEx", "plugins", "integration.dll"));

            return new ModPackCreateRequest
            {
                CreatorName = string.IsNullOrWhiteSpace(creatorName) ? null : creatorName.Trim(),
                FullModId = instance.BaseModId,
                FullModVersion = string.IsNullOrWhiteSpace(instance.FullModVersion) ? "latest" : instance.FullModVersion,
                ModName = string.IsNullOrWhiteSpace(packDisplayName) ? instance.DisplayName : packDisplayName.Trim(),
                DiscordInvite = string.IsNullOrWhiteSpace(discordInvite) ? null : discordInvite.Trim(),
                IncludeIntegrationDll = includeIntegrationDll ?? includeIntegration,
                TtlDays = ttlDays,
                DllMods = dllMods,
                TouConfig = touConfig,
                ExternalDlls = externalDlls
            };
        }

        private ModPackExternalDllDeclaration? MapExternalDll(ModInstanceDll row)
        {
            if (string.IsNullOrWhiteSpace(row.InstalledPath))
                return null;

            var fullPath = Path.IsPathRooted(row.InstalledPath)
                ? row.InstalledPath
                : Path.Combine(
                    PathSettings.GetActualModPath(
                        _instances.GetInstance(row.InstanceId)?.InstallPath ?? string.Empty),
                    row.InstalledPath);

            if (!File.Exists(fullPath))
                return null;

            var fileName = Path.GetFileName(fullPath);
            var sha256 = string.IsNullOrWhiteSpace(row.Sha256)
                ? ComputeSha256(fullPath)
                : row.Sha256;
            var info = new FileInfo(fullPath);

            return new ModPackExternalDllDeclaration
            {
                FileName = fileName,
                FileSha256 = sha256,
                FileSize = info.Length
            };
        }

        private JsonElement? TryReadTouConfig(string instanceId)
        {
            var configs = _instances.GetConfigs(instanceId);
            var tou = configs.FirstOrDefault(c =>
                string.Equals(c.ConfigType, "tou", StringComparison.OrdinalIgnoreCase));
            if (tou == null || string.IsNullOrWhiteSpace(tou.ConfigJson))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(tou.ConfigJson);
                return doc.RootElement.Clone();
            }
            catch
            {
                return null;
            }
        }

        private static string ComputeSha256(string path)
        {
            using var stream = File.OpenRead(path);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
