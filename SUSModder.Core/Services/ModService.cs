using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Utilities;
using SUSModder.Core.Configuration;
using SUSModder.Core.GameIntegration;
using SUSModder.Core.Diagnostics;


namespace SUSModder.Core.Services
{
    public class ModService
    {
        private readonly IConfiguration configuration;

        public ModService(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        /// <summary>
        /// Instalacja pełnego moda (full) lub dll (w wybranych modach full).
        /// </summary>
        public async Task InstallModAsync(
            ModConfiguration modConfig,
            List<ModConfiguration> modConfigs,
            IProgressReporter progress,
            IDiagnosticsOutput log,
            IUserInteraction userInteraction,
            string mode,
            List<ModConfiguration>? selectedFullModsForDll = null)
        {
            if (modConfig.ModType == "dll")
            {
                // Instalacja DLL do wybranych modów full
                if (selectedFullModsForDll == null || !selectedFullModsForDll.Any())
                {
                    userInteraction.ShowError("Nie wybrano żadnych pełnych modów do instalacji DLL.", "Błąd");
                    return;
                }
                var manager = new ModManager(configuration);
                var callbacks = new ModManagerUserCallbacks
                {
                    ConfirmAsync = userInteraction.ShowConfirmAsync,
                    ShowErrorAsync = userInteraction.ShowErrorAsync,
                    ShowInfoAsync = userInteraction.ShowInfoAsync
                };

                await manager.ModifyDllAsync(modConfig, selectedFullModsForDll, progress, log, callbacks);
                userInteraction.ShowInfo($"Instalacja moda {modConfig.ModName} zakończona pomyślnie.", "Sukces");
            }
            else if (modConfig.ModType == "full")
            {
                var manager = new ModManager(configuration);
                var callbacks = new ModManagerUserCallbacks
                {
                    ConfirmAsync = userInteraction.ShowConfirmAsync,
                    ShowErrorAsync = userInteraction.ShowErrorAsync,
                    ShowInfoAsync = userInteraction.ShowInfoAsync
                };

                await manager.ModifyAsync(modConfig, modConfigs, progress, log, callbacks, mode);
                userInteraction.ShowInfo($"Instalacja moda {modConfig.ModName} zakończona pomyślnie.", "Sukces");
            }
            else
            {
                userInteraction.ShowError("Nieobsługiwany typ moda do instalacji.", "Błąd");
            }
        }

        /// <summary>
        /// Aktualizacja pełnego moda (full).
        /// </summary>
        public async Task UpdateModAsync(
            ModConfiguration modConfig,
            List<ModConfiguration> modConfigs,
            IProgressReporter progress,
            IDiagnosticsOutput log,
            IUserInteraction userInteraction)
        {
            await ModUpdates.UpdateModAsync(modConfig, modConfigs, progress, log, userInteraction, configuration);
        }

        /// <summary>
        /// Usuwanie moda (full lub dll).
        /// </summary>
        public void DeleteMod(
            ModConfiguration modConfig,
            List<ModConfiguration> modConfigs,
            IUserInteraction userInteraction)
        {
            ModDelete.DeleteMod(modConfig, modConfigs, userInteraction);
        }

        /// <summary>
        /// Usuwa DLL moda z wybranych modów full.
        /// </summary>
        public void DeleteDllFromFullMods(
            ModConfiguration dllModConfig,
            List<ModConfiguration> fullMods,
            IDiagnosticsOutput log,
            IUserInteraction userInteraction)
        {
            foreach (var fullMod in fullMods)
            {
                // Sprawdź czy InstallPath nie jest null ani pusty
                if (string.IsNullOrEmpty(fullMod.InstallPath))
                {
                    log.Write($"Pominięto mod '{fullMod.ModName}' - brak ścieżki instalacji");
                    continue;
                }

                string dllPath = Path.Combine(
                    fullMod.InstallPath,
                    dllModConfig.DllInstallPath ?? string.Empty,
                    $"{dllModConfig.ModName}.dll"
                );

                try
                {
                    if (File.Exists(dllPath))
                    {
                        File.Delete(dllPath);
                        log.Write($"Plik {dllPath} został usunięty.");
                    }
                    else
                    {
                        log.Write($"Plik {dllPath} nie istnieje.");
                    }
                }
                catch (Exception ex)
                {
                    userInteraction.ShowError($"Błąd podczas usuwania pliku {dllPath}: {ex.Message}", "Błąd");
                }
            }

            userInteraction.ShowInfo($"Usuwanie moda {dllModConfig.ModName} zakończone pomyślnie.", "Sukces");
        }
    }
}
