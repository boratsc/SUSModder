using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Configuration;

namespace SUSModder.Core.Services
{
    public class GameService
    {
        private static readonly string[] CommonSteamPaths =
        {
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam",
            Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%\\Steam"),
            "D:\\Steam",
            "D:\\",
            "D:\\Gry\\Steam"
        };

        private static readonly string[] CommonEpicPaths =
        {
            @"C:\Program Files (x86)\Epic Games",
            @"C:\Program Files\Epic Games",
            "D:\\Epic Games",
            "D:\\Gry\\Epic Games",
            "D:\\Gry\\Epic"
        };

        /// <summary>
        /// Próbuj znaleźć ścieżkę Among Us (Steam/Epic). Zwraca ścieżkę i tryb ("steam"/"epic") lub null.
        /// </summary>
        public (string? Path, string? Mode) TryFindAmongUsPath()
        {
            foreach (var basePath in CommonSteamPaths)
            {
                var path = Path.Combine(basePath, "steamapps", "common", "Among Us");
                if (Directory.Exists(path) && File.Exists(Path.Combine(path, "Among Us.exe")))
                {
                    return (path, "steam");
                }
            }

            foreach (var basePath in CommonEpicPaths)
            {
                var path = Path.Combine(basePath, "AmongUs");
                if (Directory.Exists(path) && File.Exists(Path.Combine(path, "Among Us.exe")) && Directory.Exists(Path.Combine(path, ".egstore")))
                {
                    return (path, "epic");
                }
            }

            return (null, null);
        }

        /// <summary>
        /// Pobiera wersję gry z pliku Among Us.exe.
        /// </summary>
        public string GetGameVersion(string path)
        {
            try
            {
                var exePath = Path.Combine(path, "Among Us.exe");
                var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
                return versionInfo.FileVersion ?? "Nieznana";
            }
            catch
            {
                return "Nieznana";
            }
        }

        /// <summary>
        /// Ustawia/aktualizuje konfigurację vanilla Among Us w liście modów.
        /// </summary>
        public void CheckAndSetupVanillaMod(List<ModConfiguration> modConfigs, IConfiguration configuration)
        {
            var existingConfig = modConfigs.FirstOrDefault(x => x.ModName == "AmongUs" &&
                                                                x.Id == 0 &&
                                                                !string.IsNullOrEmpty(x.InstallPath));

            string currentMode = configuration["Configuration:Mode"] ?? "steam";

            if (existingConfig != null)
            {
                return;
            }

            var (foundPath, detectedMode) = TryFindAmongUsPath();
            if (foundPath == null)
            {
                // W UI: userInteraction.ShowInfo("Nie znaleziono podstawowej wersji Among Us. Proszę wskazać folder ręcznie...", "Informacja");
                return;
            }

            if (detectedMode != null)
            {
                configuration["Configuration:Mode"] = detectedMode;
            }

            var vanillaMod = new ModConfiguration
            {
                ModName = "AmongUs",
                PngFileName = "Vanilla.png",
                InstallPath = foundPath,
                GitHubRepoOrLink = string.Empty,
                EpicGitHubRepoOrLink = string.Empty,
                ModType = "Vanilla",
                DllInstallPath = null,
                ModVersion = "",
                LastUpdated = DateTime.Now,
                AmongVersion = GetGameVersion(foundPath),
                Description = $"Detected as {detectedMode}"
            };

            modConfigs.Add(vanillaMod);
            ConfigManager.SaveConfig(modConfigs);

            if (detectedMode != null)
            {
                configuration["Configuration:Mode"] = detectedMode;
                ConfigManager.SaveConfigurationSetting("Mode", detectedMode);
            }
        }
    }
}
