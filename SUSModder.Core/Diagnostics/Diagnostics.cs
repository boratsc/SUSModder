using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SUSModder.Core;
using SUSModder.Core.Utilities;
using SUSModder.Core.Configuration;

namespace SUSModder.Core.Diagnostics
{
    public static class Diagnostics
    {
        private static IDiagnosticsOutput? _output;

        public static void SetOutput(IDiagnosticsOutput output)
        {
            _output = output;
        }

        private static void Write(string line)
        {
            _output?.Write(line);
        }

        private static readonly HashSet<string> _excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Mini.RegionInstall.dll",
            "Reactor.dll",
            "touhats.bundle",
            "touhats.catalog"
        };

        public static void LogModsAndPlugins(string? appVersion = null)
        {
            // 1) zainstalowane mody wg config.json
            var configs = ConfigManager.LoadConfig();
            Write("=== Zainstalowane mody (config.json) ===");
            foreach (var cfg in configs.Where(c => !string.IsNullOrWhiteSpace(c.InstallPath)))
            {
                Write(cfg.ModName);
            }
            Write(string.Empty);

            // 2) „ręczne” mody w folderze ModsInstallPath
            var modsRoot = PathSettings.ModsInstallPath
                           ?? PathSettings.DefaultModsPath;

            Write($"=== Katalogi w folderze: {modsRoot} ===");
            if (Directory.Exists(modsRoot))
            {
                foreach (var dir in Directory.EnumerateDirectories(modsRoot))
                {
                    Write(Path.GetFileName(dir));
                }
            }
            else
            {
                Write($"Folder nie istnieje: {modsRoot}");
            }

            Write(string.Empty);

            // 3) wyszukiwanie DLL/.bundle/.catalog w BepInEx\plugins
            Write("=== Nie-standardowe pluginy w BepInEx\\plugins ===");
            if (Directory.Exists(modsRoot))
            {
                foreach (var modDir in Directory.GetDirectories(modsRoot))
                {
                    var modName = Path.GetFileName(modDir);
                    var pluginDir = Path.Combine(modDir, "BepInEx", "plugins");
                    if (!Directory.Exists(pluginDir))
                        continue;

                    foreach (var file in Directory.EnumerateFiles(pluginDir))
                    {
                        var fn = Path.GetFileName(file);
                        var ext = Path.GetExtension(fn).ToLowerInvariant();
                        if (_excluded.Contains(fn))
                            continue;

                        if (ext == ".dll" || ext == ".bundle" || ext == ".catalog")
                            Write($"{modName}\\{fn}");
                    }
                }
            }

            Write("========================================");
            Write($"         SUSModder {appVersion ?? ""}       ");
            Write("========================================");
            Write("      === Koniec diagnostyki ===      ");
            Write("========================================");
        }
    }
}
