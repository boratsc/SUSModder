using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SUSModder.Core.Utilities;
using SUSModder.Core.Configuration;

namespace SUSModder.Core.GameIntegration
{
    public static class ModDelete
    {
        public static void DeleteMod(ModConfiguration modConfig, List<ModConfiguration> modConfigs, IUserInteraction userInteraction)
        {
            if (modConfig.ModType == "full")
            {
                DeleteFullMod(modConfig, modConfigs, userInteraction);
            }
            else if (modConfig.ModType == "dll")
            {
                DeleteDllMod(modConfig, modConfigs, userInteraction);
            }
        }

        private static void DeleteFullMod(ModConfiguration modConfig, List<ModConfiguration> modConfigs, IUserInteraction userInteraction)
        {
            try
            {
                if (Directory.Exists(modConfig.InstallPath))
                {
                    Directory.Delete(modConfig.InstallPath, true);
                    modConfig.InstallPath = string.Empty;
                    ConfigManager.SaveConfig(modConfigs);
                    userInteraction.ShowInfo($"Mod '{modConfig.ModName}' został pomyślnie usunięty.", "Sukces");
                }
            }
            catch (Exception ex)
            {
                userInteraction.ShowError($"Wystąpił błąd podczas usuwania: {ex.Message}", "Błąd");
            }
        }

        private static void DeleteDllMod(ModConfiguration modConfig, List<ModConfiguration> modConfigs, IUserInteraction userInteraction)
        {
            // W Core nie robimy UI do wyboru modów - UI powinien przekazać listę wybranych modów
            var fullMods = modConfigs.Where(x => x.ModType == "full" && !string.IsNullOrEmpty(x.InstallPath)).ToList();

            // Tu zakładamy, że UI przekazuje listę modów do usunięcia DLL (np. przez osobny parametr)
            // Dla uproszczenia: usuwamy DLL ze wszystkich pełnych modów
            foreach (var fullMod in fullMods)
            {
                string dllPath = Path.Combine(
                    fullMod.InstallPath ?? string.Empty,
                    modConfig.DllInstallPath ?? string.Empty,
                    $"{modConfig.ModName}.dll"
                );
                if (File.Exists(dllPath))
                {
                    try
                    {
                        File.Delete(dllPath);
                        userInteraction.ShowInfo($"Mod DLL '{modConfig.ModName}' został pomyślnie usunięty z '{fullMod.ModName}'.", "Sukces");
                    }
                    catch (Exception ex)
                    {
                        userInteraction.ShowError($"Wystąpił błąd podczas usuwania DLL: {ex.Message}", "Błąd");
                    }
                }
            }
            modConfig.InstallPath = string.Empty;
            ConfigManager.SaveConfig(modConfigs);
        }
    }
}
