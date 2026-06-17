using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;
using SUSModder.Core.Data;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Sprawdza dostępność aktualizacji paczek modów dla lokalnych instancji
    /// utworzonych z udostępnionych paczek (Origin = shared_pack, SourcePackCode != null).
    /// Read-only — nie instaluje automatycznie.
    /// </summary>
    public sealed class ModPackUpdateChecker
    {
        private readonly IModPackService _modPackService;
        private readonly IModInstanceRepository _instanceRepository;
        private readonly IDiagnosticsOutput _log;
        private readonly ConfigService _configService;

        public ModPackUpdateChecker(
            IModPackService modPackService,
            IModInstanceRepository instanceRepository,
            IDiagnosticsOutput log,
            ConfigService configService)
        {
            _modPackService = modPackService;
            _instanceRepository = instanceRepository;
            _log = log;
            _configService = configService;
        }

        /// <summary>
        /// Sprawdza aktualizacje dla wszystkich instancji z SourcePackCode.
        /// </summary>
        public async Task<List<ModPackUpdateInfo>> CheckAllInstancesAsync(CancellationToken ct = default)
        {
            var results = new List<ModPackUpdateInfo>();

            var instances = _instanceRepository.GetPackInstances()
                .Where(i => !string.IsNullOrEmpty(i.SourcePackCode) &&
                            i.Origin == ModInstanceOrigins.SharedPack)
                .ToList();

            _log.Write($"[ModPackUpdateChecker] Sprawdzam aktualizacje dla {instances.Count} instancji shared_pack...");

            foreach (var instance in instances)
            {
                var result = await CheckInstanceAsync(instance, ct);
                results.Add(result);
            }

            _log.Write($"[ModPackUpdateChecker] {results.Count(r => r.HasUpdate)}/{results.Count} instancji ma dostępną aktualizację paczki");
            return results;
        }

        /// <summary>
        /// Sprawdza aktualizację dla pojedynczej instancji.
        /// </summary>
        public async Task<ModPackUpdateInfo> CheckInstanceAsync(ModInstance instance, CancellationToken ct = default)
        {
            var result = new ModPackUpdateInfo
            {
                PackCode = instance.SourcePackCode ?? string.Empty,
                InstanceId = instance.InstanceId,
                InstanceName = instance.DisplayName,
                HasUpdate = false
            };

            if (string.IsNullOrEmpty(instance.SourcePackCode))
            {
                result.CheckSucceeded = false;
                result.ErrorMessage = "Brak kodu paczki";
                return result;
            }

            try
            {
                // 1. Pobierz aktualną paczkę z API
                var remotePack = await _modPackService.GetPackAsync(instance.SourcePackCode, ct);

                if (remotePack == null)
                {
                    result.CheckSucceeded = false;
                    result.ErrorMessage = "Paczka nie istnieje lub wygasła";
                    _log.Write($"[ModPackUpdateChecker] {instance.DisplayName}: paczka {instance.SourcePackCode} nie istnieje");
                    return result;
                }

                // 2. Porównaj z lokalną instancją
                var changes = new List<ModPackChangeItem>();

                // 2a. Full mod version
                if (remotePack.FullMod != null)
                {
                    var remoteVersion = remotePack.FullMod.Version ?? "";
                    var localVersion = instance.FullModVersion ?? "";

                    if (!string.Equals(remoteVersion, localVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        changes.Add(new ModPackChangeItem
                        {
                            ChangeType = "fullMod",
                            Name = instance.BaseModName,
                            OldValue = localVersion,
                            NewValue = remoteVersion
                        });
                    }
                }

                // 2b. DLL katalogowe — porównaj wersje
                var localDlls = _instanceRepository.GetDlls(instance.InstanceId);
                // Pobierz katalog aby znaleźć nazwy DLL po ID
                var catalogConfigs = _configService.LoadConfig();

                foreach (var remoteDll in remotePack.DllMods)
                {
                    var localDll = localDlls.FirstOrDefault(d => d.DllModId == remoteDll.DllModId);
                    var localVersion = localDll?.DllVersion ?? "";
                    var remoteVersion = remoteDll.DllModVersion ?? "";

                    if (!string.Equals(localVersion, remoteVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        var dllName = catalogConfigs.FirstOrDefault(c => c.Id == remoteDll.DllModId)?.ModName
                            ?? $"DLL #{remoteDll.DllModId}";

                        changes.Add(new ModPackChangeItem
                        {
                            ChangeType = "dll",
                            Name = dllName,
                            OldValue = localVersion,
                            NewValue = remoteVersion
                        });
                    }
                }

                // 2c. External DLL — porównaj SHA256 jeśli dostępny
                foreach (var remoteExtDll in remotePack.ExternalDlls)
                {
                    var localExtDll = localDlls.FirstOrDefault(d =>
                        d.Source == "external" &&
                        string.Equals(d.InstalledPath?.Split('\\', '/').LastOrDefault(),
                                      remoteExtDll.FileName, StringComparison.OrdinalIgnoreCase));

                    if (localExtDll != null && !string.IsNullOrEmpty(remoteExtDll.Sha256))
                    {
                        if (!string.Equals(localExtDll.Sha256, remoteExtDll.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            changes.Add(new ModPackChangeItem
                            {
                                ChangeType = "externalDll",
                                Name = remoteExtDll.FileName,
                                OldValue = localExtDll.Sha256,
                                NewValue = remoteExtDll.Sha256
                            });
                        }
                    }
                }

                // 2d. Konfiguracja ToU — informacja o możliwej zmianie (bez dokładnego porównania)
                if (remotePack.TouConfig.HasValue && remotePack.TouConfig.Value.ValueKind != System.Text.Json.JsonValueKind.Null)
                {
                    changes.Add(new ModPackChangeItem
                    {
                        ChangeType = "config",
                        Name = "Town of Us"
                    });
                }

                result.HasUpdate = changes.Any();
                result.Changes = changes;
                result.CheckSucceeded = true;

                _log.Write($"[ModPackUpdateChecker] {instance.DisplayName}: " +
                           $"{(result.HasUpdate ? $"{changes.Count} zmian" : "brak zmian")}");
            }
            catch (Exception ex)
            {
                result.CheckSucceeded = false;
                result.ErrorMessage = ex.Message;
                _log.Write($"[ModPackUpdateChecker] Błąd sprawdzania {instance.DisplayName}: {ex.Message}");
            }

            return result;
        }
    }
}
