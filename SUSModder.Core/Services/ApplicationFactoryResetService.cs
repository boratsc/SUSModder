using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Utilities;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Przywraca aplikację do stanu fabrycznego: usuwa mody, dane w %APPDATA% i config.json.
    /// Czyszczenie bazy SQLite odbywa się przy następnym starcie (plik jest zablokowany przez działającą instancję).
    /// </summary>
    public class ApplicationFactoryResetService
    {
        public const string PendingResetFlagFileName = "pending-factory-reset.flag";
        public const string ForceOnboardingFlagFileName = "force-onboarding.flag";

        public static string PendingResetFlagPath =>
            Path.Combine(UserSettingsService.GetAppDataFolder(), PendingResetFlagFileName);

        public static string ForceOnboardingFlagPath =>
            Path.Combine(UserSettingsService.GetAppDataFolder(), ForceOnboardingFlagFileName);
        public IReadOnlyList<string> CollectDataPathsToDelete()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var modsRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddModsRoot(string? path)
            {
                var normalized = NormalizeDirectoryPath(path);
                if (string.IsNullOrWhiteSpace(normalized))
                    return;

                modsRoots.Add(normalized);
                paths.Add(normalized);
            }

            var userSettings = new UserSettingsService().LoadUserSettings();

            AddModsRoot(PathSettings.ModsInstallPath);
            AddModsRoot(PathSettings.DefaultModsPath);
            AddModsRoot(userSettings.ModsInstallPath);

            try
            {
                var configs = new ConfigService().LoadConfig();
                foreach (var config in configs)
                {
                    if (IsVanillaMod(config) || string.IsNullOrWhiteSpace(config.InstallPath))
                        continue;

                    var installPath = NormalizeDirectoryPath(config.InstallPath);
                    if (string.IsNullOrWhiteSpace(installPath))
                        continue;

                    // Katalogi modów poza głównym folderem SUSModder (niestandardowa lokalizacja)
                    if (IsUnderAnyRoot(installPath, modsRoots))
                        continue;

                    if (IsProtectedGameInstallPath(installPath))
                        continue;

                    paths.Add(installPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FactoryReset] Nie udało się odczytać ścieżek modów: {ex.Message}");
            }

            return paths.OrderByDescending(p => p.Length).ToList();
        }

        private static bool IsVanillaMod(ModConfiguration config) =>
            config.Id == 0 ||
            config.ModType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) ||
            config.ModName.Equals("AmongUs", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Oryginalna instalacja gry Steam — nie należy do SUSModder i nie jest usuwana przy resecie.
        /// </summary>
        private static bool IsProtectedGameInstallPath(string path)
        {
            var normalized = path.Replace('/', '\\').TrimEnd('\\');
            return normalized.Contains(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnderAnyRoot(string path, IEnumerable<string> roots)
        {
            foreach (var root in roots)
            {
                if (IsSameOrSubPath(root, path))
                    return true;
            }

            return false;
        }

        private static bool IsSameOrSubPath(string root, string path)
        {
            try
            {
                var normalizedRoot = Path.GetFullPath(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var normalizedPath = Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                if (normalizedRoot.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))
                    return true;

                var prefix = normalizedRoot + Path.DirectorySeparatorChar;
                return normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string? NormalizeDirectoryPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            return Environment.ExpandEnvironmentVariables(path.Trim().TrimEnd('\\', '/'));
        }

        public async Task DeleteModsDirectoriesAsync(
            IEnumerable<string> paths,
            IDiagnosticsOutput? diagnostics = null)
        {
            foreach (var path in paths)
            {
                if (!Directory.Exists(path))
                    continue;

                bool deleted = await FileSystemUtilities.SafeDeleteDirectoryAsync(
                    path,
                    Path.GetFileName(path.TrimEnd('\\', '/')),
                    diagnostics);

                if (!deleted && Directory.Exists(path))
                {
                    diagnostics?.Write($"[FactoryReset] SafeDelete nie powiódł się, próba force delete: {path}");
                    ForceDeleteDirectorySync(path);
                }
            }
        }

        /// <summary>
        /// Oznacza reset danych aplikacji do wykonania przy następnym uruchomieniu
        /// (susmodder.db jest zablokowany przez bieżący proces).
        /// </summary>
        public void ScheduleApplicationDataResetOnNextStartup()
        {
            var appData = UserSettingsService.GetAppDataFolder();
            Directory.CreateDirectory(appData);
            File.WriteAllText(PendingResetFlagPath, DateTime.UtcNow.ToString("O"));
        }

        /// <summary>
        /// Kończy reset danych aplikacji zaplanowany przed restartem.
        /// Wywoływać na starcie aplikacji PRZED otwarciem bazy SQLite.
        /// </summary>
        public static bool CompletePendingApplicationDataResetIfNeeded()
        {
            if (!File.Exists(PendingResetFlagPath))
                return false;

            var appData = UserSettingsService.GetAppDataFolder();

            for (int attempt = 1; attempt <= 8; attempt++)
            {
                try
                {
                    if (Directory.Exists(appData))
                        ClearDirectoryContents(appData);

                    Directory.CreateDirectory(appData);
                    File.WriteAllText(ForceOnboardingFlagPath, "factory-reset");
                    return true;
                }
                catch (Exception ex) when (attempt < 8)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[FactoryReset] Próba {attempt}/8 czyszczenia app data nie powiodła się: {ex.Message}");
                    Thread.Sleep(400);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[FactoryReset] Nie udało się wyczyścić app data po restarcie: {ex.Message}");
                    return false;
                }
            }

            return false;
        }

        public void DeleteRuntimeConfigFile()
        {
            var configPath = Path.Combine(ApplicationPaths.GetApplicationDirectory(), "config.json");
            if (File.Exists(configPath))
                File.Delete(configPath);
        }

        private static void ClearDirectoryContents(string directoryPath)
        {
            foreach (var file in Directory.GetFiles(directoryPath))
                TryDeleteFile(file);

            foreach (var dir in Directory.GetDirectories(directoryPath))
                ForceDeleteDirectorySync(dir);
        }

        private static void TryDeleteFile(string filePath)
        {
            if (!File.Exists(filePath))
                return;

            try
            {
                var attr = File.GetAttributes(filePath);
                if ((attr & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    File.SetAttributes(filePath, attr & ~FileAttributes.ReadOnly);
            }
            catch
            {
                // ignoruj
            }

            File.Delete(filePath);
        }

        private static void ForceDeleteDirectorySync(string path)
        {
            if (!Directory.Exists(path))
                return;

            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var attr = File.GetAttributes(file);
                    if ((attr & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                        File.SetAttributes(file, attr & ~FileAttributes.ReadOnly);
                }
                catch
                {
                    // Pojedyncze pliki mogą być zablokowane — kontynuuj.
                }
            }

            Directory.Delete(path, true);
        }
    }
}
