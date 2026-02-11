using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SUSModder.Core.Utilities
{
    /// <summary>
    /// Pomocnicza klasa do znajdowania poprzedniej wersji aplikacji w strukturze Velopack (app-*/current).
    /// </summary>
    public static class PreviousVersionLocator
    {
        public static string? TryGetPreviousVersionDirectory()
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                if (string.IsNullOrWhiteSpace(baseDir))
                    return null;

                var normalizedBase = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var currentFolderName = Path.GetFileName(normalizedBase);

                if (!string.Equals(currentFolderName, "current", StringComparison.OrdinalIgnoreCase))
                    return null;

                var parent = Directory.GetParent(normalizedBase);
                if (parent == null || !parent.Exists)
                    return null;

                var currentVersionRaw = TryReadVersionString(Path.Combine(normalizedBase, "version.json"));
                var currentVersion = TryParseVersion(currentVersionRaw);

                var candidates = parent.GetDirectories()
                    .Where(d => !string.Equals(d.Name, "current", StringComparison.OrdinalIgnoreCase))
                    .Where(d =>
                        File.Exists(Path.Combine(d.FullName, "config.json")) ||
                        File.Exists(Path.Combine(d.FullName, "appsettings.json")) ||
                        File.Exists(Path.Combine(d.FullName, "version.json")))
                    .Select(d =>
                    {
                        var raw = TryReadVersionString(Path.Combine(d.FullName, "version.json")) ?? TryReadVersionFromFolderName(d.Name);
                        var version = TryParseVersion(raw);
                        return new Candidate(d, version, raw, d.LastWriteTimeUtc);
                    })
                    .Where(c =>
                        currentVersion == null ||
                        c.Version == null ||
                        !c.Version.Equals(currentVersion))
                    .ToList();

                if (candidates.Count == 0)
                    return null;

                var selected = candidates
                    .OrderByDescending(c => c.Version ?? new Version(0, 0))
                    .ThenByDescending(c => c.LastWriteTimeUtc)
                    .FirstOrDefault();

                return selected.Directory.FullName;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PreviousVersionLocator] Error: {ex.Message}");
                return null;
            }
        }

        public static string? TryGetPreviousConfigPath()
        {
            var dir = TryGetPreviousVersionDirectory();
            if (string.IsNullOrWhiteSpace(dir))
                return null;

            var path = Path.Combine(dir, "config.json");
            return File.Exists(path) ? path : null;
        }

        public static string? TryGetPreviousAppSettingsPath()
        {
            var dir = TryGetPreviousVersionDirectory();
            if (string.IsNullOrWhiteSpace(dir))
                return null;

            var path = Path.Combine(dir, "appsettings.json");
            return File.Exists(path) ? path : null;
        }

        private static string? TryReadVersionFromFolderName(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
                return null;

            if (!folderName.StartsWith("app-", StringComparison.OrdinalIgnoreCase))
                return null;

            return folderName.Substring("app-".Length);
        }

        private static string? TryReadVersionString(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;

                var json = File.ReadAllText(filePath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("currentVersion", out var currentVersion))
                    return currentVersion.GetString();

                if (root.TryGetProperty("CurrentVersion", out var currentVersionAlt))
                    return currentVersionAlt.GetString();

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static Version? TryParseVersion(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var cleaned = raw.Trim();
            if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(1);

            var dashIndex = cleaned.IndexOf('-');
            if (dashIndex > 0)
                cleaned = cleaned.Substring(0, dashIndex);

            return Version.TryParse(cleaned, out var version) ? version : null;
        }

        private readonly struct Candidate
        {
            public Candidate(DirectoryInfo directory, Version? version, string? rawVersion, DateTime lastWriteTimeUtc)
            {
                Directory = directory;
                Version = version;
                RawVersion = rawVersion;
                LastWriteTimeUtc = lastWriteTimeUtc;
            }

            public DirectoryInfo Directory { get; }
            public Version? Version { get; }
            public string? RawVersion { get; }
            public DateTime LastWriteTimeUtc { get; }
        }
    }
}
