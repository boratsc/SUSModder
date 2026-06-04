using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
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
    /// Instalacja paczki modów: full mod, DLL katalogowe, external DLL, config ToU, integration.dll.
    /// Domyślnie tworzy nową lokalną instancję (InstallAsNewInstance).
    /// </summary>
    public sealed class ModPackInstaller
    {
        private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

        private readonly IConfiguration _configuration;
        private readonly ConfigService _configService;
        private readonly DllModificationService _dllService;
        private readonly ModInstanceInstaller? _instanceInstaller;
        private readonly IModInstanceRepository? _instanceRepository;
        private readonly IDiagnosticsOutput _log;
        private readonly string _baseUrl;
        private readonly string _modPacksEndpoint;

        public ModPackInstaller(
            IConfiguration configuration,
            ConfigService configService,
            DllModificationService dllService,
            IDiagnosticsOutput log,
            ModInstanceInstaller? instanceInstaller = null,
            IModInstanceRepository? instanceRepository = null)
        {
            _configuration = configuration;
            _configService = configService;
            _dllService = dllService;
            _log = log;
            _instanceInstaller = instanceInstaller;
            _instanceRepository = instanceRepository;
            _baseUrl = (_configuration["Configuration:BaseUrl"] ?? "https://susmodder.app/").TrimEnd('/');
            _modPacksEndpoint = _configuration["Configuration:ModPacksEndpoint"] ?? "/api/mod-packs";
        }

        public async Task<ModPackInstallResult> InstallPackAsync(
            ModPack pack,
            string platform,
            IProgress<(int percent, string message)>? progress = null,
            ModManagerUserCallbacks? modManagerCallbacks = null,
            string? displayName = null,
            CancellationToken ct = default)
        {
            if (_instanceInstaller != null)
                return await InstallPackAsNewInstanceAsync(
                    pack, platform, progress, modManagerCallbacks, displayName, ct);

            return await InstallPackLegacyAsync(pack, platform, progress, modManagerCallbacks, ct);
        }

        private async Task<ModPackInstallResult> InstallPackAsNewInstanceAsync(
            ModPack pack,
            string platform,
            IProgress<(int percent, string message)>? progress,
            ModManagerUserCallbacks? modManagerCallbacks,
            string? displayName,
            CancellationToken ct)
        {
            var result = new ModPackInstallResult();
            if (pack.FullMod == null)
            {
                result.ErrorMessage = "mod_pack_missing_full_mod";
                return result;
            }

            var allConfigs = _configService.LoadConfig();
            var fullModConfig = allConfigs.FirstOrDefault(c => c.Id == pack.FullMod.Id);
            if (fullModConfig == null)
            {
                result.ErrorMessage = "mod_pack_full_mod_not_in_catalog";
                result.FailedMods.Add(pack.ModName ?? $"mod#{pack.FullMod.Id}");
                return result;
            }

            var modToInstall = CloneForInstall(fullModConfig, pack.FullMod.Version);
            var instanceName = string.IsNullOrWhiteSpace(displayName)
                ? (pack.ModName ?? fullModConfig.ModName ?? "Zestaw")
                : displayName.Trim();

            try
            {
                progress?.Report((5, "Instalacja moda głównego..."));
                var progressReporter = new TupleProgressReporter(progress, 5, 45);
                var diag = new SimpleDiagnostics(_log);
                var callbacks = modManagerCallbacks ?? new ModManagerUserCallbacks();

                var instance = await _instanceInstaller!.InstallFullModInstanceAsync(
                    modToInstall,
                    instanceName,
                    platform,
                    progressReporter,
                    diag,
                    callbacks,
                    origin: "shared_pack",
                    sourcePackCode: pack.PackCode);

                result.InstanceId = instance.InstanceId;
                result.InstalledMods.Add(fullModConfig.ModName ?? "full mod");

                var dllProgressBase = 50;
                var dllCount = pack.DllMods.Count;
                var dllIndex = 0;
                foreach (var dllEntry in pack.DllMods)
                {
                    ct.ThrowIfCancellationRequested();
                    dllIndex++;
                    var dllMod = allConfigs.FirstOrDefault(c => c.Id == dllEntry.DllModId);
                    if (dllMod == null)
                    {
                        result.FailedMods.Add($"DLL#{dllEntry.DllModId}");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(dllEntry.DllModVersion) &&
                        !string.Equals(dllEntry.DllModVersion, "latest", StringComparison.OrdinalIgnoreCase))
                    {
                        dllMod = CloneForInstall(dllMod, dllEntry.DllModVersion);
                    }

                    var pct = dllProgressBase + (dllIndex * 25 / Math.Max(1, dllCount));
                    progress?.Report((pct, $"Instalacja {dllMod.ModName}..."));

                    try
                    {
                        await _instanceInstaller.InstallDllToInstanceAsync(dllMod, instance.InstanceId, platform, diag);
                        result.InstalledMods.Add(dllMod.ModName ?? $"DLL#{dllEntry.DllModId}");
                    }
                    catch (Exception ex)
                    {
                        _log.Write($"[ModPackInstaller] DLL install failed: {ex.Message}");
                        result.FailedMods.Add(dllMod.ModName ?? $"DLL#{dllEntry.DllModId}");
                    }
                }

                var targetMod = TargetModFromInstance(instance, fullModConfig);
                foreach (var ext in pack.ExternalDlls)
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.Equals(ext.VtStatus, "suspicious", StringComparison.OrdinalIgnoreCase))
                    {
                        result.FailedMods.Add(ext.FileName);
                        continue;
                    }

                    progress?.Report((78, $"Pobieranie {ext.FileName}..."));
                    var ok = await InstallExternalDllAsync(pack.PackCode, ext, targetMod);
                    if (ok)
                        result.InstalledMods.Add(ext.FileName);
                    else
                        result.FailedMods.Add(ext.FileName);
                }

                if (pack.TouConfig.HasValue && pack.TouConfig.Value.ValueKind != JsonValueKind.Undefined)
                {
                    progress?.Report((90, "Stosowanie configu ToU..."));
                    ModInstanceTouConfigService.ApplyJsonToGlobalFile(pack.TouConfig.Value);
                    if (!string.IsNullOrEmpty(result.InstanceId) && _instanceRepository != null)
                        ModInstanceTouConfigService.SaveSnapshot(_instanceRepository, result.InstanceId, pack.TouConfig.Value);
                    result.InstalledMods.Add("ToU config");
                }

                if (pack.IncludeIntegrationDll)
                {
                    progress?.Report((95, "Kopiowanie integration.dll..."));
                    if (TryCopyIntegrationDll(targetMod))
                        result.InstalledMods.Add("integration.dll");
                    else
                        result.SkippedMods.Add("integration.dll");
                }

                result.Success = result.FailedMods.Count == 0 || result.InstalledMods.Count > 0;
                progress?.Report((100, "Gotowe"));
                return result;
            }
            catch (Exception ex)
            {
                _log.Write($"[ModPackInstaller] {ex.Message}");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private async Task<ModPackInstallResult> InstallPackLegacyAsync(
            ModPack pack,
            string platform,
            IProgress<(int percent, string message)>? progress,
            ModManagerUserCallbacks? modManagerCallbacks,
            CancellationToken ct)
        {
            var result = new ModPackInstallResult();
            if (pack.FullMod == null)
            {
                result.ErrorMessage = "Brak moda głównego.";
                return result;
            }

            var allConfigs = _configService.LoadConfig();
            var fullModConfig = allConfigs.FirstOrDefault(c => c.Id == pack.FullMod.Id);
            if (fullModConfig == null)
            {
                result.ErrorMessage = "Mod główny nie znaleziony w katalogu.";
                result.FailedMods.Add(pack.ModName ?? $"mod#{pack.FullMod.Id}");
                return result;
            }

            try
            {
                progress?.Report((5, "Instalacja moda głównego..."));

                if (string.IsNullOrEmpty(fullModConfig.InstallPath))
                {
                    var modManager = new ModManager(_configuration);
                    var progressReporter = new SimpleProgressReporter(p => progress?.Report((5 + p / 2, "Pobieranie moda głównego...")));
                    var diag = new SimpleDiagnostics(_log);
                    var callbacks = modManagerCallbacks ?? new ModManagerUserCallbacks();

                    await modManager.ModifyAsync(
                        fullModConfig,
                        allConfigs,
                        progressReporter,
                        diag,
                        callbacks,
                        platform);
                    allConfigs = _configService.LoadConfig();
                    fullModConfig = allConfigs.FirstOrDefault(c => c.Id == pack.FullMod.Id) ?? fullModConfig;
                    result.InstalledMods.Add(fullModConfig.ModName ?? "full mod");
                }
                else
                {
                    result.SkippedMods.Add(fullModConfig.ModName ?? "full mod");
                }

                if (string.IsNullOrEmpty(fullModConfig.InstallPath))
                {
                    result.Success = false;
                    result.ErrorMessage = "Nie udało się zainstalować moda głównego.";
                    result.FailedMods.Add(fullModConfig.ModName ?? "full mod");
                    return result;
                }

                var dllProgressBase = 50;
                var dllCount = pack.DllMods.Count;
                var dllIndex = 0;
                foreach (var dllEntry in pack.DllMods)
                {
                    ct.ThrowIfCancellationRequested();
                    dllIndex++;
                    var dllMod = allConfigs.FirstOrDefault(c => c.Id == dllEntry.DllModId);
                    if (dllMod == null)
                    {
                        result.FailedMods.Add($"DLL#{dllEntry.DllModId}");
                        continue;
                    }

                    var pct = dllProgressBase + (dllIndex * 20 / Math.Max(1, dllCount));
                    progress?.Report((pct, $"Instalacja {dllMod.ModName}..."));

                    if (_dllService.IsDllInstalledInMod(dllMod, fullModConfig, platform))
                    {
                        result.SkippedMods.Add(dllMod.ModName ?? $"DLL#{dllEntry.DllModId}");
                        continue;
                    }

                    var installResult = await _dllService.InstallDllToModAsync(dllMod, fullModConfig, platform);
                    if (installResult != null)
                        result.InstalledMods.Add(dllMod.ModName ?? $"DLL#{dllEntry.DllModId}");
                    else
                        result.FailedMods.Add(dllMod.ModName ?? $"DLL#{dllEntry.DllModId}");
                }

                foreach (var ext in pack.ExternalDlls)
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.Equals(ext.VtStatus, "suspicious", StringComparison.OrdinalIgnoreCase))
                    {
                        result.FailedMods.Add(ext.FileName);
                        continue;
                    }

                    progress?.Report((75, $"Pobieranie {ext.FileName}..."));
                    var ok = await InstallExternalDllAsync(pack.PackCode, ext, fullModConfig);
                    if (ok)
                        result.InstalledMods.Add(ext.FileName);
                    else
                        result.FailedMods.Add(ext.FileName);
                }

                if (pack.TouConfig.HasValue && pack.TouConfig.Value.ValueKind != JsonValueKind.Undefined)
                {
                    progress?.Report((90, "Stosowanie configu ToU..."));
                    ModInstanceTouConfigService.ApplyJsonToGlobalFile(pack.TouConfig.Value);
                    result.InstalledMods.Add("ToU config");
                }

                if (pack.IncludeIntegrationDll)
                {
                    progress?.Report((95, "Kopiowanie integration.dll..."));
                    if (TryCopyIntegrationDll(fullModConfig))
                        result.InstalledMods.Add("integration.dll");
                    else
                        result.SkippedMods.Add("integration.dll");
                }

                result.Success = result.FailedMods.Count == 0 || result.InstalledMods.Count > 0;
                progress?.Report((100, "Gotowe"));
                return result;
            }
            catch (Exception ex)
            {
                _log.Write($"[ModPackInstaller] {ex.Message}");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private static ModConfiguration CloneForInstall(ModConfiguration source, string? version)
        {
            var clone = new ModConfiguration
            {
                Id = source.Id,
                ModName = source.ModName,
                ModType = source.ModType,
                ModVersion = source.ModVersion,
                AmongVersion = source.AmongVersion,
                GitHubRepoOrLink = source.GitHubRepoOrLink,
                EpicGitHubRepoOrLink = source.EpicGitHubRepoOrLink,
                Description = source.Description,
                PngFileName = source.PngFileName,
                DllInstallPath = source.DllInstallPath,
                InstallPath = source.InstallPath
            };

            if (!string.IsNullOrWhiteSpace(version) &&
                !string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase))
            {
                clone.ModVersion = version;
            }

            return clone;
        }

        private static ModConfiguration TargetModFromInstance(ModInstance instance, ModConfiguration catalogMod)
        {
            return new ModConfiguration
            {
                Id = catalogMod.Id,
                ModName = catalogMod.ModName,
                ModType = "full",
                ModVersion = instance.FullModVersion,
                AmongVersion = instance.AmongVersion,
                InstallPath = instance.InstallPath,
                GitHubRepoOrLink = catalogMod.GitHubRepoOrLink,
                EpicGitHubRepoOrLink = catalogMod.EpicGitHubRepoOrLink,
                PngFileName = catalogMod.PngFileName
            };
        }

        private async Task<bool> InstallExternalDllAsync(
            string packCode, ModPackExternalDll ext, ModConfiguration targetMod)
        {
            try
            {
                var downloadUrl = ext.DownloadUrl;
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    downloadUrl = $"{_baseUrl}{_modPacksEndpoint}/{packCode}/dlls/{ext.Sha256}";
                }

                var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    _log.Write($"[ModPackInstaller] External DLL download failed: {response.StatusCode}");
                    return false;
                }

                if (response.Content.Headers.ContentLength > 10 * 1024 * 1024)
                {
                    _log.Write($"[ModPackInstaller] External DLL too large: {response.Content.Headers.ContentLength} bytes");
                    return false;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync();
                var actualPath = PathSettings.GetActualModPath(targetMod.InstallPath!);
                var pluginsDir = Path.Combine(actualPath, "BepInEx", "plugins");

                if (!TryResolveSafeDllPath(pluginsDir, ext.FileName, out var safeDest))
                {
                    _log.Write($"[ModPackInstaller] Path traversal blocked: {ext.FileName}");
                    return false;
                }

                Directory.CreateDirectory(pluginsDir);
                await File.WriteAllBytesAsync(safeDest, bytes);
                _log.Write($"[ModPackInstaller] External DLL saved: {safeDest}");
                return true;
            }
            catch (Exception ex)
            {
                _log.Write($"[ModPackInstaller] External DLL error: {ex.Message}");
                return false;
            }
        }

        private static bool TryCopyIntegrationDll(ModConfiguration targetMod)
        {
            if (string.IsNullOrEmpty(targetMod.InstallPath))
                return false;

            var integrationCandidates = new[]
            {
                Path.Combine(targetMod.InstallPath, "BepInEx", "plugins", "integration.dll"),
                Path.Combine(PathSettings.ModsInstallPath, "integration.dll")
            };

            string? source = integrationCandidates.FirstOrDefault(File.Exists);
            if (source == null)
                return false;

            var actualPath = PathSettings.GetActualModPath(targetMod.InstallPath);
            var dest = Path.Combine(actualPath, "BepInEx", "plugins", "integration.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(source, dest, overwrite: true);
            return true;
        }

        internal static bool TryResolveSafeDllPath(string pluginsDir, string fileName, out string safePath)
        {
            safePath = string.Empty;

            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            var safeFileName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(safeFileName))
                return false;

            if (safeFileName is "." or "..")
                return false;

            var dest = Path.Combine(pluginsDir, safeFileName);
            var fullDest = Path.GetFullPath(dest);
            var fullPluginsDir = Path.GetFullPath(pluginsDir);

            if (!fullDest.StartsWith(fullPluginsDir, StringComparison.OrdinalIgnoreCase))
                return false;

            safePath = fullDest;
            return true;
        }

        private sealed class SimpleProgressReporter : IProgressReporter
        {
            private readonly Action<int> _onProgress;
            public SimpleProgressReporter(Action<int> onProgress) => _onProgress = onProgress;
            public void Report(int percent, string? message = null) => _onProgress(percent);
        }

        private sealed class TupleProgressReporter : IProgressReporter
        {
            private readonly IProgress<(int percent, string message)>? _progress;
            private readonly int _min;
            private readonly int _max;

            public TupleProgressReporter(IProgress<(int percent, string message)>? progress, int min, int max)
            {
                _progress = progress;
                _min = min;
                _max = max;
            }

            public void Report(int percent, string? message = null)
            {
                var mapped = _min + (percent * (_max - _min) / 100);
                _progress?.Report((mapped, message ?? string.Empty));
            }
        }

        private sealed class SimpleDiagnostics : IDiagnosticsOutput
        {
            private readonly IDiagnosticsOutput _inner;
            public SimpleDiagnostics(IDiagnosticsOutput inner) => _inner = inner;
            public void Write(string message) => _inner.Write($"[ModPackInstaller] {message}");
        }
    }
}
