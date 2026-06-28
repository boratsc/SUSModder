using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SUSModder.Core.Configuration;
using SUSModder.Core.Services;

namespace SUSModder.Core.GameIntegration
{
    /// <summary>
    /// Wyszukuje instalację Among Us (Steam) w systemie — rejestr, manifesty Steam, biblioteki i typowe ścieżki.
    /// </summary>
    public static class AmongUsPathDiscovery
    {
        public const string SteamAppId = "945360";
        public const string GameExeName = "Among Us.exe";
        public const string GameFolderName = "Among Us";

        private static readonly string[] CommonSteamRoots =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam"),
            @"D:\SteamLibrary",
            @"D:\Steam",
            @"D:\Gry\Steam",
            @"E:\SteamLibrary",
            @"E:\Steam",
            @"F:\SteamLibrary",
            @"F:\Steam"
        };

        public static bool IsValidInstallDirectory(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                var normalized = NormalizePath(path);
                return File.Exists(Path.Combine(normalized, GameExeName));
            }
            catch
            {
                return false;
            }
        }

        public static string? TryFindInstallDirectory()
        {
            foreach (var candidate in CollectCandidatePaths())
            {
                if (IsValidInstallDirectory(candidate))
                    return NormalizePath(candidate);
            }

            return null;
        }

        public static IEnumerable<string> CollectCandidatePaths()
        {
            var candidates = new List<string>();

            void AddCandidate(string? path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;

                candidates.Add(NormalizePath(path));
            }

            try
            {
                var settings = new UserSettingsService().LoadUserSettings();
                AddCandidate(settings.VanillaInstallPath);
            }
            catch
            {
                // ignoruj
            }

            try
            {
                var configs = new ConfigService().LoadConfig();
                var vanilla = configs.FirstOrDefault(c =>
                    c.Id == 0 ||
                    c.ModType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) ||
                    c.ModName.Equals("AmongUs", StringComparison.OrdinalIgnoreCase));

                AddCandidate(vanilla?.InstallPath);
            }
            catch
            {
                // ignoruj
            }

            foreach (var steamPath in DiscoverSteamInstallPaths())
                AddCandidate(steamPath);

            AddCandidate(TryGetRegistryInstallLocation());

            foreach (var scannedPath in ScanFixedDrivesForAmongUs())
                AddCandidate(scannedPath);

            return candidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(p => p.Length);
        }

        private static IEnumerable<string> DiscoverSteamInstallPaths()
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var libraryRoot in GetAllSteamLibraryRoots())
            {
                var directPath = Path.Combine(libraryRoot, "steamapps", "common", GameFolderName);
                if (IsValidInstallDirectory(directPath))
                    results.Add(NormalizePath(directPath));

                var manifestPath = Path.Combine(libraryRoot, "steamapps", $"appmanifest_{SteamAppId}.acf");
                if (!File.Exists(manifestPath))
                    continue;

                var installDir = TryParseAppManifestInstallDir(manifestPath);
                if (string.IsNullOrWhiteSpace(installDir))
                    continue;

                var manifestGamePath = Path.Combine(libraryRoot, "steamapps", "common", installDir);
                if (IsValidInstallDirectory(manifestGamePath))
                    results.Add(NormalizePath(manifestGamePath));
            }

            return results;
        }

        private static IEnumerable<string> GetAllSteamLibraryRoots()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddRoot(string? path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;

                try
                {
                    var normalized = NormalizePath(path);
                    if (Directory.Exists(normalized))
                        roots.Add(normalized);
                }
                catch
                {
                    // ignoruj
                }
            }

            if (OperatingSystem.IsWindows())
            {
                AddRoot(ReadRegistryString(Registry.CurrentUser, @"Software\Valve\Steam", "InstallPath"));
                AddRoot(ReadRegistryString(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"));
                AddRoot(ReadRegistryString(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"));
            }

            foreach (var commonRoot in CommonSteamRoots)
                AddRoot(commonRoot);

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                var letter = drive.Name.TrimEnd('\\', '/');
                AddRoot(Path.Combine(letter, "Steam"));
                AddRoot(Path.Combine(letter, "Program Files (x86)", "Steam"));
                AddRoot(Path.Combine(letter, "Program Files", "Steam"));
                AddRoot(Path.Combine(letter, "SteamLibrary"));
                AddRoot(Path.Combine(letter, "Gry", "Steam"));
                AddRoot(Path.Combine(letter, "Games", "Steam"));
            }

            foreach (var root in roots.ToList())
                AddLibraryFoldersFromVdf(root, roots);

            return roots;
        }

        private static void AddLibraryFoldersFromVdf(string steamRoot, HashSet<string> roots)
        {
            var libraryFoldersPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFoldersPath))
                return;

            try
            {
                var content = File.ReadAllText(libraryFoldersPath);
                foreach (var libraryPath in ParseSteamLibraryFolders(content))
                {
                    var normalizedLibrary = NormalizePath(libraryPath);
                    if (Directory.Exists(normalizedLibrary))
                        roots.Add(normalizedLibrary);
                }
            }
            catch
            {
                // ignoruj
            }
        }

        private static IEnumerable<string> ParseSteamLibraryFolders(string content)
        {
            var matches = Regex.Matches(content, "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                if (match.Groups.Count < 2)
                    continue;

                var raw = match.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                yield return raw.Replace("\\\\", "\\");
            }
        }

        internal static string? TryParseAppManifestInstallDir(string manifestPath)
        {
            try
            {
                return TryParseAppManifestInstallDirFromContent(File.ReadAllText(manifestPath));
            }
            catch
            {
                return null;
            }
        }

        internal static string? TryParseAppManifestInstallDirFromContent(string content)
        {
            var match = Regex.Match(content, "\"installdir\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string? TryGetRegistryInstallLocation()
        {
            if (!OperatingSystem.IsWindows())
                return null;

            string[] registryKeys =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 945360",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 945360"
            };

            foreach (var registryKey in registryKeys)
            {
                var location = ReadRegistryString(Registry.LocalMachine, registryKey, "InstallLocation");
                if (IsValidInstallDirectory(location))
                    return NormalizePath(location!);
            }

            return null;
        }

        private static IEnumerable<string> ScanFixedDrivesForAmongUs()
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                var letter = drive.Name.TrimEnd('\\', '/');
                string[] relativeCandidates =
                {
                    Path.Combine("SteamLibrary", "steamapps", "common", GameFolderName),
                    Path.Combine("Steam", "steamapps", "common", GameFolderName),
                    Path.Combine("Program Files (x86)", "Steam", "steamapps", "common", GameFolderName),
                    Path.Combine("Program Files", "Steam", "steamapps", "common", GameFolderName),
                    Path.Combine("Gry", "Steam", "steamapps", "common", GameFolderName),
                    Path.Combine("Games", "Steam", "steamapps", "common", GameFolderName)
                };

                foreach (var relative in relativeCandidates)
                {
                    var candidate = Path.Combine(letter, relative);
                    if (IsValidInstallDirectory(candidate))
                        results.Add(NormalizePath(candidate));
                }
            }

            return results;
        }

        [SupportedOSPlatform("windows")]
        private static string? ReadRegistryString(RegistryKey root, string subKeyPath, string valueName)
        {
            try
            {
                using var key = root.OpenSubKey(subKeyPath);
                return key?.GetValue(valueName) as string;
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Replace('/', Path.DirectorySeparatorChar)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }
    }
}
