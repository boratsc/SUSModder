using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SUSModder.Core.Utilities;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Configuration;

namespace SUSModder.Core.GameIntegration
{
    public class ModUpdates
    {
        public static async Task UpdateModAsync(
            ModConfiguration modConfig,
            List<ModConfiguration> modConfigs,
            IProgressReporter progress,
            IDiagnosticsOutput log,
            IUserInteraction userInteraction,
            IConfiguration configuration)
        {
            try
            {
                if (modConfig.ModType == "full")
                {
                    progress.Report(0, "Rozpoczynam aktualizację moda...");
                    log.Write($"[Aktualizacja] Usuwam istniejącego moda: {modConfig.ModName}");

                    // Usunięcie istniejącego moda przed aktualizacją
                    ModDelete.DeleteMod(modConfig, modConfigs, userInteraction);

                    // Inicjalizacja ModManager z konfiguracją
                    ModManager modManager = new ModManager(configuration);

                    // Użycie mode z konfiguracji
                    string mode = configuration["Configuration:Mode"] ?? "steam";

                    // Wywołanie ModifyAsync z prawidłowym argumentem mode
                    await modManager.ModifyAsync(
                        modConfig,
                        modConfigs,
                        progress,
                        log,
                        userInteraction,
                        mode
                    );

                    progress.Report(100, "Mod zaktualizowany.");
                    userInteraction.ShowInfo($"Mod '{modConfig.ModName}' został pomyślnie zaktualizowany.", "Sukces");
                }
            }
            catch (Exception ex)
            {
                log.Write($"[ERROR] Wystąpił błąd podczas aktualizacji: {ex}");
                userInteraction.ShowError($"Wystąpił błąd podczas aktualizacji: {ex.Message}", "Błąd");
            }
        }
    }
}
