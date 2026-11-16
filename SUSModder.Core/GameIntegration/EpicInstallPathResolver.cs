using System.IO;

namespace SUSModder.Core.GameIntegration;

public static class EpicInstallPathResolver
{
    public static string GetExpectedGameExePath(string baseInstallDirectory) =>
        Path.Combine(baseInstallDirectory, "AmongUs", "Among Us.exe");

    public static string GetVanillaExpectedExePath(string modsInstallPath) =>
        Path.Combine(modsInstallPath, "Among Us - Vanilla", "AmongUs", "Among Us.exe");

    public static string GetBaseInstallDirectory(string? savedInstallPath, string modsInstallPath, string? modName)
    {
        var path = string.IsNullOrWhiteSpace(savedInstallPath)
            ? Path.Combine(modsInstallPath, modName ?? string.Empty)
            : savedInstallPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(Path.GetFileName(path), "AmongUs", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(path) ?? path
            : path;
    }
}
