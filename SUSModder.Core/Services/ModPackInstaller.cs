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
        private readonly string _apiV2BaseUrl;

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
            _apiV2BaseUrl = (_configuration["Configuration:ApiV2BaseUrl"] ?? "https://api.susmodder-cdn.ovh/v2").TrimEnd('/');
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

        /// <summary>
        /// Aktualizuje istniejącą instancję zgodnie z manifestem zdalnej paczki (wersje z pack, nie latest z katalogu).
        /// </summary>
        public async Task<ModPackInstallResult> UpdateExistingInstanceAsync(
            string instanceId,
            ModPack pack,
            string platform,
            IProgress<(int percent, string message)>? progress = null,
            ModManagerUserCallbacks? modManagerCallbacks = null,
            CancellationToken ct = default)
        {
            var result = new ModPackInstallResult();

            if (_instanceInstaller == null || _instanceRepository == null)
            {
                result.ErrorMessage = "mod_instance_installer_unavailable";
                return result;
            }

            var instance = _instanceRepository.GetInstance(instanceId);
            if (instance == null)
            {
                result.ErrorMessage = "mod_instance_not_found";
                return result;
            }

            if (!TryResolveFullModConfig(pack, out var fullModConfig, out var resolveError) || fullModConfig == null)
            {
                result.ErrorMessage = resolveError ?? "mod_pack_missing_full_mod";
                return result;
            }

            var catalog = _configService.LoadConfig();
            var diag = new SimpleDiagnostics(_log);
            var callbacks = modManagerCallbacks ?? new ModManagerUserCallbacks();
            result.InstanceId = instanceId;

            try
            {
                var remoteFullVersion = pack.HasCustomFullMod
                    ? pack.CustomFullMod!.Version ?? string.Empty
                    : pack.FullMod?.Version ?? string.Empty;
                var localFullVersion = instance.FullModVersion ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(remoteFullVersion) &&
                    !string.Equals(remoteFullVersion, localFullVersion, StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report((10, "full_mod"));
                    var progressReporter = new TupleProgressReporter(progress, 10, 50);
                    await _instanceInstaller.UpdateInstanceAsync(
                        instanceId,
                        fullModConfig,
                        catalog,
                        platform,
                        progressReporter,
                        diag,
                        callbacks);
                    result.InstalledMods.Add(fullModConfig.ModName ?? "full mod");
                }

                instance = _instanceRepository.GetInstance(instanceId)!;
                var localDlls = _instanceRepository.GetDlls(instanceId).ToList();

                var dllIndex = 0;
                var dllCount = pack.DllMods.Count;
                foreach (var dllEntry in pack.DllMods)
                {
                    ct.ThrowIfCancellationRequested();
                    dllIndex++;

                    var localDll = localDlls.FirstOrDefault(d => d.DllModId == dllEntry.DllModId);
                    var localVersion = localDll?.DllVersion ?? string.Empty;
                    var remoteVersion = dllEntry.DllModVersion ?? string.Empty;

                    if (string.Equals(localVersion, remoteVersion, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var dllMod = catalog.FirstOrDefault(c => c.Id == dllEntry.DllModId);
                    if (dllMod == null)
                    {
                        result.FailedMods.Add($"DLL#{dllEntry.DllModId}");
                        continue;
                    }

                    var pinnedDll = CloneForInstall(dllMod, dllEntry.DllModVersion);
                    var pct = 55 + (dllIndex * 20 / Math.Max(1, dllCount));
                    progress?.Report((pct, dllMod.ModName ?? $"DLL#{dllEntry.DllModId}"));

                    try
                    {
                        await _instanceInstaller.InstallDllToInstanceAsync(pinnedDll, instanceId, platform, diag);
                        result.InstalledMods.Add(dllMod.ModName ?? $"DLL#{dllEntry.DllModId}");
                    }
                    catch (Exception ex)
                    {
                        _log.Write($"[ModPackInstaller] DLL update failed: {ex.Message}");
                        result.FailedMods.Add(dllMod.ModName ?? $"DLL#{dllEntry.DllModId}");
                    }
                }

                var targetMod = TargetModFromInstance(instance, fullModConfig);
                foreach (var ext in EnumerateExternalDlls(pack))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!string.Equals(ext.VtStatus, "clean", StringComparison.OrdinalIgnoreCase))
                    {
                        result.FailedMods.Add(ext.FileName);
                        continue;
                    }

                    var localExtDll = localDlls.FirstOrDefault(d =>
                        d.Source == "external" &&
                        string.Equals(
                            d.InstalledPath?.Split('\\', '/').LastOrDefault(),
                            ext.FileName,
                            StringComparison.OrdinalIgnoreCase));

                    if (localExtDll != null &&
                        !string.IsNullOrEmpty(ext.Sha256) &&
                        string.Equals(localExtDll.Sha256, ext.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    progress?.Report((78, ext.FileName));
                    var ok = await InstallExternalDllAsync(pack.PackCode, ext, targetMod, ct);
                    if (ok)
                        result.InstalledMods.Add(ext.FileName);
                    else
                        result.FailedMods.Add(ext.FileName);
                }

                if (pack.TouConfig.HasValue && pack.TouConfig.Value.ValueKind != JsonValueKind.Undefined)
                {
                    progress?.Report((90, "tou_config"));
                    ModInstanceTouConfigService.ApplyJsonToGlobalFile(pack.TouConfig.Value);
                    ModInstanceTouConfigService.SaveSnapshot(_instanceRepository, instanceId, pack.TouConfig.Value);
                    result.InstalledMods.Add("ToU config");
                }

                result.Success = result.InstalledMods.Count > 0 || result.FailedMods.Count == 0;
                progress?.Report((100, "done"));
                return result;
            }
            catch (Exception ex)
            {
                _log.Write($"[ModPackInstaller] UpdateExistingInstanceAsync: {ex.Message}");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private bool TryResolveFullModConfig(ModPack pack, out ModConfiguration? fullModConfig, out string? errorMessage)
        {
            fullModConfig = null;
            errorMessage = null;

            var usingCustomFull = pack.HasCustomFullMod;
            var customFull = pack.CustomFullMod;

            if (!usingCustomFull && pack.FullMod == null)
            {
                errorMessage = "mod_pack_missing_full_mod";
                return false;
            }

            var allConfigs = _configService.LoadConfig();
            if (usingCustomFull)
            {
                if (string.IsNullOrWhiteSpace(customFull!.DownloadUrl))
                {
                    errorMessage = "custom_full_download_missing";
                    return false;
                }

                fullModConfig = BuildCustomFullModConfig(customFull);
                return true;
            }

            var fullMod = pack.FullMod!;
            var match = allConfigs.FirstOrDefault(c => c.Id == fullMod.Id);
            if (match == null)
            {
                errorMessage = "mod_pack_full_mod_not_in_catalog";
                return false;
            }

            fullModConfig = CloneForInstall(match, fullMod.Version);
            return true;
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

            var usingCustomFull = pack.HasCustomFullMod;
            var customFull = pack.CustomFullMod;

            if (!usingCustomFull && pack.FullMod == null)
            {
                result.ErrorMessage = "mod_pack_missing_full_mod";
                return result;
            }

            var allConfigs = _configService.LoadConfig();
            ModConfiguration fullModConfig;
            if (usingCustomFull)
            {
                if (string.IsNullOrWhiteSpace(customFull!.DownloadUrl))
                {
                    result.ErrorMessage = "custom_full_download_missing";
                    return result;
                }

                fullModConfig = BuildCustomFullModConfig(customFull);
            }
            else
            {
                var fullMod = pack.FullMod!;
                var match = allConfigs.FirstOrDefault(c => c.Id == fullMod.Id);
                if (match == null)
                {
                    result.ErrorMessage = "mod_pack_full_mod_not_in_catalog";
                    result.FailedMods.Add(pack.ModName ?? $"mod#{fullMod.Id}");
                    return result;
                }

                fullModConfig = CloneForInstall(match, fullMod.Version);
            }

            var modToInstall = fullModConfig;
            var instanceName = string.IsNullOrWhiteSpace(displayName)
                ? (pack.ModName ?? fullModConfig.ModName ?? "Zestaw")
                : displayName.Trim();

            try
            {
                progress?.Report((5, "Instalacja moda głównego..."));
                var progressReporter = new TupleProgressReporter(progress, 5, 45);
                var diag = new SimpleDiagnostics(_log);
                var callbacks = modManagerCallbacks ?? new ModManagerUserCallbacks();

                var origin = usingCustomFull ? "shared_pack_custom_full" : "shared_pack";
                var instance = await _instanceInstaller!.InstallFullModInstanceAsync(
                    modToInstall,
                    instanceName,
                    platform,
                    progressReporter,
                    diag,
                    callbacks,
                    origin: origin,
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
                foreach (var ext in EnumerateExternalDlls(pack))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!string.Equals(ext.VtStatus, "clean", StringComparison.OrdinalIgnoreCase))
                    {
                        result.FailedMods.Add(ext.FileName);
                        continue;
                    }

                    progress?.Report((78, $"Pobieranie {ext.FileName}..."));
                    var ok = await InstallExternalDllAsync(pack.PackCode, ext, targetMod, ct);
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

            var usingCustomFull = pack.HasCustomFullMod;
            var customFull = pack.CustomFullMod;

            if (!usingCustomFull && pack.FullMod == null)
            {
                result.ErrorMessage = "Brak moda głównego.";
                return result;
            }

            var allConfigs = _configService.LoadConfig();
            ModConfiguration fullModConfig;

            if (usingCustomFull)
            {
                if (string.IsNullOrWhiteSpace(customFull!.DownloadUrl))
                {
                    result.ErrorMessage = "custom_full_download_missing";
                    return result;
                }

                fullModConfig = BuildCustomFullModConfig(customFull);
            }
            else
            {
                var fullMod = pack.FullMod!;
                var match = allConfigs.FirstOrDefault(c => c.Id == fullMod.Id);
                if (match == null)
                {
                    result.ErrorMessage = "Mod główny nie znaleziony w katalogu.";
                    result.FailedMods.Add(pack.ModName ?? $"mod#{fullMod.Id}");
                    return result;
                }
                fullModConfig = match;
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

                    var installResult = await modManager.ModifyAsync(
                        fullModConfig,
                        allConfigs,
                        progressReporter,
                        diag,
                        callbacks,
                        platform);
                    if (!installResult.Success)
                    {
                        result.Success = false;
                        result.ErrorMessage = installResult.ErrorMessage ?? "Nie udało się zainstalować moda głównego.";
                        result.FailedMods.Add(fullModConfig.ModName ?? "full mod");
                        return result;
                    }

                    allConfigs = _configService.LoadConfig();
                    fullModConfig = allConfigs.FirstOrDefault(c => c.Id == fullModConfig.Id) ?? fullModConfig;
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

                foreach (var ext in EnumerateExternalDlls(pack))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!string.Equals(ext.VtStatus, "clean", StringComparison.OrdinalIgnoreCase))
                    {
                        result.FailedMods.Add(ext.FileName);
                        continue;
                    }

                    progress?.Report((75, $"Pobieranie {ext.FileName}..."));
                    var ok = await InstallExternalDllAsync(pack.PackCode, ext, fullModConfig, ct);
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

        private static ModConfiguration BuildCustomFullModConfig(ModPackCustomArtifact customFull)
        {
            return new ModConfiguration
            {
                Id = 0,
                ModName = string.IsNullOrWhiteSpace(customFull.DisplayName)
                    ? customFull.FileName
                    : customFull.DisplayName,
                ModType = "full",
                ModVersion = customFull.Version ?? string.Empty,
                GitHubRepoOrLink = customFull.DownloadUrl ?? string.Empty,
                DllInstallPath = "BepInEx/plugins"
            };
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

        private static IEnumerable<ModPackExternalDll> EnumerateExternalDlls(ModPack pack)
        {
            foreach (var ext in pack.ExternalDlls)
                yield return ext;

            foreach (var artifact in pack.CustomArtifacts)
            {
                if (!IsDllArtifact(artifact))
                    continue;

                yield return new ModPackExternalDll
                {
                    FileName = artifact.FileName,
                    Sha256 = artifact.Sha256,
                    FileSize = artifact.FileSize,
                    VtStatus = artifact.Status,
                    VtPermalink = artifact.VtPermalink,
                    DownloadUrl = artifact.DownloadUrl,
                    DllInstallPath = artifact.DllInstallPath
                };
            }
        }

        private static bool IsDllArtifact(ModPackCustomArtifact artifact) =>
            string.Equals(artifact.ModType, "dll", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(artifact.SourceKind, "uploaded_dll", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(artifact.SourceKind, "github_dll", StringComparison.OrdinalIgnoreCase);

        private async Task<bool> InstallExternalDllAsync(
            string packCode, ModPackExternalDll ext, ModConfiguration targetMod, CancellationToken ct)
        {
            try
            {
                var downloadUrl = ext.DownloadUrl;
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    downloadUrl = $"{_apiV2BaseUrl}/modpacks/{packCode}/dlls/{ext.Sha256}";
                }

                var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
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

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                if (!string.IsNullOrWhiteSpace(ext.Sha256) &&
                    !Sha256Verifier.VerifyBytes(bytes, ext.Sha256))
                {
                    _log.Write($"[ModPackInstaller] External DLL SHA256 mismatch: {ext.FileName}");
                    return false;
                }

                var actualPath = PathSettings.GetActualModPath(targetMod.InstallPath!);
                if (!TryResolveSafeDllDirectory(actualPath, ext.DllInstallPath, out var pluginsDir))
                {
                    _log.Write($"[ModPackInstaller] Unsafe DLL install path blocked: {ext.DllInstallPath}");
                    return false;
                }

                if (!TryResolveSafeDllPath(pluginsDir, ext.FileName, out var safeDest))
                {
                    _log.Write($"[ModPackInstaller] Path traversal blocked: {ext.FileName}");
                    return false;
                }

                Directory.CreateDirectory(pluginsDir);
                var tempPath = Path.Combine(pluginsDir, $".{Path.GetFileName(safeDest)}.{Guid.NewGuid():N}.tmp");
                try
                {
                    await File.WriteAllBytesAsync(tempPath, bytes, ct);
                    File.Move(tempPath, safeDest, overwrite: true);
                }
                catch
                {
                    TryDeleteTempFile(tempPath);
                    throw;
                }
                _log.Write($"[ModPackInstaller] External DLL saved: {safeDest}");
                return true;
            }
            catch (Exception ex)
            {
                _log.Write($"[ModPackInstaller] External DLL error: {ex.Message}");
                return false;
            }
        }

        internal static bool TryResolveSafeDllDirectory(string actualModPath, string? dllInstallPath, out string safeDirectory)
        {
            safeDirectory = string.Empty;

            if (string.IsNullOrWhiteSpace(actualModPath))
                return false;

            var relativePath = string.IsNullOrWhiteSpace(dllInstallPath)
                ? Path.Combine("BepInEx", "plugins")
                : dllInstallPath.Trim().Replace('/', Path.DirectorySeparatorChar);

            if (string.Equals(relativePath, "plugins", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("plugins" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = Path.Combine("BepInEx", relativePath);
            }

            if (string.Equals(Path.GetExtension(relativePath), ".dll", StringComparison.OrdinalIgnoreCase))
            {
                relativePath = Path.GetDirectoryName(relativePath) ?? Path.Combine("BepInEx", "plugins");
            }

            if (Path.IsPathRooted(relativePath) ||
                relativePath.Contains("..", StringComparison.Ordinal) ||
                relativePath.Contains(':', StringComparison.Ordinal))
                return false;

            var fullActualPath = Path.GetFullPath(actualModPath);
            var pluginsRoot = Path.GetFullPath(Path.Combine(fullActualPath, "BepInEx", "plugins"));
            var targetDirectory = Path.GetFullPath(Path.Combine(fullActualPath, relativePath));

            if (!IsSameOrChildPath(targetDirectory, pluginsRoot))
                return false;

            safeDirectory = targetDirectory;
            return true;
        }

        private static bool IsSameOrChildPath(string candidate, string root)
        {
            var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedCandidate = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                   normalizedCandidate.StartsWith(
                       normalizedRoot + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static void TryDeleteTempFile(string tempPath)
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // best-effort cleanup only
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

            if (Path.IsPathRooted(fileName) ||
                fileName.Contains("..", StringComparison.Ordinal) ||
                fileName.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                fileName.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal) ||
                fileName.Contains(':', StringComparison.Ordinal))
                return false;

            var safeFileName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(safeFileName))
                return false;

            if (safeFileName is "." or "..")
                return false;

            if (!string.Equals(Path.GetExtension(safeFileName), ".dll", StringComparison.OrdinalIgnoreCase))
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
