using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Models;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Tworzy i utrzymuje lokalne instancje modpacków bez nadpisywania katalogowego InstallPath moda.
    /// </summary>
    public sealed class ModInstanceInstaller
    {
        private readonly IModInstanceRepository _instances;
        private readonly IFullModInstanceInstaller _fullModInstaller;
        private readonly IDllModInstanceInstaller? _dllInstaller;

        public ModInstanceInstaller(
            IConfiguration configuration,
            IModInstanceRepository instances,
            DllModificationService? dllService = null)
            : this(
                instances,
                new ModManagerFullModInstanceInstaller(configuration),
                dllService == null ? null : new DllModificationServiceInstanceInstaller(dllService))
        {
        }

        public ModInstanceInstaller(
            IModInstanceRepository instances,
            IFullModInstanceInstaller fullModInstaller,
            IDllModInstanceInstaller? dllInstaller = null)
        {
            _instances = instances ?? throw new ArgumentNullException(nameof(instances));
            _fullModInstaller = fullModInstaller ?? throw new ArgumentNullException(nameof(fullModInstaller));
            _dllInstaller = dllInstaller;
        }

        public async Task<ModInstance> InstallFullModInstanceAsync(
            ModConfiguration fullMod,
            string displayName,
            string platform,
            IProgressReporter? progress = null,
            IDiagnosticsOutput? log = null,
            ModManagerUserCallbacks? userCallbacks = null,
            string origin = "manual",
            string? sourcePackCode = null,
            string? requestedInstallPath = null,
            string? notes = null,
            Action<string>? onSpeedUpdate = null)
        {
            if (fullMod == null) throw new ArgumentNullException(nameof(fullMod));
            if (!string.Equals(fullMod.ModType, "full", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("mod_instance_requires_full_mod");
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("mod_instance_display_name_required", nameof(displayName));
            if (string.IsNullOrWhiteSpace(platform))
                throw new ArgumentException("mod_instance_platform_required", nameof(platform));

            progress ??= NoOpProgressReporter.Instance;
            log ??= NoOpDiagnosticsOutput.Instance;
            userCallbacks ??= new ModManagerUserCallbacks();

            var installPath = ResolveInstallPath(displayName, requestedInstallPath);
            EnsurePathCanBeUsedForNewInstance(installPath);

            log.Write($"[ModInstanceInstaller] Instaluję instancję '{displayName}' do: {installPath}");
            var installResult = await _fullModInstaller.InstallAsync(
                fullMod,
                installPath,
                platform,
                progress,
                log,
                userCallbacks,
                onSpeedUpdate);

            if (!installResult.Success)
            {
                throw new InvalidOperationException(
                    installResult.ErrorMessage ?? "mod_instance_install_failed");
            }

            var now = DateTime.UtcNow.ToString("O");
            var instance = new ModInstance
            {
                InstanceId = Guid.NewGuid().ToString("D"),
                DisplayName = displayName.Trim(),
                BaseModId = fullMod.Id,
                BaseModName = fullMod.ModName ?? string.Empty,
                FullModVersion = fullMod.ModVersion ?? string.Empty,
                AmongVersion = fullMod.AmongVersion ?? string.Empty,
                Platform = platform.Trim().ToLowerInvariant(),
                InstallPath = installPath,
                Origin = string.IsNullOrWhiteSpace(origin) ? "manual" : origin.Trim(),
                SourcePackCode = string.IsNullOrWhiteSpace(sourcePackCode) ? null : sourcePackCode.Trim(),
                PinnedVersion = null,
                AutoUpdateEnabled = false,
                Notes = notes ?? string.Empty,
                CreatedAt = now,
                UpdatedAt = now
            };

            _instances.AddInstance(instance);
            await SaveInstallationMapV2Async(instance, fullMod, log);

            log.Write($"[ModInstanceInstaller] Utworzono instancję {instance.InstanceId} ({instance.DisplayName}).");
            return instance;
        }

        public async Task<ModInstanceDll> InstallDllToInstanceAsync(
            ModConfiguration dllMod,
            string instanceId,
            string platform,
            IDiagnosticsOutput? log = null)
        {
            if (_dllInstaller == null)
                throw new InvalidOperationException("mod_instance_dll_service_required");
            if (dllMod == null) throw new ArgumentNullException(nameof(dllMod));
            if (!string.Equals(dllMod.ModType, "dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("mod_instance_requires_dll_mod");

            log ??= NoOpDiagnosticsOutput.Instance;
            var instance = _instances.GetInstance(instanceId)
                ?? throw new InvalidOperationException("mod_instance_not_found");

            var target = new ModConfiguration
            {
                Id = instance.BaseModId,
                ModName = instance.BaseModName,
                ModType = "full",
                ModVersion = instance.FullModVersion,
                AmongVersion = instance.AmongVersion,
                InstallPath = instance.InstallPath
            };

            var installedPath = await _dllInstaller.InstallAsync(dllMod, target, platform);
            if (string.IsNullOrWhiteSpace(installedPath))
                throw new InvalidOperationException("mod_instance_dll_install_failed");

            foreach (var existing in _instances.GetDlls(instance.InstanceId)
                         .Where(d => d.DllModId == dllMod.Id)
                         .ToList())
            {
                _instances.RemoveDll(existing.Id);
            }

            var relativePath = TryGetRelativePath(instance.InstallPath, installedPath);
            var row = new ModInstanceDll
            {
                InstanceId = instance.InstanceId,
                DllModId = dllMod.Id,
                DllName = dllMod.ModName ?? string.Empty,
                DllVersion = dllMod.ModVersion ?? string.Empty,
                Source = "catalog",
                InstalledPath = relativePath,
                CreatedAt = DateTime.UtcNow.ToString("O")
            };
            _instances.AddDll(row);

            log.Write($"[ModInstanceInstaller] DLL {row.DllName} przypisany do instancji {instance.InstanceId}.");
            return row;
        }

        public async Task RenameInstanceAsync(string instanceId, string displayName, IDiagnosticsOutput? log = null)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("mod_instance_display_name_required", nameof(displayName));

            log ??= NoOpDiagnosticsOutput.Instance;
            var instance = _instances.GetInstance(instanceId)
                ?? throw new InvalidOperationException("mod_instance_not_found");

            _instances.RenameInstance(instanceId, displayName.Trim());
            instance.DisplayName = displayName.Trim();
            await UpdateInstallationMapMetadataAsync(instance, log);
        }

        public async Task DeleteInstanceAsync(string instanceId, bool deleteFiles, IDiagnosticsOutput? log = null)
        {
            log ??= NoOpDiagnosticsOutput.Instance;
            var instance = _instances.GetInstance(instanceId)
                ?? throw new InvalidOperationException("mod_instance_not_found");

            if (deleteFiles && Directory.Exists(instance.InstallPath))
            {
                var map = await InstallationMapManager.LoadInstallationMapAsync(instance.InstallPath);
                if (!string.Equals(map?.InstanceId, instance.InstanceId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("mod_instance_delete_map_mismatch");
                }

                Directory.Delete(instance.InstallPath, recursive: true);
                log.Write($"[ModInstanceInstaller] Usunięto folder instancji: {instance.InstallPath}");
            }

            _instances.DeleteInstance(instanceId);
        }

        public void MarkInstanceLaunched(string instanceId)
        {
            if (_instances.GetInstance(instanceId) == null)
                throw new InvalidOperationException("mod_instance_not_found");

            _instances.UpdateLastLaunched(instanceId);
        }

        public async Task UpdateInstanceAsync(
            string instanceId,
            ModConfiguration updatedFullMod,
            IReadOnlyList<ModConfiguration> catalogMods,
            string platform,
            IProgressReporter? progress = null,
            IDiagnosticsOutput? log = null,
            ModManagerUserCallbacks? userCallbacks = null,
            Action<string>? onSpeedUpdate = null)
        {
            if (updatedFullMod == null)
                throw new ArgumentNullException(nameof(updatedFullMod));
            if (!string.Equals(updatedFullMod.ModType, "full", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("mod_instance_requires_full_mod");
            if (string.IsNullOrWhiteSpace(platform))
                throw new ArgumentException("mod_instance_platform_required", nameof(platform));

            progress ??= NoOpProgressReporter.Instance;
            log ??= NoOpDiagnosticsOutput.Instance;
            userCallbacks ??= new ModManagerUserCallbacks();

            var instance = _instances.GetInstance(instanceId)
                ?? throw new InvalidOperationException("mod_instance_not_found");

            var installPath = instance.InstallPath;
            if (string.IsNullOrWhiteSpace(installPath))
                throw new InvalidOperationException("mod_instance_missing_install_path");

            var dllRows = _instances.GetDlls(instanceId).ToList();
            var dllAutoUpdateFlags = await CaptureDllAutoUpdateFlagsAsync(instance);
            var preserveIntegration = IntegrationDllExists(installPath);

            log.Write($"[ModInstanceInstaller] Aktualizuję instancję '{instance.DisplayName}' ({instance.FullModVersion} → {updatedFullMod.ModVersion})");

            progress.Report(5, "preparing");
            if (Directory.Exists(installPath))
                Directory.Delete(installPath, recursive: true);
            Directory.CreateDirectory(installPath);

            progress.Report(15, "full_mod");
            await _fullModInstaller.InstallAsync(
                updatedFullMod,
                installPath,
                platform,
                progress,
                log,
                userCallbacks,
                onSpeedUpdate);

            instance.FullModVersion = updatedFullMod.ModVersion ?? instance.FullModVersion;
            instance.AmongVersion = updatedFullMod.AmongVersion ?? instance.AmongVersion;
            instance.UpdatedAt = DateTime.UtcNow.ToString("O");
            _instances.UpdateInstance(instance);

            await SaveInstallationMapV2Async(instance, updatedFullMod, log);

            if (preserveIntegration)
                TryRestoreIntegrationDll(installPath, log);

            if (_dllInstaller != null && dllRows.Count > 0)
            {
                var dllIndex = 0;
                foreach (var row in dllRows)
                {
                    dllIndex++;
                    var dllMod = catalogMods.FirstOrDefault(c => c.Id == row.DllModId);
                    if (dllMod == null)
                    {
                        log.Write($"[ModInstanceInstaller] Pominięto DLL#{row.DllModId} — brak w katalogu.");
                        continue;
                    }

                    progress.Report(70 + (dllIndex * 25 / Math.Max(1, dllRows.Count)), row.DllName);
                    try
                    {
                        await InstallDllToInstanceAsync(dllMod, instanceId, platform, log);
                        if (row.DllModId.HasValue && dllAutoUpdateFlags.TryGetValue(row.DllModId.Value, out var autoUpdateEnabled))
                            await ApplyDllAutoUpdateFlagAsync(instance.InstallPath, row.DllModId.Value, autoUpdateEnabled);
                    }
                    catch (Exception ex)
                    {
                        log.Write($"[ModInstanceInstaller] Nie udało się przywrócić DLL {row.DllName}: {ex.Message}");
                    }
                }
            }

            progress.Report(100, "done");
            log.Write($"[ModInstanceInstaller] Zaktualizowano instancję {instance.InstanceId} do {instance.FullModVersion}.");
        }

        private static async Task<Dictionary<int, bool>> CaptureDllAutoUpdateFlagsAsync(ModInstance instance)
        {
            var map = await InstallationMapManager.LoadInstallationMapAsync(instance.InstallPath);
            if (map == null)
                return new Dictionary<int, bool>();

            if (!string.IsNullOrWhiteSpace(map.InstanceId) &&
                !string.Equals(map.InstanceId, instance.InstanceId, StringComparison.OrdinalIgnoreCase))
            {
                return new Dictionary<int, bool>();
            }

            return map.InstalledDlls
                .GroupBy(dll => dll.ModId)
                .ToDictionary(group => group.Key, group => group.First().AutoUpdateEnabled);
        }

        private static async Task ApplyDllAutoUpdateFlagAsync(string installPath, int dllModId, bool autoUpdateEnabled)
        {
            var map = await InstallationMapManager.LoadInstallationMapAsync(installPath);
            var dll = map?.InstalledDlls.FirstOrDefault(d => d.ModId == dllModId);
            if (map == null || dll == null)
                return;

            dll.AutoUpdateEnabled = autoUpdateEnabled;
            await InstallationMapManager.SaveInstallationMapAsync(installPath, map);
        }

        public async Task<ModInstance> CloneInstanceAsync(
            string sourceInstanceId,
            ModInstanceCloneOptions options,
            IProgressReporter? progress = null,
            IDiagnosticsOutput? log = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.NewDisplayName))
                throw new ArgumentException("mod_instance_display_name_required", nameof(options));

            log ??= NoOpDiagnosticsOutput.Instance;
            progress ??= NoOpProgressReporter.Instance;

            var source = _instances.GetInstance(sourceInstanceId)
                ?? throw new InvalidOperationException("mod_instance_not_found");

            if (!Directory.Exists(source.InstallPath))
                throw new DirectoryNotFoundException("mod_instance_source_missing");

            var sourceMap = await InstallationMapManager.LoadInstallationMapAsync(source.InstallPath);
            if (sourceMap != null &&
                !string.IsNullOrWhiteSpace(sourceMap.InstanceId) &&
                !string.Equals(sourceMap.InstanceId, source.InstanceId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("mod_instance_clone_map_mismatch");
            }

            var newDisplayName = options.NewDisplayName.Trim();
            var targetPath = ResolveInstallPath(newDisplayName, requestedInstallPath: null);
            EnsurePathCanBeUsedForNewInstance(targetPath);

            log.Write($"[ModInstanceInstaller] Klonuję '{source.DisplayName}' → '{newDisplayName}' ({targetPath})");
            progress.Report(10, "copy");
            CopyDirectoryRecursive(source.InstallPath, targetPath);

            if (!options.CopyIntegrationDll)
                TryDeleteIntegrationDll(targetPath);

            var sourceDllRows = _instances.GetDlls(sourceInstanceId);
            if (!options.CopyDlls)
            {
                foreach (var dll in sourceDllRows)
                    TryDeleteDllFile(targetPath, dll.InstalledPath);
            }

            var now = DateTime.UtcNow.ToString("O");
            var clone = new ModInstance
            {
                InstanceId = Guid.NewGuid().ToString("D"),
                DisplayName = newDisplayName,
                BaseModId = source.BaseModId,
                BaseModName = source.BaseModName,
                FullModVersion = source.FullModVersion,
                AmongVersion = source.AmongVersion,
                Platform = source.Platform,
                InstallPath = targetPath,
                Origin = "clone",
                SourcePackCode = source.SourcePackCode,
                PinnedVersion = options.CopyPinnedVersion ? source.PinnedVersion : null,
                AutoUpdateEnabled = options.CopyPinnedVersion && source.AutoUpdateEnabled,
                Notes = source.Notes,
                CreatedAt = now,
                UpdatedAt = now
            };

            _instances.AddInstance(clone);

            if (options.CopyDlls)
            {
                foreach (var dll in sourceDllRows)
                {
                    _instances.AddDll(new ModInstanceDll
                    {
                        InstanceId = clone.InstanceId,
                        DllModId = dll.DllModId,
                        DllName = dll.DllName,
                        DllVersion = dll.DllVersion,
                        Source = dll.Source,
                        Sha256 = dll.Sha256,
                        VtStatus = dll.VtStatus,
                        InstalledPath = dll.InstalledPath,
                        CreatedAt = now
                    });
                }
            }

            if (options.CopyTouConfig)
            {
                foreach (var cfg in _instances.GetConfigs(sourceInstanceId))
                {
                    _instances.AddConfig(new ModInstanceConfig
                    {
                        InstanceId = clone.InstanceId,
                        ConfigType = cfg.ConfigType,
                        ConfigName = cfg.ConfigName,
                        ConfigJson = cfg.ConfigJson,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
            }

            progress.Report(90, "map");
            await SaveClonedInstallationMapAsync(source, clone, options, sourceMap, log);

            log.Write($"[ModInstanceInstaller] Sklonowano instancję {clone.InstanceId} ({clone.DisplayName}).");
            progress.Report(100, "done");
            return clone;
        }

        private static async Task SaveClonedInstallationMapAsync(
            ModInstance source,
            ModInstance clone,
            ModInstanceCloneOptions options,
            InstallationMap? sourceMap,
            IDiagnosticsOutput log)
        {
            var map = await InstallationMapManager.LoadInstallationMapAsync(clone.InstallPath) ?? new InstallationMap();
            map.Version = "2.0";
            map.InstanceId = clone.InstanceId;
            map.DisplayName = clone.DisplayName;
            map.Origin = "clone";
            map.SourcePackCode = clone.SourcePackCode;
            map.Platform = clone.Platform;
            map.InstalledAt = DateTime.Now;
            map.InstalledBy = string.IsNullOrWhiteSpace(map.InstalledBy) ? "SUSModder" : map.InstalledBy;

            if (sourceMap?.FullMod != null)
            {
                map.FullMod = new FullModInstallation
                {
                    ModId = sourceMap.FullMod.ModId,
                    ModName = sourceMap.FullMod.ModName,
                    ModVersion = sourceMap.FullMod.ModVersion,
                    AmongVersion = sourceMap.FullMod.AmongVersion,
                    InstallPath = clone.InstallPath,
                    InstalledFrom = sourceMap.FullMod.InstalledFrom,
                    LastUpdated = DateTime.Now,
                    AutoUpdateEnabled = clone.AutoUpdateEnabled,
                    PinnedInstallVersion = clone.PinnedVersion
                };
            }

            map.Metadata ??= new InstallationMetadata();
            map.Metadata.Notes = clone.Notes;

            if (!options.CopyDlls)
                map.InstalledDlls = new List<DllModInstallation>();
            else if (sourceMap?.InstalledDlls != null)
                map.InstalledDlls = sourceMap.InstalledDlls.Select(d => new DllModInstallation
                {
                    ModId = d.ModId,
                    ModName = d.ModName,
                    ModVersion = d.ModVersion,
                    InstallPath = d.InstallPath,
                    InstalledFrom = d.InstalledFrom,
                    InstalledAt = d.InstalledAt,
                    LastUpdated = DateTime.Now
                }).ToList();

            await InstallationMapManager.SaveInstallationMapAsync(clone.InstallPath, map);
            log.Write($"[ModInstanceInstaller] Zapisano mapę sklonowanej instancji: {clone.InstallPath}");
        }

        private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
        {
            var sourceFull = Path.GetFullPath(sourceDir);
            var targetFull = Path.GetFullPath(targetDir);
            Directory.CreateDirectory(targetFull);

            foreach (var dir in Directory.GetDirectories(sourceFull, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceFull, dir);
                Directory.CreateDirectory(Path.Combine(targetFull, relative));
            }

            foreach (var file in Directory.GetFiles(sourceFull, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceFull, file);
                var dest = Path.Combine(targetFull, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: true);
            }
        }

        private static void TryDeleteIntegrationDll(string installPath)
        {
            var actual = PathSettings.GetActualModPath(installPath);
            var path = Path.Combine(actual, "BepInEx", "plugins", "integration.dll");
            if (File.Exists(path))
                File.Delete(path);
        }

        private static void TryDeleteDllFile(string installPath, string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return;

            var fullPath = Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.Combine(PathSettings.GetActualModPath(installPath), relativePath);

            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }

        private static string ResolveInstallPath(string displayName, string? requestedInstallPath)
        {
            if (!string.IsNullOrWhiteSpace(requestedInstallPath))
                return Path.GetFullPath(requestedInstallPath);

            var root = PathSettings.ModsInstallPath;
            Directory.CreateDirectory(root);

            var safeName = SanitizeFolderName(displayName);
            var candidate = Path.Combine(root, safeName);
            if (!Directory.Exists(candidate))
                return candidate;

            for (var i = 2; i < 1000; i++)
            {
                candidate = Path.Combine(root, $"{safeName} ({i})");
                if (!Directory.Exists(candidate))
                    return candidate;
            }

            return Path.Combine(root, $"{safeName}-{Guid.NewGuid():N}");
        }

        private static string SanitizeFolderName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.Trim()
                .Select(ch => invalid.Contains(ch) ? '_' : ch)
                .ToArray();
            var sanitized = new string(chars).Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? "Modpack" : sanitized;
        }

        private static void EnsurePathCanBeUsedForNewInstance(string installPath)
        {
            if (Directory.Exists(installPath) && Directory.EnumerateFileSystemEntries(installPath).Any())
                throw new IOException("mod_instance_target_not_empty");

            if (File.Exists(installPath))
                throw new IOException("mod_instance_target_is_file");

            var parent = Path.GetDirectoryName(installPath);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);
        }

        private static async Task SaveInstallationMapV2Async(
            ModInstance instance,
            ModConfiguration fullMod,
            IDiagnosticsOutput log)
        {
            var map = await InstallationMapManager.LoadInstallationMapAsync(instance.InstallPath) ?? new InstallationMap();
            map.Version = "2.0";
            map.InstanceId = instance.InstanceId;
            map.DisplayName = instance.DisplayName;
            map.Origin = instance.Origin;
            map.SourcePackCode = instance.SourcePackCode;
            map.Platform = instance.Platform;
            map.InstalledAt = map.InstalledAt == default ? DateTime.Now : map.InstalledAt;
            map.InstalledBy = string.IsNullOrWhiteSpace(map.InstalledBy) ? "SUSModder" : map.InstalledBy;
            map.FullMod = new FullModInstallation
            {
                ModId = instance.BaseModId,
                ModName = instance.BaseModName,
                ModVersion = instance.FullModVersion,
                AmongVersion = instance.AmongVersion,
                InstallPath = instance.InstallPath,
                InstalledFrom = ModDownloadUrlBuilder.Build(fullMod, instance.Platform),
                LastUpdated = DateTime.Now,
                AutoUpdateEnabled = instance.AutoUpdateEnabled,
                PinnedInstallVersion = instance.PinnedVersion
            };
            map.Metadata ??= new InstallationMetadata();
            map.Metadata.Notes = instance.Notes;

            await InstallationMapManager.SaveInstallationMapAsync(instance.InstallPath, map);
            log.Write($"[ModInstanceInstaller] Zapisano Installation Map v2: {instance.InstallPath}");
        }

        private static async Task UpdateInstallationMapMetadataAsync(ModInstance instance, IDiagnosticsOutput log)
        {
            var map = await InstallationMapManager.LoadInstallationMapAsync(instance.InstallPath);
            if (map == null)
            {
                log.Write($"[ModInstanceInstaller] Brak Installation Map dla instancji {instance.InstanceId}.");
                return;
            }

            map.Version = "2.0";
            map.InstanceId = instance.InstanceId;
            map.DisplayName = instance.DisplayName;
            map.Origin = instance.Origin;
            map.SourcePackCode = instance.SourcePackCode;
            await InstallationMapManager.SaveInstallationMapAsync(instance.InstallPath, map);
        }

        private static bool IntegrationDllExists(string installPath)
        {
            var actualPath = PathSettings.GetActualModPath(installPath);
            var path = Path.Combine(actualPath, "BepInEx", "plugins", "integration.dll");
            return File.Exists(path);
        }

        private static void TryRestoreIntegrationDll(string installPath, IDiagnosticsOutput log)
        {
            var integrationCandidates = new[]
            {
                Path.Combine(installPath, "BepInEx", "plugins", "integration.dll"),
                Path.Combine(PathSettings.ModsInstallPath, "integration.dll")
            };

            var source = integrationCandidates.FirstOrDefault(File.Exists);
            if (source == null)
                return;

            try
            {
                var actualPath = PathSettings.GetActualModPath(installPath);
                var dest = Path.Combine(actualPath, "BepInEx", "plugins", "integration.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(source, dest, overwrite: true);
                log.Write("[ModInstanceInstaller] Przywrócono integration.dll po aktualizacji.");
            }
            catch (Exception ex)
            {
                log.Write($"[ModInstanceInstaller] integration.dll: {ex.Message}");
            }
        }

        private static string TryGetRelativePath(string root, string path)
        {
            try
            {
                return Path.GetRelativePath(root, path);
            }
            catch
            {
                return path;
            }
        }

        private sealed class NoOpProgressReporter : IProgressReporter
        {
            public static readonly NoOpProgressReporter Instance = new();
            public void Report(int percent, string? message = null) { }
        }

        private sealed class NoOpDiagnosticsOutput : IDiagnosticsOutput
        {
            public static readonly NoOpDiagnosticsOutput Instance = new();
            public void Write(string line) { }
        }
    }
}
