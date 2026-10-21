using System;
using System.IO;
using SUSModder.Core.GameIntegration;

namespace SUSModder.Core.Utilities
{
    /// <summary>
    /// Waliduje katalog instalacji modów — blokuje Program Files, instalacje gry Steam/Epic i brak zapisu.
    /// </summary>
    public static class ModsInstallPathGuard
    {
        public const string ErrorEmpty = "empty";
        public const string ErrorInvalidPath = "invalid_path";
        public const string ErrorProgramFiles = "program_files";
        public const string ErrorSteamGameInstall = "steam_game_install";
        public const string ErrorGameInstallDirectory = "game_install_directory";
        public const string ErrorNotWritable = "not_writable";

        public readonly record struct ValidationResult(bool IsValid, string? ErrorCode, string Message)
        {
            public static ValidationResult Ok(string normalizedPath) =>
                new(true, null, normalizedPath);
        }

        public static ValidationResult Validate(string? path, bool probeWriteAccess = true)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ValidationResult(
                    false,
                    ErrorEmpty,
                    "Mods install path cannot be empty.");
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
            }
            catch (Exception ex)
            {
                return new ValidationResult(
                    false,
                    ErrorInvalidPath,
                    $"Invalid mods install path: {ex.Message}");
            }

            if (IsUnderProtectedSystemDirectory(fullPath))
            {
                return new ValidationResult(
                    false,
                    ErrorProgramFiles,
                    "Mods install folder cannot be under Program Files or other protected system locations.");
            }

            if (IsSteamGameInstallPath(fullPath))
            {
                return new ValidationResult(
                    false,
                    ErrorSteamGameInstall,
                    "Mods install folder cannot be inside the Steam game folder (steamapps\\common).");
            }

            if (TryFindAmongUsInstallRoot(fullPath, out var gameRoot))
            {
                return new ValidationResult(
                    false,
                    ErrorGameInstallDirectory,
                    $"Selected folder is part of Among Us installation ({gameRoot}). SUSModder installs mods outside the game folder.");
            }

            if (probeWriteAccess && !IsWritableDirectory(fullPath))
            {
                return new ValidationResult(
                    false,
                    ErrorNotWritable,
                    "No write permission in the selected folder. Choose a folder in your user profile (e.g. %APPDATA%\\Among Us - Mody).");
            }

            return ValidationResult.Ok(fullPath);
        }

        /// <summary>
        /// Zwraca bezpieczną ścieżkę instalacji modów lub domyślną, gdy zapisana jest nieprawidłowa.
        /// </summary>
        public static string ResolveSafeModsInstallPath(string? configuredPath, string defaultPath)
        {
            var validation = Validate(configuredPath, probeWriteAccess: false);
            if (validation.IsValid)
                return validation.Message;

            System.Diagnostics.Debug.WriteLine(
                $"[ModsInstallPathGuard] Odrzucono ścieżkę '{configuredPath}' ({validation.ErrorCode}): {validation.Message}");

            return defaultPath;
        }

        public static bool IsSteamGameInstallPath(string path)
        {
            var normalized = Normalize(path);
            return normalized.Contains(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsUnderProtectedSystemDirectory(string path)
        {
            var normalized = Normalize(path);

            foreach (var protectedRoot in GetProtectedSystemRoots())
            {
                if (IsSameOrSubPath(protectedRoot, normalized))
                    return true;
            }

            return false;
        }

        public static bool TryFindAmongUsInstallRoot(string path, out string gameRoot)
        {
            gameRoot = string.Empty;

            try
            {
                var current = new DirectoryInfo(Path.GetFullPath(path));
                for (var depth = 0; depth < 12 && current != null; depth++, current = current.Parent)
                {
                    var exePath = Path.Combine(current.FullName, AmongUsPathDiscovery.GameExeName);
                    if (!File.Exists(exePath))
                        continue;

                    gameRoot = current.FullName;
                    return true;
                }
            }
            catch
            {
                // ignore invalid paths
            }

            return false;
        }

        private static bool IsWritableDirectory(string path)
        {
            try
            {
                Directory.CreateDirectory(path);

                var probeFile = Path.Combine(
                    path,
                    $".susmodder-write-test-{Guid.NewGuid():N}.tmp");

                File.WriteAllText(probeFile, "ok");
                File.Delete(probeFile);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string[] GetProtectedSystemRoots()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            return new[]
            {
                programFiles,
                programFilesX86,
                windows
            };
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

        private static string Normalize(string path) =>
            path.Replace('/', '\\').TrimEnd('\\');
    }
}
