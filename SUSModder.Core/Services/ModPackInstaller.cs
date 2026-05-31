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
using SUSModder.Core.Diagnostics;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Models;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Instalacja paczki modów: full mod, DLL katalogowe, external DLL, config ToU, integration.dll.
    /// </summary>
    public sealed class ModPackInstaller
    {
        private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

        private readonly IConfiguration _configuration;
        private readonly ConfigService _configService;
        private readonly DllModificationService _dllService;
        private readonly IDiagnosticsOutput _log;
        private readonly string _baseUrl;
        private readonly string _modPacksEndpoint;

        public ModPackInstaller(
            IConfiguration configuration,
            ConfigService configService,
            DllModificationService dllService,
            IDiagnosticsOutput log)
        {
            _configuration = configuration;
            _configService = configService;
            _dllService = dllService;
            _log = log;
            _baseUrl = (_configuration["Configuration:BaseUrl"] ?? "https://susmodder.app/").TrimEnd('/');
            _modPacksEndpoint = _configuration["Configuration:ModPacksEndpoint"] ?? "/api/mod-packs";
        }

        public async Task<ModPackInstallResult> InstallPackAsync(
            ModPack pack,
            string platform,
            IProgress<(int percent, string message)>? progress = null,
            CancellationToken ct = default)
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
                    var callbacks = new ModManagerUserCallbacks();

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

                // DLL katalogowe
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

                // External DLL
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

                // Config ToU
                if (pack.TouConfig.HasValue && pack.TouConfig.Value.ValueKind != JsonValueKind.Undefined)
                {
                    progress?.Report((90, "Stosowanie configu ToU..."));
                    ApplyTouConfigJson(pack.TouConfig.Value);
                    result.InstalledMods.Add("ToU config");
                }

                // integration.dll
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

                var response = await HttpClient.GetAsync(downloadUrl);
                if (!response.IsSuccessStatusCode)
                {
                    _log.Write($"[ModPackInstaller] External DLL download failed: {response.StatusCode}");
                    return false;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync();
                var actualPath = PathSettings.GetActualModPath(targetMod.InstallPath!);
                var pluginsDir = Path.Combine(actualPath, "BepInEx", "plugins");
                Directory.CreateDirectory(pluginsDir);
                var dest = Path.Combine(pluginsDir, ext.FileName);
                await File.WriteAllBytesAsync(dest, bytes);
                _log.Write($"[ModPackInstaller] External DLL saved: {dest}");
                return true;
            }
            catch (Exception ex)
            {
                _log.Write($"[ModPackInstaller] External DLL error: {ex.Message}");
                return false;
            }
        }

        private static void ApplyTouConfigJson(JsonElement config)
        {
            // API zwraca pełny JSON configu — zapisujemy jako settings.amogus_TOU jeśli to obiekt z polami gry
            var destDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @"AppData\LocalLow\Innersloth\Among Us");
            Directory.CreateDirectory(destDir);

            var destFile = Path.Combine(destDir, "settings.amogus_TOU");
            var json = config.GetRawText();
            File.WriteAllText(destFile, json);
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

        private sealed class SimpleProgressReporter : IProgressReporter
        {
            private readonly Action<int> _onProgress;
            public SimpleProgressReporter(Action<int> onProgress) => _onProgress = onProgress;
            public void Report(int percent, string? message = null) => _onProgress(percent);
        }

        private sealed class SimpleDiagnostics : IDiagnosticsOutput
        {
            private readonly IDiagnosticsOutput _inner;
            public SimpleDiagnostics(IDiagnosticsOutput inner) => _inner = inner;
            public void Write(string message) => _inner.Write($"[ModPackInstaller] {message}");
        }
    }
}
